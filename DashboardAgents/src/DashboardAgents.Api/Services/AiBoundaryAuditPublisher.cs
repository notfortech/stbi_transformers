using System.Text.Json;
using DashboardAgents.Core.Services;

namespace DashboardAgents.Api.Services;

/// <summary>
/// HTTP-backed implementation of <see cref="IAiBoundaryAuditPublisher"/> — posts to
/// koru-main's AiBoundaryAuditEvents table via <see cref="KoruApiClient"/>. This service has no
/// database of its own, so unlike koru-main's IAiBoundaryAuditService this only publishes, it
/// never queries.
/// </summary>
public sealed class AiBoundaryAuditPublisher : IAiBoundaryAuditPublisher
{
    private readonly KoruApiClient _koru;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AiBoundaryAuditPublisher(KoruApiClient koru)
    {
        _koru = koru;
    }

    public Task LogSentAsync(
        string service, string operation, string correlationId, string targetService,
        object metadata, CancellationToken cancellationToken = default) =>
        _koru.PostAiBoundaryAuditEventAsync(new AiBoundaryAuditEventPayload
        {
            CorrelationId = correlationId,
            Service = service,
            Operation = operation,
            Phase = "Sent",
            TargetService = targetService,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOpts)
        }, cancellationToken);

    public Task LogReceivedAsync(
        string service, string operation, string correlationId, string targetService,
        object metadata, long durationMs, CancellationToken cancellationToken = default) =>
        _koru.PostAiBoundaryAuditEventAsync(new AiBoundaryAuditEventPayload
        {
            CorrelationId = correlationId,
            Service = service,
            Operation = operation,
            Phase = "Received",
            TargetService = targetService,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOpts),
            DurationMs = durationMs
        }, cancellationToken);

    public Task LogFailedAsync(
        string service, string operation, string correlationId, string targetService,
        long durationMs, string errorSummary, CancellationToken cancellationToken = default) =>
        _koru.PostAiBoundaryAuditEventAsync(new AiBoundaryAuditEventPayload
        {
            CorrelationId = correlationId,
            Service = service,
            Operation = operation,
            Phase = "Failed",
            TargetService = targetService,
            DurationMs = durationMs,
            ErrorSummary = errorSummary
        }, cancellationToken);
}
