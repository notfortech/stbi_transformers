using System.Diagnostics;
using System.Text.Json;
using DashboardAgents.Core.Models;
using DashboardAgents.Core.Services;
using DashboardAgents.Llm;
using Microsoft.Extensions.Logging;

namespace DashboardAgents.BlueprintAgent;

public interface IBlueprintGenerationService
{
    // aiProviderOverride is deliberately the LAST parameter (after cancellationToken, which was
    // already here) so existing 3-positional-argument call sites (options, correlationId,
    // cancellationToken) keep compiling unchanged — only a caller that wants an override needs
    // to add a 4th argument.
    Task<Blueprint> GenerateAsync(
        BlueprintGenerationOptions options, string correlationId, CancellationToken cancellationToken = default,
        string? aiProviderOverride = null);
}

/// <summary>
/// Orchestrates the full blueprint generation pipeline: build system + user prompt from the
/// same design docs the original tool used, call the LLM, parse the JSON, and validate it
/// against the mandatory-field contract before handing it back.
/// </summary>
public sealed class BlueprintGenerationService : IBlueprintGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILlmClientFactory _llmFactory;
    private readonly ILogger<BlueprintGenerationService> _logger;
    private readonly IAiBoundaryAuditPublisher _audit;

    public BlueprintGenerationService(ILlmClientFactory llmFactory, ILogger<BlueprintGenerationService> logger, IAiBoundaryAuditPublisher audit)
    {
        _llmFactory = llmFactory;
        _logger = logger;
        _audit = audit;
    }

    public async Task<Blueprint> GenerateAsync(
        BlueprintGenerationOptions options, string correlationId, CancellationToken cancellationToken = default,
        string? aiProviderOverride = null)
    {
        if (options.Mode == "requirements" && string.IsNullOrWhiteSpace(options.Requirements)
            || options.Mode == "schema" && string.IsNullOrWhiteSpace(options.SchemaText))
        {
            throw new ArgumentException("Business requirements or a dataset schema must be provided before generating a blueprint.");
        }

        var llm = _llmFactory.Resolve(aiProviderOverride);

        var systemPrompt = SystemPromptBuilder.Build(options);
        var userPrompt = UserPromptBuilder.Build(options);

        _logger.LogInformation("Requesting blueprint generation (mode={Mode}, industryOverride={Industry})",
            options.Mode, options.IndustryExplicit ?? "(auto-detect)");

        await _audit.LogSentAsync(
            "BlueprintGenerator", "GenerateBlueprint", correlationId, llm.ProviderName,
            new { mode = options.Mode, industryOverride = options.IndustryExplicit ?? "(auto-detect)" },
            cancellationToken);

        var sw = Stopwatch.StartNew();
        string rawResponse;
        try
        {
            rawResponse = await llm.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            await _audit.LogFailedAsync(
                "BlueprintGenerator", "GenerateBlueprint", correlationId, llm.ProviderName,
                sw.ElapsedMilliseconds, $"LLM call failed: {ex.GetType().Name}", cancellationToken);
            throw;
        }
        sw.Stop();

        Blueprint blueprint;
        try
        {
            using var doc = JsonExtraction.ExtractJsonDocument(rawResponse);
            blueprint = JsonSerializer.Deserialize<Blueprint>(doc.RootElement.GetRawText(), JsonOptions)
                ?? throw new BlueprintParseException("Deserialized blueprint was null.");
        }
        catch (BlueprintParseException ex)
        {
            // This previously threw straight out of GenerateAsync with no diagnostic beyond the
            // generic "did not contain a recognizable JSON object" message, and no audit record —
            // the raw response was never logged anywhere, so every prior occurrence was
            // undiagnosable after the fact. Logging a capped preview here (this is the LLM's own
            // generated blueprint text, not client data — same trust level as everything else in
            // this audit trail) turns the next occurrence into an actual diagnosis instead of a
            // repeat of the same open question.
            const int previewLength = 1500;
            var preview = rawResponse.Length <= previewLength
                ? rawResponse
                : rawResponse[..previewLength] + $"... [truncated, full length {rawResponse.Length}]";
            _logger.LogError(ex,
                "Blueprint JSON parse failed. RawResponseLength={Length} RawResponsePreview={Preview}",
                rawResponse.Length, preview);

            await _audit.LogFailedAsync(
                "BlueprintGenerator", "GenerateBlueprint", correlationId, llm.ProviderName,
                sw.ElapsedMilliseconds, $"JSON parse failed: {ex.Message}", cancellationToken);

            throw;
        }

        var validation = BlueprintValidator.Validate(blueprint, options.SourceTableCount);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Generated blueprint failed validation with {Count} violations: {Violations}",
                validation.Violations.Count, string.Join(" | ", validation.Violations));

            await _audit.LogFailedAsync(
                "BlueprintGenerator", "GenerateBlueprint", correlationId, llm.ProviderName,
                sw.ElapsedMilliseconds, $"Blueprint failed validation: {validation.Violations.Count} violation(s)",
                cancellationToken);

            // Fail closed rather than silently serving a blueprint that violates its own schema
            // contract (e.g. missing KPI owners, or fewer than the required 9 self-review gates).
            throw new BlueprintValidationException(validation.Violations);
        }

        blueprint.BlueprintId = Guid.NewGuid().ToString("N");

        await _audit.LogReceivedAsync(
            "BlueprintGenerator", "GenerateBlueprint", correlationId, llm.ProviderName,
            new { blueprintId = blueprint.BlueprintId }, sw.ElapsedMilliseconds, cancellationToken);

        return blueprint;
    }
}

public sealed class BlueprintValidationException : Exception
{
    public IReadOnlyList<string> Violations { get; }

    public BlueprintValidationException(IReadOnlyList<string> violations)
        : base("Generated blueprint failed mandatory-field validation: " + string.Join(" | ", violations))
    {
        Violations = violations;
    }
}
