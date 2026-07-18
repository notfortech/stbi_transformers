namespace DashboardAgents.Llm;

/// <summary>
/// Resolves which <see cref="ILlmClient"/> a given call should use. The server-side
/// OpenAI:Enabled / Anthropic:Enabled configuration decides which providers are allowed to run
/// at all; this factory layers an optional per-call override on top of that, so a caller can
/// pick among whatever's enabled without bypassing the Enabled gate.
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>
    /// Resolves the LLM client to use. <paramref name="explicitProvider"/> ("anthropic" |
    /// "openai"), when given, must be Enabled=true server-side or this throws
    /// <see cref="InvalidOperationException"/> — an explicit request for a disabled provider
    /// fails loudly, it never silently falls back to a different one. Null/omitted uses the
    /// existing Enabled-flag/tie-breaker default.
    /// </summary>
    ILlmClient Resolve(string? explicitProvider = null);
}
