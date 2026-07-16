using System.Text.Json.Serialization;

namespace DashboardAgents.Core.Models;

public sealed class TmdlValidationResult
{
    [JsonPropertyName("isValid")] public bool IsValid { get; set; }
    [JsonPropertyName("violations")] public List<string> Violations { get; set; } = new();
}
