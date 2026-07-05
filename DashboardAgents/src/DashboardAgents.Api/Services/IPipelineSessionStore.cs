using DashboardAgents.Core.Models;

namespace DashboardAgents.Api.Services;

public interface IPipelineSessionStore
{
    void Save(PipelineSession session);
    PipelineSession? Get(string sessionId);
    void Remove(string sessionId);
}
