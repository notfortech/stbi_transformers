namespace DashboardAgents.Llm;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>Read from configuration/environment — never hard-code. See appsettings.json comments.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// Confirmed via production diagnostics (2026-07-17): blueprint generation was hitting this
    /// cap with StopReason=max_tokens and zero characters of actual text output — the model's
    /// response contained a single non-text content block (consistent with internal reasoning)
    /// that alone exhausted the entire 8000-token budget before any JSON could be emitted.
    /// Doubled as a first step; raise further via ANTHROPIC_MAX_TOKENS if StopReason=max_tokens
    /// still shows up in AnthropicClient's per-call log line.
    /// </summary>
    public int MaxTokens { get; set; } = 16000;

    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    public string ApiVersion { get; set; } = "2023-06-01";
}
