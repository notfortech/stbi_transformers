using System.Text.Json.Serialization;

namespace DashboardAgents.Core.Models;

public sealed class TmdlValidationResult
{
    [JsonPropertyName("is_valid")] public bool IsValid { get; set; }
    [JsonPropertyName("violations")] public List<string> Violations { get; set; } = new();
}
