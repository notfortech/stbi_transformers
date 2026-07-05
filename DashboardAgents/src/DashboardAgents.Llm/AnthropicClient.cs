using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardAgents.Llm;

public interface IAnthropicClient
{
    /// <summary>
    /// Sends a single-turn completion request (system prompt + one user message) and returns
    /// the concatenated text content of the response. Both agent projects use this as their
    /// only integration point with the model provider.
    /// </summary>
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default);
}

public sealed class AnthropicClient : IAnthropicClient
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicClient> _logger;

    public AnthropicClient(HttpClient httpClient, IOptions<AnthropicOptions> options, ILogger<AnthropicClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Anthropic API key is not configured. Set Anthropic:ApiKey via configuration or the ANTHROPIC_API_KEY environment variable.");
        }
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        var request = new AnthropicRequest
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = systemPrompt,
            Messages = new List<AnthropicMessage>
            {
                new() { Role = "user", Content = userMessage }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl);
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", _options.ApiVersion);
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API call failed with status {Status}: {Body}", response.StatusCode, body);
            throw new AnthropicApiException($"Anthropic API returned {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<AnthropicResponse>(body, JsonOptions)
            ?? throw new AnthropicApiException("Anthropic API returned an unparseable response.");

        return string.Join("\n", parsed.Content
            .Where(c => c.Type == "text")
            .Select(c => c.Text));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class AnthropicRequest
    {
        public string Model { get; set; } = "";
        public int MaxTokens { get; set; }
        public string System { get; set; } = "";
        public List<AnthropicMessage> Messages { get; set; } = new();
    }

    private sealed class AnthropicMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    private sealed class AnthropicResponse
    {
        public List<AnthropicContentBlock> Content { get; set; } = new();
    }

    private sealed class AnthropicContentBlock
    {
        public string Type { get; set; } = "";
        public string Text { get; set; } = "";
    }
}

public sealed class AnthropicApiException : Exception
{
    public AnthropicApiException(string message) : base(message) { }
}
