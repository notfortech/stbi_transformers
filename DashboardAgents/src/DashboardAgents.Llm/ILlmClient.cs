namespace DashboardAgents.Llm;

/// <summary>
/// Provider-agnostic LLM completion contract. Both agent projects depend on this interface only —
/// the concrete provider (OpenAI or Anthropic) is resolved at startup via configuration.
/// </summary>
public interface ILlmClient
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default);
}
