namespace DashboardAgents.Llm;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>Read from configuration/environment — never hard-code. See appsettings.json comments.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "claude-sonnet-5";

    public int MaxTokens { get; set; } = 8000;

    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    public string ApiVersion { get; set; } = "2023-06-01";
}
