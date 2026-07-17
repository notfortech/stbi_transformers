using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardAgents.Llm;

/// <summary>Kept for backwards compatibility — <see cref="ILlmClient"/> is the preferred dependency.</summary>
public interface IAnthropicClient : ILlmClient { }

public sealed class AnthropicClient : IAnthropicClient
{
    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicClient> _logger;

    public string ProviderName => "Anthropic";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicClient(HttpClient http, IOptions<AnthropicOptions> options, ILogger<AnthropicClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            _logger.LogWarning("Anthropic API key is not configured. Set Anthropic:ApiKey or the ANTHROPIC_API_KEY environment variable.");
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
        httpRequest.Content = JsonContent.Create(request, options: JsonOpts);

        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API call failed {Status}: {Body}", response.StatusCode, body);
            throw new LlmProviderException($"Anthropic returned {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<AnthropicResponse>(body, JsonOpts)
            ?? throw new LlmProviderException("Anthropic returned an unparseable response.");

        var text = string.Join("\n", parsed.Content
            .Where(c => c.Type == "text")
            .Select(c => c.Text));

        // StopReason is the single most useful signal for diagnosing a downstream JSON-parse
        // failure: "max_tokens" means the response was cut off mid-generation (raise MaxTokens),
        // vs "end_turn" meaning the model completed normally but produced something unparseable
        // (a prompt/formatting problem, not a length problem). Logged on every call, not just
        // failures, since it's cheap and the two causes are otherwise indistinguishable from the
        // caller's side.
        _logger.LogInformation(
            "Anthropic response received. StopReason={StopReason} ContentBlocks={ContentBlocks} TextLength={TextLength} BlockTypes={BlockTypes}",
            parsed.StopReason ?? "(none)", parsed.Content.Count, text.Length,
            string.Join(",", parsed.Content.Select(c => c.Type)));

        return text;
    }

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
        public string? StopReason { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        public string Type { get; set; } = "";
        public string Text { get; set; } = "";
    }
}

public sealed class AnthropicApiException : LlmProviderException
{
    public AnthropicApiException(string message) : base(message) { }
}
