namespace DashboardAgents.Llm;

public class LlmProviderException : Exception
{
    public LlmProviderException(string message) : base(message) { }
    public LlmProviderException(string message, Exception inner) : base(message, inner) { }
}
