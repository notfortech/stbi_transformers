using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardAgents.Llm;

public sealed class OpenAiClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiClient(HttpClient http, IOptions<OpenAiOptions> options, ILogger<OpenAiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            _logger.LogWarning("OpenAI API key is not configured. Set OpenAI:ApiKey or the OPENAI_API_KEY environment variable.");
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        var payload = new ChatRequest
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature,
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user",   Content = userMessage  }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        req.Content = JsonContent.Create(payload, options: JsonOpts);

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI API call failed {Status}: {Body}", resp.StatusCode, body);
            throw new LlmProviderException($"OpenAI returned {(int)resp.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? throw new LlmProviderException("OpenAI returned an empty content block.");
    }

    // ── Wire types ───────────────────────────────────────────────────────────

    private sealed class ChatRequest
    {
        public string Model { get; set; } = "";
        public int MaxTokens { get; set; }
        public double Temperature { get; set; }
        public List<ChatMessage> Messages { get; set; } = new();
    }

    private sealed class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }
}

public sealed class LlmProviderException : Exception
{
    public LlmProviderException(string message) : base(message) { }
}
