using DashboardAgents.Llm;
using Microsoft.Extensions.Options;

namespace DashboardAgents.Api.Services;

public sealed class LlmClientFactory : ILlmClientFactory
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private readonly OpenAiOptions _openAi;
    private readonly AnthropicOptions _anthropic;

    public LlmClientFactory(
        IServiceProvider sp,
        IConfiguration cfg,
        IOptions<OpenAiOptions> openAi,
        IOptions<AnthropicOptions> anthropic)
    {
        _sp = sp;
        _cfg = cfg;
        _openAi = openAi.Value;
        _anthropic = anthropic.Value;
    }

    public ILlmClient Resolve(string? explicitProvider = null)
    {
        var normalized = explicitProvider?.Trim().ToLowerInvariant();

        if (normalized is "anthropic" or "openai")
        {
            var enabled = normalized == "anthropic" ? _anthropic.Enabled : _openAi.Enabled;
            if (!enabled)
                throw new InvalidOperationException(
                    $"Requested aiProvider=\"{normalized}\" but {(normalized == "anthropic" ? "Anthropic" : "OpenAI")}:Enabled is false.");

            return normalized == "anthropic"
                ? _sp.GetRequiredService<AnthropicClient>()
                : _sp.GetRequiredService<OpenAiClient>();
        }

        return ResolveDefault();
    }

    // Explicit Enabled flag decides which provider runs — never inferred from API-key presence.
    // That ambiguity (provider chosen by whichever key happened to be configured, defaulting to
    // Anthropic if both were present) previously let a leftover Llm:Provider=anthropic App
    // Setting silently route stbi-blueprint-api to Anthropic and hit a billing 402, even though
    // only OpenAI was intended. Set OPENAI_ENABLED=true or ANTHROPIC_ENABLED=true as an explicit
    // Azure App Setting — Llm:Provider (LLM_PROVIDER) only matters as a tie-breaker if both are
    // enabled at once.
    private ILlmClient ResolveDefault()
    {
        if (_openAi.Enabled && _anthropic.Enabled)
        {
            var tieBreak = _cfg["Llm:Provider"]?.Trim().ToLowerInvariant();
            if (tieBreak == "anthropic") return _sp.GetRequiredService<AnthropicClient>();
            if (tieBreak == "openai") return _sp.GetRequiredService<OpenAiClient>();
            throw new InvalidOperationException(
                "Both OpenAI:Enabled and Anthropic:Enabled are true — set Llm:Provider " +
                "(LLM_PROVIDER App Setting) to \"openai\" or \"anthropic\" to disambiguate.");
        }
        if (_openAi.Enabled) return _sp.GetRequiredService<OpenAiClient>();
        if (_anthropic.Enabled) return _sp.GetRequiredService<AnthropicClient>();

        throw new InvalidOperationException(
            "No LLM provider is enabled — set OPENAI_ENABLED=true or ANTHROPIC_ENABLED=true " +
            "as an Azure App Setting.");
    }
}
