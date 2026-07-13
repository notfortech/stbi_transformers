using System.Text;
using System.Text.Json;
using DashboardAgents.Api.Models;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Llm;

namespace DashboardAgents.Api.Services;

public interface ISchemaModelMatchingService
{
    Task<SchemaModelMatchResult> MatchAsync(SchemaModelMatchRequest request, CancellationToken cancellationToken = default);
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

    public SchemaModelMatchingService(ILlmClient llm, ILogger<SchemaModelMatchingService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<SchemaModelMatchResult> MatchAsync(SchemaModelMatchRequest request, CancellationToken cancellationToken = default)
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
            "Match:   {\"matchedModelId\": \"<id from the candidate list>\", \"confidence\": <0..1>, \"reasoning\": \"<one sentence>\"}\n" +
            "Propose: {\"proposedModel\": {\"name\": \"<short name>\", \"industry\": \"<industry>\", " +
            "\"templateName\": \"<short dashboard name, e.g. '<name> — Overview'>\", " +
            "\"fields\": [{\"fieldName\": \"<name>\", \"dataType\": \"string|decimal|date|datetime|int|bool\", \"isRequired\": <bool>}]}, " +
            "\"confidence\": 0, \"reasoning\": \"<one sentence explaining why nothing in the directory fit>\"}\n" +
            "Only propose a new model if the best candidate's semantic fit is genuinely weak — prefer matching. " +
            "templateName is just a label — do not design dashboard layout/sections, koru-main handles that separately.";

        var userPrompt = BuildUserPrompt(request);

        _logger.LogInformation(
            "SchemaModelMatch.Requested ColumnCount={ColumnCount} CandidateCount={CandidateCount}",
            request.Columns.Count, request.CandidateModels.Count);

        var rawResponse = await _llm.CompleteAsync(systemPrompt, userPrompt, cancellationToken);

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
            throw;
        }

        if (string.IsNullOrWhiteSpace(result.MatchedModelId) && result.ProposedModel is null)
            throw new BlueprintParseException("Schema model match result had neither a matchedModelId nor a proposedModel.");

        if (!string.IsNullOrWhiteSpace(result.MatchedModelId) &&
            !request.CandidateModels.Any(c => c.Id == result.MatchedModelId))
        {
            _logger.LogWarning(
                "SchemaModelMatch.HallucinatedId MatchedModelId={MatchedModelId} — treating as no match.",
                result.MatchedModelId);
            throw new BlueprintParseException("AI returned a matchedModelId that was not in the candidate list.");
        }

        _logger.LogInformation(
            "SchemaModelMatch.Completed MatchedModelId={MatchedModelId} ProposedNew={ProposedNew} Confidence={Confidence}",
            result.MatchedModelId ?? "(none)", result.ProposedModel is not null, result.Confidence);

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
