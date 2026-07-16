using System.Net.Http.Headers;
using System.Net.Http.Json;
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

/// <summary>Mirrors koru-main's AiBoundaryAuditEventIngestRequest — see that file for field meanings.</summary>
public sealed class AiBoundaryAuditEventPayload
{
    public string CorrelationId { get; set; } = "";
    public string Service { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Phase { get; set; } = "";
    public string TargetService { get; set; } = "";
    public string? MetadataJson { get; set; }
    public long? DurationMs { get; set; }
    public int? StatusCode { get; set; }
    public string? ErrorSummary { get; set; }
    public Guid? ClientId { get; set; }
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

    private static readonly JsonSerializerOptions CamelCaseJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Pushes one AI-boundary audit event (see AiBoundaryAuditEventPayload) to koru-main's
    /// AiBoundaryAuditEvents table. Uses a short, independent timeout — a slow or unreachable
    /// koru-main must never add meaningful latency to the actual LLM call this is auditing —
    /// and swallows every failure, same non-negotiable principle as koru-main's own
    /// AiBoundaryAuditService.AddAsync: the audit log must never be able to break the flow it
    /// watches.
    /// </summary>
    public async Task PostAiBoundaryAuditEventAsync(AiBoundaryAuditEventPayload payload, CancellationToken cancellationToken = default)
    {
        if (_http.BaseAddress is null)
            return; // Koru:BaseUrl not configured — nothing to push to.

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var content = JsonContent.Create(payload, options: CamelCaseJsonOpts);
            using var resp = await _http.PostAsync("/api/internal/ai-boundary-audit-log", content, cts.Token);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "AiBoundaryAudit.PushFailed StatusCode={StatusCode} CorrelationId={CorrelationId} Phase={Phase}",
                    (int)resp.StatusCode, payload.CorrelationId, payload.Phase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AiBoundaryAudit.PushFailed CorrelationId={CorrelationId} Phase={Phase} — continuing, audit push is best-effort",
                payload.CorrelationId, payload.Phase);
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
