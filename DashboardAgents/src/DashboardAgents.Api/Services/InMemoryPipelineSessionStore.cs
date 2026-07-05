using System.Collections.Concurrent;
using DashboardAgents.Core.Models;

namespace DashboardAgents.Api.Services;

public sealed class InMemoryPipelineSessionStore : IPipelineSessionStore
{
    private readonly ConcurrentDictionary<string, PipelineSession> _store = new();

    public void Save(PipelineSession session) =>
        _store[session.SessionId] = session;

    public PipelineSession? Get(string sessionId)
    {
        if (!_store.TryGetValue(sessionId, out var session))
            return null;
        if (session.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _store.TryRemove(sessionId, out _);
            return null;
        }
        return session;
    }

    public void Remove(string sessionId) => _store.TryRemove(sessionId, out _);
}
