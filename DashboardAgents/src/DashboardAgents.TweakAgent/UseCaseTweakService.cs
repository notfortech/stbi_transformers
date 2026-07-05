using System.Text.Json;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Core.Models;
using DashboardAgents.Llm;
using Microsoft.Extensions.Logging;

namespace DashboardAgents.TweakAgent;

public interface IUseCaseTweakService
{
    Task<TweakResult> AdaptAsync(Blueprint blueprint, string scenario, CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates the use-case adaptation pipeline: build an explicit field allow-list from the
/// existing blueprint, ask the LLM to either match an existing page or compose a new one built
/// only from allow-listed fields, then validate the response never invented a field.
/// </summary>
public sealed class UseCaseTweakService : IUseCaseTweakService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAnthropicClient _llm;
    private readonly ILogger<UseCaseTweakService> _logger;

    public UseCaseTweakService(IAnthropicClient llm, ILogger<UseCaseTweakService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<TweakResult> AdaptAsync(Blueprint blueprint, string scenario, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scenario))
            throw new ArgumentException("A use-case scenario must be provided.", nameof(scenario));

        var allowlist = FieldAllowlistBuilder.Build(blueprint);

        var systemPrompt = TweakPromptLoader.Instructions;
        var userPrompt = $"""
            {allowlist.PromptText}

            USE-CASE SCENARIO TO ADDRESS:
            {scenario}

            Decide whether an existing page answers this scenario ("matched_existing") or whether
            you need to compose one new page from allow-listed fields only ("composed_new").
            Return the exact JSON shape described in your instructions — nothing else.
            """;

        _logger.LogInformation("Adapting blueprint {BlueprintId} to scenario: {Scenario}", blueprint.BlueprintId, scenario);

        var rawResponse = await _llm.CompleteAsync(systemPrompt, userPrompt, cancellationToken);

        using var doc = JsonExtraction.ExtractJsonDocument(rawResponse);
        var parsed = JsonSerializer.Deserialize<TweakAgentResponse>(doc.RootElement.GetRawText(), JsonOptions)
            ?? throw new BlueprintParseException("Tweak agent response was null after parsing.");

        var result = new TweakResult
        {
            Mode = parsed.Mode,
            Pages = parsed.Pages,
            Explanation = parsed.Explanation,
            FieldsUsed = parsed.FieldsUsed
        };

        var validation = TweakOutputValidator.Validate(result, allowlist);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Tweak agent output failed field allow-list validation: {Violations}",
                string.Join(" | ", validation.Violations));
            throw new TweakValidationException(validation.Violations);
        }

        return result;
    }
}

public sealed class TweakValidationException : Exception
{
    public IReadOnlyList<string> Violations { get; }

    public TweakValidationException(IReadOnlyList<string> violations)
        : base("Tweak agent output referenced fields outside the blueprint's allow-list: " + string.Join(" | ", violations))
    {
        Violations = violations;
    }
}
