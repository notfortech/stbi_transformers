using System.Text.Json;
using DashboardAgents.Core.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace DashboardAgents.Api.Services;

/// <summary>
/// Redis-backed pipeline session store. Used when Redis:ConnectionString is configured.
/// Sessions are serialised to JSON and stored with the same 2-hour TTL the in-memory
/// implementation uses. Survives API restarts and works across multiple App Service instances.
/// </summary>
public sealed class RedisPipelineSessionStore : IPipelineSessionStore
{
    private readonly IDistributedCache _cache;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisPipelineSessionStore(IDistributedCache cache) => _cache = cache;

    public void Save(PipelineSession session)
    {
        var json = JsonSerializer.Serialize(session, JsonOpts);
        var ttl = session.ExpiresAt - DateTimeOffset.UtcNow;
        _cache.SetString(Key(session.SessionId), json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromHours(2)
        });
    }

    public PipelineSession? Get(string sessionId)
    {
        var json = _cache.GetString(Key(sessionId));
        return json is null ? null : JsonSerializer.Deserialize<PipelineSession>(json, JsonOpts);
    }

    public void Remove(string sessionId) => _cache.Remove(Key(sessionId));

    private static string Key(string id) => $"da:pipeline:{id}";
}
