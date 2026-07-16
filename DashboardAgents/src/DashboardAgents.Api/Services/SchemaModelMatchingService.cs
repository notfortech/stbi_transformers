using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DashboardAgents.Api.Models;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Core.Services;
using DashboardAgents.Llm;

namespace DashboardAgents.Api.Services;

public interface ISchemaModelMatchingService
{
    Task<SchemaModelMatchResult> MatchAsync(
        SchemaModelMatchRequest request, string correlationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The AI step in the schema/model/template matching pipeline: given a client's column
/// headers/types and koru-main's Approved SchemaModel directory, semantically match to
/// the best-fitting model (fields can be conceptually equivalent without matching by
/// literal string, unlike koru-main's own deterministic name-overlap scoring) or propose
/// a new model when nothing in the directory fits well. Headers/types only — no row data
/// is ever part of this request.
/// </summary>
public sealed class SchemaModelMatchingService : ISchemaModelMatchingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILlmClient _llm;
    private readonly ILogger<SchemaModelMatchingService> _logger;
    private readonly IAiBoundaryAuditPublisher _audit;

    public SchemaModelMatchingService(ILlmClient llm, ILogger<SchemaModelMatchingService> logger, IAiBoundaryAuditPublisher audit)
    {
        _llm = llm;
        _logger = logger;
        _audit = audit;
    }

    public async Task<SchemaModelMatchResult> MatchAsync(
        SchemaModelMatchRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        if (request.Columns.Count == 0)
            throw new ArgumentException("At least one column is required.");

        var systemPrompt =
            "You are a data-modeling assistant for a BI platform. You are given a connecting " +
            "client's column names/types and a directory of candidate reference data models " +
            "(each with a name, industry, and expected fields). Decide whether one candidate is " +
            "a good semantic fit for the client's columns (field names may differ from the " +
            "client's column names but mean the same thing — e.g. \"Revenue\" and \"total_sales\") " +
            "or whether none fit well enough and a new model should be proposed instead.\n\n" +
            "Respond with ONLY a single JSON object, no prose, no markdown fences, matching exactly " +
            "one of these two shapes:\n" +
            "Match:   {\"matchedModelId\": \"<id from the candidate list>\", \"confidence\": <0..1>, " +
            "\"fieldMappings\": [{\"fieldName\": \"<one of the matched candidate's field names>\", " +
            "\"clientColumnName\": \"<the client column that means the same thing, or null if none does>\"}, " +
            "...one entry for EVERY field of the matched candidate, in the same order they were given...], " +
            "\"reasoning\": \"<one sentence>\"}\n" +
            "Propose: {\"proposedModel\": {\"name\": \"<short name>\", \"industry\": \"<industry>\", " +
            "\"templateName\": \"<short dashboard name, e.g. '<name> — Overview'>\", " +
            "\"fields\": [{\"fieldName\": \"<name>\", \"dataType\": \"string|decimal|date|datetime|int|bool\", \"isRequired\": <bool>}]}, " +
            "\"confidence\": 0, \"reasoning\": \"<one sentence explaining why nothing in the directory fit>\"}\n" +
            "Only propose a new model if the best candidate's semantic fit is genuinely weak — prefer matching. " +
            "templateName is just a label — do not design dashboard layout/sections, koru-main handles that separately. " +
            "fieldMappings is only relevant for a Match response — omit it entirely for a Propose response.";

        var userPrompt = BuildUserPrompt(request);

        _logger.LogInformation(
            "SchemaModelMatch.Requested ColumnCount={ColumnCount} CandidateCount={CandidateCount}",
            request.Columns.Count, request.CandidateModels.Count);

        await _audit.LogSentAsync(
            "SchemaModelMatchingService", "MatchSchemaModel", correlationId, _llm.ProviderName,
            new { columnCount = request.Columns.Count, candidateCount = request.CandidateModels.Count },
            cancellationToken);

        var sw = Stopwatch.StartNew();
        string rawResponse;
        try
        {
            rawResponse = await _llm.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            await _audit.LogFailedAsync(
                "SchemaModelMatchingService", "MatchSchemaModel", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, $"LLM call failed: {ex.GetType().Name}", cancellationToken);
            throw;
        }
        sw.Stop();

        SchemaModelMatchResult result;
        try
        {
            using var doc = JsonExtraction.ExtractJsonDocument(rawResponse);
            result = JsonSerializer.Deserialize<SchemaModelMatchResult>(doc.RootElement.GetRawText(), JsonOptions)
                ?? throw new BlueprintParseException("Deserialized schema model match result was null.");
        }
        catch (BlueprintParseException ex)
        {
            _logger.LogError(ex, "SchemaModelMatch.ParseFailed");
            await _audit.LogFailedAsync(
                "SchemaModelMatchingService", "MatchSchemaModel", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, "Response could not be parsed.", cancellationToken);
            throw;
        }

        if (string.IsNullOrWhiteSpace(result.MatchedModelId) && result.ProposedModel is null)
        {
            await _audit.LogFailedAsync(
                "SchemaModelMatchingService", "MatchSchemaModel", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, "Result had neither a matchedModelId nor a proposedModel.", cancellationToken);
            throw new BlueprintParseException("Schema model match result had neither a matchedModelId nor a proposedModel.");
        }

        if (!string.IsNullOrWhiteSpace(result.MatchedModelId) &&
            !request.CandidateModels.Any(c => c.Id == result.MatchedModelId))
        {
            _logger.LogWarning(
                "SchemaModelMatch.HallucinatedId MatchedModelId={MatchedModelId} — treating as no match.",
                result.MatchedModelId);
            // This is exactly the kind of event the audit trail exists to prove happened —
            // the AI proposed something outside its allowed candidate set and it was rejected,
            // not silently accepted.
            await _audit.LogFailedAsync(
                "SchemaModelMatchingService", "MatchSchemaModel", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, $"Hallucinated matchedModelId '{result.MatchedModelId}' rejected.", cancellationToken);
            throw new BlueprintParseException("AI returned a matchedModelId that was not in the candidate list.");
        }

        // fieldMappings is best-effort — the model may omit it despite the prompt, or name a
        // field that isn't actually on the matched candidate (hallucination). Drop anything
        // that doesn't correspond to a real field rather than failing the whole match over it;
        // the caller (koru-main) already falls back to its own exact-name matching per field.
        if (!string.IsNullOrWhiteSpace(result.MatchedModelId) && result.FieldMappings is { Count: > 0 })
        {
            var matchedCandidate = request.CandidateModels.First(c => c.Id == result.MatchedModelId);
            var validFieldNames = matchedCandidate.Fields.Select(f => f.FieldName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var validColumnNames = request.Columns.Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var before = result.FieldMappings.Count;
            result.FieldMappings = result.FieldMappings
                .Where(fm => validFieldNames.Contains(fm.FieldName) &&
                             (fm.ClientColumnName is null || validColumnNames.Contains(fm.ClientColumnName)))
                .ToList();

            if (result.FieldMappings.Count != before)
                _logger.LogWarning(
                    "SchemaModelMatch.DroppedInvalidFieldMappings Before={Before} After={After} MatchedModelId={MatchedModelId}",
                    before, result.FieldMappings.Count, result.MatchedModelId);
        }

        _logger.LogInformation(
            "SchemaModelMatch.Completed MatchedModelId={MatchedModelId} ProposedNew={ProposedNew} Confidence={Confidence} FieldMappingCount={FieldMappingCount}",
            result.MatchedModelId ?? "(none)", result.ProposedModel is not null, result.Confidence, result.FieldMappings?.Count ?? 0);

        await _audit.LogReceivedAsync(
            "SchemaModelMatchingService", "MatchSchemaModel", correlationId, _llm.ProviderName,
            new
            {
                matchedModelId = result.MatchedModelId,
                proposedNew = result.ProposedModel is not null,
                confidence = result.Confidence,
                fieldMappingCount = result.FieldMappings?.Count ?? 0
            },
            sw.ElapsedMilliseconds, cancellationToken);

        return result;
    }

    private static string BuildUserPrompt(SchemaModelMatchRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Client columns:");
        foreach (var col in request.Columns)
            sb.AppendLine($"- {col.ColumnName} ({col.DataType ?? "unknown"})");

        sb.AppendLine();
        sb.AppendLine("Candidate models:");
        foreach (var model in request.CandidateModels)
        {
            sb.AppendLine($"- id={model.Id} name=\"{model.Name}\" industry=\"{model.Industry}\"");
            foreach (var field in model.Fields)
                sb.AppendLine($"    field: {field.FieldName} ({field.DataType}){(field.IsRequired ? " [required]" : "")}");
        }

        return sb.ToString();
    }
}
