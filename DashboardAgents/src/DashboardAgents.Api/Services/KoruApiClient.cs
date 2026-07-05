using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DashboardAgents.Api.Services;

public sealed class KoruOptions
{
    public const string SectionName = "Koru";
    public string BaseUrl { get; set; } = "";
    public string? ServiceApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class KoruTemplate
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("templateName")] public string? TemplateName { get; set; }
    [JsonPropertyName("industry")] public string? Industry { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("requiredColumns")] public string? RequiredColumns { get; set; }
    [JsonPropertyName("optionalColumns")] public string? OptionalColumns { get; set; }
    [JsonPropertyName("supportedCapabilities")] public List<string>? SupportedCapabilities { get; set; }
    [JsonPropertyName("designImageUrl")] public string? DesignImageUrl { get; set; }
}

/// <summary>
/// Thin HTTP client that calls koru-main's internal APIs to retrieve the template catalog
/// and, when available, forward schema analysis requests.
/// Configured via appsettings.json Koru section. When BaseUrl is empty the client returns
/// empty results and the caller falls back to built-in archetypes.
/// </summary>
public sealed class KoruApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<KoruApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public KoruApiClient(HttpClient http, ILogger<KoruApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<KoruTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _http.GetAsync("/api/admin/templates?pageSize=100", cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Koru templates endpoint returned {StatusCode}", resp.StatusCode);
                return new();
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            // Handle ApiResponse<PaginatedResult<T>> envelope: { data: { items: [...] } }
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("items", out var items))
                    return JsonSerializer.Deserialize<List<KoruTemplate>>(items.GetRawText(), JsonOpts) ?? new();
                return JsonSerializer.Deserialize<List<KoruTemplate>>(data.GetRawText(), JsonOpts) ?? new();
            }
            return JsonSerializer.Deserialize<List<KoruTemplate>>(json, JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch templates from koru-main");
            return new();
        }
    }

    public async Task<string?> GetTemplateDesignImageUrlAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        // Returns a proxied URL the UI can use to render the template screenshot
        try
        {
            var baseAddr = _http.BaseAddress?.ToString().TrimEnd('/') ?? "";
            if (string.IsNullOrEmpty(baseAddr)) return null;
            return $"{baseAddr}/api/templates/{templateId}/design-image";
        }
        catch
        {
            return null;
        }
    }
}
