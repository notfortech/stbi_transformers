using System.Text.Json.Serialization;

namespace DashboardAgents.Core.Models;

/// <summary>One TMDL file, relative to the semantic model's definition/ root (e.g. "tables/Fact_Orders.tmdl").</summary>
public sealed class TmdlFile
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

/// <summary>
/// Output of TmdlAuthoringService.AuthorAsync — a full TMDL semantic model definition
/// (database.tmdl, model.tmdl, relationships.tmdl, expressions.tmdl, cultures/en-US.tmdl,
/// tables/*.tmdl) derived from an approved Blueprint. Deliberately NOT validated or deployed
/// here — see S8 (deterministic TMDL validator + deploy) for what happens to this next.
/// </summary>
public sealed class TmdlAuthoringResult
{
    [JsonPropertyName("files")] public List<TmdlFile> Files { get; set; } = new();
    [JsonPropertyName("reasoning")] public string Reasoning { get; set; } = "";
}
