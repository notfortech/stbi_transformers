namespace DashboardAgents.Llm;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>Read from configuration/environment — never hard-code. See appsettings.json comments.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// Confirmed via production diagnostics (2026-07-17): blueprint generation was hitting this
    /// cap with StopReason=max_tokens, TextLength=0, and BlockTypes=thinking — claude-sonnet-5
    /// defaults to adaptive extended thinking when the request omits `thinking` entirely, and a
    /// single "thinking" content block consumed the whole MaxTokens budget before any JSON output
    /// was emitted, at both 8000 and 16000. Raising MaxTokens alone does not fix this (a bigger
    /// budget just gets consumed by more thinking) — the real fix is AnthropicClient explicitly
    /// sending `thinking: {"type": "disabled"}`. Left at 16000 as headroom for actual JSON output.
    /// </summary>
    public int MaxTokens { get; set; } = 16000;

    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    public string ApiVersion { get; set; } = "2023-06-01";
}
