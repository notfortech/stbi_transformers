using System.Text.Json.Serialization;
using DashboardAgents.Core.Models;

namespace DashboardAgents.TweakAgent;

/// <summary>Raw shape returned by the LLM per UseCaseTweakAgentInstructions.md's OUTPUT FORMAT section.</summary>
internal sealed class TweakAgentResponse
{
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("pages")] public List<DashboardPage> Pages { get; set; } = new();
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
    [JsonPropertyName("fields_used")] public List<string> FieldsUsed { get; set; } = new();
}
