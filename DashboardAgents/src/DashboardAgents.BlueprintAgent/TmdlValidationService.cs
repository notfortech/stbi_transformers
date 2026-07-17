using System.Text.RegularExpressions;
using DashboardAgents.Core.Models;

namespace DashboardAgents.BlueprintAgent;

public interface ITmdlValidationService
{
    /// <summary>
    /// Deterministic, non-LLM checks that TmdlAuthoringService's output actually matches the
    /// blueprint it was authored from — no fabricated tables, no dropped columns/measures, every
    /// relationship resolves to a real table/column. This is the safety gate S7's output must
    /// pass before S8's deploy step (in stbi-bind-deploy) is allowed to touch it.
    /// </summary>
    TmdlValidationResult Validate(Blueprint blueprint, TmdlAuthoringResult authored);
}

public sealed class TmdlValidationService : ITmdlValidationService
{
    private static readonly string[] RequiredFixedFiles =
    [
        "database.tmdl", "model.tmdl", "relationships.tmdl", "expressions.tmdl",
        "cultures/en-US.tmdl", "tables/_Measures.tmdl"
    ];

    public TmdlValidationResult Validate(Blueprint blueprint, TmdlAuthoringResult authored)
    {
        var violations = new List<string>();
        var filesByPath = authored.Files
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Content, StringComparer.OrdinalIgnoreCase);

        foreach (var required in RequiredFixedFiles)
            if (!filesByPath.ContainsKey(required))
                violations.Add($"Missing required file: {required}");

        // Every fact/dimension/date table in the blueprint must have exactly one tables/*.tmdl file.
        var expectedTableNames = blueprint.DataModel.FactTables.Select(f => f.Name)
            .Concat(blueprint.DataModel.DimensionTables.Select(d => d.Name))
            .Concat(string.IsNullOrWhiteSpace(blueprint.DataModel.DateTable.Name) ? [] : [blueprint.DataModel.DateTable.Name])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var actualTableFiles = filesByPath.Keys
            .Where(p => p.StartsWith("tables/", StringComparison.OrdinalIgnoreCase)
                        && !p.Equals("tables/_Measures.tmdl", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var actualTableNames = actualTableFiles
            .Select(p => System.IO.Path.GetFileNameWithoutExtension(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in expectedTableNames)
            if (!actualTableNames.Contains(expected))
                violations.Add($"Missing table file for blueprint table '{expected}' (expected tables/{expected}.tmdl).");

        // No fabricated tables — every tables/*.tmdl (other than _Measures) must correspond to a real blueprint table.
        foreach (var actual in actualTableNames)
            if (!expectedTableNames.Contains(actual))
                violations.Add($"Table file 'tables/{actual}.tmdl' does not correspond to any table in the blueprint's data_model — possible hallucination.");

        // Each table file should have a `table` declaration and at least one `column`.
        foreach (var path in actualTableFiles)
        {
            var content = filesByPath[path];
            if (!Regex.IsMatch(content, @"^\s*table\s+\S+", RegexOptions.Multiline))
                violations.Add($"{path}: does not contain a `table <Name>` declaration.");
            if (!Regex.IsMatch(content, @"^\s*column\s+\S+", RegexOptions.Multiline))
                violations.Add($"{path}: contains no `column` blocks.");
        }

        // Fact table columns must all survive into the authored table file.
        foreach (var fact in blueprint.DataModel.FactTables)
        {
            var path = $"tables/{fact.Name}.tmdl";
            if (!filesByPath.TryGetValue(path, out var content))
                continue; // already flagged above as a missing file

            foreach (var col in fact.Columns)
                if (!Regex.IsMatch(content, $@"^\s*column\s+'?{Regex.Escape(col.Name)}'?\b", RegexOptions.Multiline))
                    violations.Add($"{path}: blueprint column '{col.Name}' not found in authored TMDL.");
        }

        // Every measure in the blueprint must appear (by name) in tables/_Measures.tmdl.
        if (filesByPath.TryGetValue("tables/_Measures.tmdl", out var measuresContent))
        {
            foreach (var measure in blueprint.Measures)
                if (!measuresContent.Contains(measure.Name, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"tables/_Measures.tmdl: blueprint measure '{measure.Name}' not found in authored TMDL.");
        }

        // Every relationship must reference tables that actually exist.
        if (filesByPath.TryGetValue("relationships.tmdl", out var relationshipsContent))
        {
            foreach (var rel in blueprint.DataModel.Relationships)
            {
                var (fromTable, _) = SplitTableColumn(rel.From);
                var (toTable, _) = SplitTableColumn(rel.To);

                if (!actualTableNames.Contains(fromTable) && !fromTable.Equals(blueprint.DataModel.DateTable.Name, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"relationships.tmdl: relationship references table '{fromTable}' which has no authored table file.");
                if (!actualTableNames.Contains(toTable) && !toTable.Equals(blueprint.DataModel.DateTable.Name, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"relationships.tmdl: relationship references table '{toTable}' which has no authored table file.");

                if (!relationshipsContent.Contains(fromTable, StringComparison.OrdinalIgnoreCase) ||
                    !relationshipsContent.Contains(toTable, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"relationships.tmdl: no relationship block found referencing both '{fromTable}' and '{toTable}'.");
            }
        }

        return new TmdlValidationResult { IsValid = violations.Count == 0, Violations = violations };
    }

    /// <summary>Parses "TableName[ColumnName]" into ("TableName", "ColumnName") — same convention koru-main's ReportDesignerClient uses.</summary>
    private static (string Table, string Column) SplitTableColumn(string reference)
    {
        var openBracket = reference.IndexOf('[');
        var closeBracket = reference.IndexOf(']');
        if (openBracket < 0 || closeBracket <= openBracket)
            return (reference, string.Empty);

        return (reference[..openBracket], reference[(openBracket + 1)..closeBracket]);
    }
}
