using System.Diagnostics;
using System.Text.Json;
using DashboardAgents.Core.Models;
using DashboardAgents.Core.Services;
using DashboardAgents.Llm;
using Microsoft.Extensions.Logging;

namespace DashboardAgents.BlueprintAgent;

public interface IBlueprintGenerationService
{
    Task<Blueprint> GenerateAsync(
        BlueprintGenerationOptions options, string correlationId, CancellationToken cancellationToken = default);
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

    private readonly ILlmClient _llm;
    private readonly ILogger<BlueprintGenerationService> _logger;
    private readonly IAiBoundaryAuditPublisher _audit;

    public BlueprintGenerationService(ILlmClient llm, ILogger<BlueprintGenerationService> logger, IAiBoundaryAuditPublisher audit)
    {
        _llm = llm;
        _logger = logger;
        _audit = audit;
    }

    public async Task<Blueprint> GenerateAsync(
        BlueprintGenerationOptions options, string correlationId, CancellationToken cancellationToken = default)
    {
        if (options.Mode == "requirements" && string.IsNullOrWhiteSpace(options.Requirements)
            || options.Mode == "schema" && string.IsNullOrWhiteSpace(options.SchemaText))
        {
            throw new ArgumentException("Business requirements or a dataset schema must be provided before generating a blueprint.");
        }

        var systemPrompt = SystemPromptBuilder.Build(options);
        var userPrompt = UserPromptBuilder.Build(options);

        _logger.LogInformation("Requesting blueprint generation (mode={Mode}, industryOverride={Industry})",
            options.Mode, options.IndustryExplicit ?? "(auto-detect)");

        await _audit.LogSentAsync(
            "BlueprintGenerator", "GenerateBlueprint", correlationId, _llm.ProviderName,
            new { mode = options.Mode, industryOverride = options.IndustryExplicit ?? "(auto-detect)" },
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
                "BlueprintGenerator", "GenerateBlueprint", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, $"LLM call failed: {ex.GetType().Name}", cancellationToken);
            throw;
        }
        sw.Stop();

        using var doc = JsonExtraction.ExtractJsonDocument(rawResponse);
        var blueprint = JsonSerializer.Deserialize<Blueprint>(doc.RootElement.GetRawText(), JsonOptions)
            ?? throw new BlueprintParseException("Deserialized blueprint was null.");

        var validation = BlueprintValidator.Validate(blueprint);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Generated blueprint failed validation with {Count} violations: {Violations}",
                validation.Violations.Count, string.Join(" | ", validation.Violations));

            await _audit.LogFailedAsync(
                "BlueprintGenerator", "GenerateBlueprint", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, $"Blueprint failed validation: {validation.Violations.Count} violation(s)",
                cancellationToken);

            // Fail closed rather than silently serving a blueprint that violates its own schema
            // contract (e.g. missing KPI owners, or fewer than the required 9 self-review gates).
            throw new BlueprintValidationException(validation.Violations);
        }

        blueprint.BlueprintId = Guid.NewGuid().ToString("N");

        await _audit.LogReceivedAsync(
            "BlueprintGenerator", "GenerateBlueprint", correlationId, _llm.ProviderName,
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
