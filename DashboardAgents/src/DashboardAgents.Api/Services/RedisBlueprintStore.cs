using System.Text.Json;
using DashboardAgents.Core.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace DashboardAgents.Api.Services;

/// <summary>
/// Redis-backed blueprint store. Used when Redis:ConnectionString is configured.
/// Blueprints are kept for 24 hours — long enough for the tweak flow without growing unbounded.
/// </summary>
public sealed class RedisBlueprintStore : IBlueprintStore
{
    private readonly IDistributedCache _cache;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly DistributedCacheEntryOptions BlueprintTtl = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
    };

    public RedisBlueprintStore(IDistributedCache cache) => _cache = cache;

    public void Save(Blueprint blueprint)
    {
        if (string.IsNullOrWhiteSpace(blueprint.BlueprintId))
            throw new ArgumentException("Blueprint must have an id before it can be stored.");

        _cache.SetString(Key(blueprint.BlueprintId), JsonSerializer.Serialize(blueprint, JsonOpts), BlueprintTtl);
    }

    public Blueprint? Get(string blueprintId)
    {
        var json = _cache.GetString(Key(blueprintId));
        return json is null ? null : JsonSerializer.Deserialize<Blueprint>(json, JsonOpts);
    }

    private static string Key(string id) => $"da:blueprint:{id}";
}
