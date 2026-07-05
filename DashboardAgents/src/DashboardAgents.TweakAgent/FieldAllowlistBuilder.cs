using System.Text;
using DashboardAgents.Core.Models;

namespace DashboardAgents.TweakAgent;

/// <summary>
/// Extracts every valid field name (measures, KPIs, tables, columns) from a blueprint into an
/// explicit allow-list. This is what turns "the LLM promises not to invent fields" into
/// "the LLM is given the closed set of fields it's allowed to reference" — a much stronger
/// guarantee, and the thing that gets validated post-hoc in TweakOutputValidator.
/// </summary>
public static class FieldAllowlistBuilder
{
    public sealed record Allowlist(
        HashSet<string> MeasureNames,
        HashSet<string> KpiNames,
        HashSet<string> TableNames,
        HashSet<string> DimensionColumnRefs, // "Dim_Customer[Region]" style refs
        string PromptText);

    public static Allowlist Build(Blueprint blueprint)
    {
        var measureNames = blueprint.Measures.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kpiNames = blueprint.Kpis.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tableNames = blueprint.DataModel.FactTables.Select(f => f.Name)
            .Concat(blueprint.DataModel.DimensionTables.Select(d => d.Name))
            .Append(blueprint.DataModel.DateTable.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dimensionColumnRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dim in blueprint.DataModel.DimensionTables)
        {
            foreach (var key in dim.KeyColumns)
                dimensionColumnRefs.Add($"{dim.Name}[{key}]");
            foreach (var hierarchy in dim.Hierarchies)
            {
                // hierarchies are expressed like "State > Region > Suburb > Participant"
                foreach (var level in hierarchy.Split('>', StringSplitOptions.TrimEntries))
                    dimensionColumnRefs.Add($"{dim.Name}[{level}]");
            }
        }
        foreach (var fact in blueprint.DataModel.FactTables)
        {
            foreach (var col in fact.Columns)
                dimensionColumnRefs.Add($"{fact.Name}[{col.Name}]");
        }
        foreach (var key in blueprint.DataModel.DateTable.KeyColumns)
            dimensionColumnRefs.Add($"{blueprint.DataModel.DateTable.Name}[{key}]");

        var promptText = BuildPromptText(blueprint, measureNames, kpiNames, tableNames, dimensionColumnRefs);

        return new Allowlist(measureNames, kpiNames, tableNames, dimensionColumnRefs, promptText);
    }

    private static string BuildPromptText(
        Blueprint blueprint,
        HashSet<string> measureNames,
        HashSet<string> kpiNames,
        HashSet<string> tableNames,
        HashSet<string> dimensionColumnRefs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FIELD ALLOW-LIST FOR THIS BLUEPRINT — YOU MAY REFERENCE ONLY THESE FIELDS");
        sb.AppendLine("===========================================================================");
        sb.AppendLine();
        sb.AppendLine("Tables:");
        foreach (var t in tableNames) sb.AppendLine($"  - {t}");
        sb.AppendLine();
        sb.AppendLine("Dimension/Fact column references (Table[Column] format):");
        foreach (var c in dimensionColumnRefs) sb.AppendLine($"  - {c}");
        sb.AppendLine();
        sb.AppendLine("Measures (name — description):");
        foreach (var m in blueprint.Measures) sb.AppendLine($"  - {m.Name} — {m.Description}");
        sb.AppendLine();
        sb.AppendLine("KPIs (name — measure_ref — description via actionability):");
        foreach (var k in blueprint.Kpis) sb.AppendLine($"  - {k.Name} (measure: {k.MeasureRef}) — {k.Actionability}");
        sb.AppendLine();
        sb.AppendLine("Existing pages (for matched_existing evaluation):");
        foreach (var p in blueprint.Pages) sb.AppendLine($"  - {p.Name} [{p.Layout}] — {p.Purpose}");

        return sb.ToString();
    }
}
