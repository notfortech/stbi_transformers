using System.Diagnostics;
using System.Text.Json;
using DashboardAgents.BlueprintAgent;
using DashboardAgents.Core.Models;
using DashboardAgents.Core.Services;
using DashboardAgents.Llm;
using Microsoft.Extensions.Logging;

namespace DashboardAgents.TweakAgent;

public interface IUseCaseTweakService
{
    Task<TweakResult> AdaptAsync(
        Blueprint blueprint, string scenario, string correlationId, CancellationToken cancellationToken = default);
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

    private readonly ILlmClient _llm;
    private readonly ILogger<UseCaseTweakService> _logger;
    private readonly IAiBoundaryAuditPublisher _audit;

    public UseCaseTweakService(ILlmClient llm, ILogger<UseCaseTweakService> logger, IAiBoundaryAuditPublisher audit)
    {
        _llm = llm;
        _logger = logger;
        _audit = audit;
    }

    public async Task<TweakResult> AdaptAsync(
        Blueprint blueprint, string scenario, string correlationId, CancellationToken cancellationToken = default)
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

        await _audit.LogSentAsync(
            "TweakAgent", "AdaptBlueprint", correlationId, _llm.ProviderName,
            new { blueprintId = blueprint.BlueprintId }, cancellationToken);

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
                "TweakAgent", "AdaptBlueprint", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, $"LLM call failed: {ex.GetType().Name}", cancellationToken);
            throw;
        }
        sw.Stop();

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
            await _audit.LogFailedAsync(
                "TweakAgent", "AdaptBlueprint", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, $"Output failed allow-list validation: {validation.Violations.Count} violation(s)",
                cancellationToken);
            throw new TweakValidationException(validation.Violations);
        }

        await _audit.LogReceivedAsync(
            "TweakAgent", "AdaptBlueprint", correlationId, _llm.ProviderName,
            new { mode = result.Mode, fieldsUsedCount = result.FieldsUsed?.Count ?? 0 }, sw.ElapsedMilliseconds,
            cancellationToken);

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
