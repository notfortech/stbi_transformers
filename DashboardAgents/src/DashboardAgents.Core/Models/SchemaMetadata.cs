namespace DashboardAgents.Core.Models;

/// <summary>Supported source database engines for the live schema connector.</summary>
public enum DbProvider
{
    SqlServer,
    PostgreSql
}

/// <summary>
/// A snapshot of a source database's structure, pulled read-only via information_schema
/// (or engine-equivalent) plus lightweight cardinality stats. This is the artifact the
/// SchemaConnector produces and the BlueprintAgent consumes in place of a pasted schema.
/// </summary>
public sealed class SchemaSnapshot
{
    public string DatabaseName { get; set; } = "";
    public DbProvider Provider { get; set; }
    public List<TableMetadata> Tables { get; set; } = new();
    public DateTimeOffset IntrospectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TableMetadata
{
    public string SchemaName { get; set; } = "dbo";
    public string TableName { get; set; } = "";
    public long? ApproximateRowCount { get; set; }
    public List<ColumnMetadata> Columns { get; set; } = new();
    public List<ForeignKeyMetadata> ForeignKeys { get; set; } = new();

    public string QualifiedName => $"{SchemaName}.{TableName}";
}

public sealed class ColumnMetadata
{
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Populated only for low-cardinality columns (below the connector's sampling threshold).
    /// Gives the blueprint agent enough signal to detect industry-specific status/enum values
    /// (e.g. NDIS plan states vs. professional-services WIP states) without seeing row data.
    /// </summary>
    public List<string>? DistinctSampleValues { get; set; }

    public long? DistinctValueCount { get; set; }
}

public sealed class ForeignKeyMetadata
{
    public string FromColumn { get; set; } = "";
    public string ToTable { get; set; } = "";
    public string ToColumn { get; set; } = "";
}
