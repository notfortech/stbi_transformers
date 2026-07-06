namespace DashboardAgents.Llm;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    /// <summary>Read from configuration or OPENAI_API_KEY environment variable — never hard-code.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "gpt-4o";

    public int MaxTokens { get; set; } = 8000;

    /// <summary>Sampling temperature (0 = deterministic, 1 = creative). Blueprint generation should stay low.</summary>
    public double Temperature { get; set; } = 0.2;

    public string BaseUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
}
