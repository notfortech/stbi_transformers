namespace DashboardAgents.Core.Services;

/// <summary>
/// Publishes AI-boundary events (this service's own calls out to Anthropic/OpenAI) to
/// koru-main's durable AiBoundaryAuditEvents table. Lives here (Core) rather than in
/// DashboardAgents.Api.Services so that generation services in other projects (e.g.
/// DashboardAgents.BlueprintAgent) can depend on the abstraction without a circular project
/// reference back to Api, which owns the concrete HTTP-backed implementation. Join key across
/// koru-main's and this service's own rows for one logical call is CorrelationId — Service and
/// Operation names do not need to match between the two sides.
/// </summary>
public interface IAiBoundaryAuditPublisher
{
    Task LogSentAsync(
        string service, string operation, string correlationId, string targetService,
        object metadata, CancellationToken cancellationToken = default);

    Task LogReceivedAsync(
        string service, string operation, string correlationId, string targetService,
        object metadata, long durationMs, CancellationToken cancellationToken = default);

    Task LogFailedAsync(
        string service, string operation, string correlationId, string targetService,
        long durationMs, string errorSummary, CancellationToken cancellationToken = default);
}
