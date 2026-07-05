using DashboardAgents.Core.Models;
using static DashboardAgents.TweakAgent.FieldAllowlistBuilder;

namespace DashboardAgents.TweakAgent;

public sealed class TweakValidationResult
{
    public bool IsValid => Violations.Count == 0;
    public List<string> Violations { get; } = new();
}

/// <summary>
/// Server-side enforcement of the allow-list constraint described in
/// UseCaseTweakAgentInstructions.md. The prompt asks the model not to invent fields; this class
/// is the backstop that actually rejects a response if it does, rather than trusting compliance.
/// </summary>
public static class TweakOutputValidator
{
    public static TweakValidationResult Validate(TweakResult result, Allowlist allowlist)
    {
        var validation = new TweakValidationResult();

        foreach (var page in result.Pages)
        {
            foreach (var slicer in page.Slicers)
            {
                if (!allowlist.DimensionColumnRefs.Contains(slicer.Field))
                    validation.Violations.Add($"Page '{page.Name}' slicer references unknown field '{slicer.Field}' (not in blueprint allow-list).");
            }

            foreach (var visual in page.Visuals)
            {
                foreach (var measureName in visual.Measures)
                {
                    if (!allowlist.MeasureNames.Contains(measureName))
                        validation.Violations.Add($"Page '{page.Name}' visual '{visual.Title}' references unknown measure '{measureName}' (not in blueprint allow-list).");
                }
            }

            if (page.DrillThrough is not null)
            {
                foreach (var field in page.DrillThrough.TriggerFields)
                {
                    if (!allowlist.DimensionColumnRefs.Contains(field))
                        validation.Violations.Add($"Page '{page.Name}' drill-through trigger references unknown field '{field}' (not in blueprint allow-list).");
                }
            }
        }

        return validation;
    }
}
