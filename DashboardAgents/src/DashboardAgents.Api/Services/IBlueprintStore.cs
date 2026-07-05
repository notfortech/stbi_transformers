using System.Collections.Concurrent;
using DashboardAgents.Core.Models;

namespace DashboardAgents.Api.Services;

public interface IBlueprintStore
{
    void Save(Blueprint blueprint);
    Blueprint? Get(string blueprintId);
}

/// <summary>
/// Minimal in-memory store so /adapt can retrieve a previously generated blueprint by id.
/// This is intentionally simple — swap the implementation for a real database (the blueprint
/// is already a clean JSON document, so a document store like Cosmos DB or a jsonb column in
/// Postgres both work well) once you need durability across API restarts / multiple instances.
/// </summary>
public sealed class InMemoryBlueprintStore : IBlueprintStore
{
    private readonly ConcurrentDictionary<string, Blueprint> _blueprints = new();

    public void Save(Blueprint blueprint)
    {
        if (string.IsNullOrWhiteSpace(blueprint.BlueprintId))
            throw new ArgumentException("Blueprint must have an id before it can be stored.");
        _blueprints[blueprint.BlueprintId] = blueprint;
    }

    public Blueprint? Get(string blueprintId) => _blueprints.GetValueOrDefault(blueprintId);
}
