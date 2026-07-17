namespace DashboardAgents.Llm;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>
    /// Explicit switch — set via the ANTHROPIC_ENABLED App Setting. A provider is only ever
    /// selected because this is true, never because an API key happens to be present (that
    /// ambiguity is what previously caused stbi-blueprint-api to silently call Anthropic and hit
    /// a billing 402 even though only OpenAI was intended).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Read from configuration/environment — never hard-code. See appsettings.json comments.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// Confirmed via production diagnostics (2026-07-17): after AnthropicClient started sending
    /// `thinking: {"type": "disabled"}`, the model finally returned real text (BlockTypes=text),
    /// but a genuinely large NDIS blueprint (5 capability domains, multiple fact tables) still hit
    /// StopReason=max_tokens at ~20000 characters and got truncated mid-JSON. Sonnet 5's newer
    /// tokenizer is denser for structured/JSON content than plain English prose, so a full
    /// multi-domain blueprint can legitimately need well more than 16000 tokens. Raised to 32000
    /// (Sonnet 5 supports up to 128K max output) — check for a stale ANTHROPIC_MAX_TOKENS App
    /// Setting on the App Service too; if one was copied over from before this default was raised,
    /// it silently overrides this value.
    /// </summary>
    public int MaxTokens { get; set; } = 32000;

    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    public string ApiVersion { get; set; } = "2023-06-01";
}
