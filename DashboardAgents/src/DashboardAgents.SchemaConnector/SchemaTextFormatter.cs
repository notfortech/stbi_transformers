using System.Text;
using DashboardAgents.Core.Models;

namespace DashboardAgents.SchemaConnector;

/// <summary>
/// Converts a live SchemaSnapshot into the same plain-text schema format a human previously
/// pasted into the tool's "Dataset Schema" textarea. This is the integration seam that lets
/// the live connector plug into the existing prompt pipeline with zero changes to the agent's
/// system prompt or blueprint schema.
/// </summary>
public static class SchemaTextFormatter
{
    public static string Format(SchemaSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DATASET SCHEMA / HEADERS (live introspection — {snapshot.Provider}, database '{snapshot.DatabaseName}', {snapshot.IntrospectedAt:yyyy-MM-dd}):");
        sb.AppendLine();

        foreach (var table in snapshot.Tables.OrderBy(t => t.QualifiedName))
        {
            var rowCountLabel = table.ApproximateRowCount is { } rc ? $"~{rc:N0} rows" : "row count unknown";
            sb.AppendLine($"Table: {table.QualifiedName} ({rowCountLabel})");

            foreach (var col in table.Columns)
            {
                var flags = new List<string>();
                if (col.IsPrimaryKey) flags.Add("PK");
                var fk = table.ForeignKeys.FirstOrDefault(f => f.FromColumn == col.ColumnName);
                if (fk is not null) flags.Add($"FK -> {fk.ToTable}.{fk.ToColumn}");
                if (!col.IsNullable) flags.Add("NOT NULL");

                var flagText = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "";
                var line = $"  {col.ColumnName} [{col.DataType}]{flagText}";

                if (col.DistinctSampleValues is { Count: > 0 })
                {
                    line += $" — {col.DistinctValueCount} distinct values: {string.Join("/", col.DistinctSampleValues)}";
                }
                else if (col.DistinctValueCount is { } dvc)
                {
                    line += $" — {dvc} distinct values";
                }

                sb.AppendLine(line);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
