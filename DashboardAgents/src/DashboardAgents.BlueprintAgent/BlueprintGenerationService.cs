using System.Text.Json;
using DashboardAgents.Core.Models;
using DashboardAgents.Llm;
using Microsoft.Extensions.Logging;

namespace DashboardAgents.BlueprintAgent;

public interface IBlueprintGenerationService
{
    Task<Blueprint> GenerateAsync(BlueprintGenerationOptions options, CancellationToken cancellationToken = default);
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

    private readonly IAnthropicClient _llm;
    private readonly ILogger<BlueprintGenerationService> _logger;

    public BlueprintGenerationService(IAnthropicClient llm, ILogger<BlueprintGenerationService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<Blueprint> GenerateAsync(BlueprintGenerationOptions options, CancellationToken cancellationToken = default)
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

        var rawResponse = await _llm.CompleteAsync(systemPrompt, userPrompt, cancellationToken);

        using var doc = JsonExtraction.ExtractJsonDocument(rawResponse);
        var blueprint = JsonSerializer.Deserialize<Blueprint>(doc.RootElement.GetRawText(), JsonOptions)
            ?? throw new BlueprintParseException("Deserialized blueprint was null.");

        var validation = BlueprintValidator.Validate(blueprint);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Generated blueprint failed validation with {Count} violations: {Violations}",
                validation.Violations.Count, string.Join(" | ", validation.Violations));

            // Fail closed rather than silently serving a blueprint that violates its own schema
            // contract (e.g. missing KPI owners, or fewer than the required 9 self-review gates).
            throw new BlueprintValidationException(validation.Violations);
        }

        blueprint.BlueprintId = Guid.NewGuid().ToString("N");
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
