using System.Text.Json.Serialization;

namespace DashboardAgents.Core.Models;

// ── Pipeline session ─────────────────────────────────────────────────────────

public enum PipelineStep
{
    Connected,
    Transformed,
    DesignSelected,
    Generated
}

public sealed class PipelineSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public PipelineStep CurrentStep { get; set; } = PipelineStep.Connected;
    public string DataSource { get; set; } = ""; // "file" | "sqlserver" | "postgres"
    public string? FileName { get; set; }
    public SchemaSnapshot? Schema { get; set; }
    public DataProfile? Profile { get; set; }
    public List<DesignOption>? DesignOptions { get; set; }
    public Blueprint? GeneratedBlueprint { get; set; }
    public TemplateMatch? BestTemplateMatch { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(2);
}

// ── Transformation / validation output ─────────────────────────────────────

public sealed class DataProfile
{
    public List<ValidatedColumn> Columns { get; set; } = new();
    public List<ValidationIssue> Issues { get; set; } = new();
    public List<TransformRecommendation> Recommendations { get; set; } = new();
    public bool IsReadyForDesign { get; set; }
    public string Summary { get; set; } = "";
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}

public sealed class ValidatedColumn
{
    public string OriginalName { get; set; } = "";
    public string SuggestedName { get; set; } = "";
    public string InferredType { get; set; } = "text";
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public double MissingRatio { get; set; }
    public long? DistinctCount { get; set; }
    public List<string> Issues { get; set; } = new();
    public bool HasIssues => Issues.Count > 0;
}

public sealed class ValidationIssue
{
    public string Severity { get; set; } = "warning"; // "error" | "warning" | "info"
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Column { get; set; }
}

public sealed class TransformRecommendation
{
    public int Order { get; set; }
    public string Action { get; set; } = ""; // "rename" | "cast" | "normalize" | "drop" | "fill_nulls" | "split"
    public string? Column { get; set; }
    public string Description { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}

// ── Design / template options ────────────────────────────────────────────────

public sealed class DesignOption
{
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Industry { get; set; } = "";
    public string? Description { get; set; }
    public double MatchScore { get; set; }
    public List<string> MatchReasons { get; set; } = new();
    public List<string> SupportedCapabilities { get; set; } = new();
    public string? DesignImageUrl { get; set; }
}

public sealed class TemplateMatch
{
    public string TemplateId { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public double Confidence { get; set; }
    public List<ColumnMapping> ColumnMappings { get; set; } = new();
    public List<string> Transformations { get; set; } = new();
    public string? Notes { get; set; }
}

public sealed class ColumnMapping
{
    public string TemplateColumn { get; set; } = "";
    public string ClientColumn { get; set; } = "";
    public string? Transform { get; set; }
}

// ── Pipeline API contracts ───────────────────────────────────────────────────

public sealed class ConnectRequest
{
    /// <summary>"file" | "sqlserver" | "postgres" | "schema"</summary>
    public string Provider { get; set; } = "file";

    /// <summary>DB connection string — required when Provider is not "file" or "schema".</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Base64-encoded file bytes — required when Provider is "file".</summary>
    public string? FileBase64 { get; set; }

    public string? FileName { get; set; }

    /// <summary>Schema filter (DB provider only); null = all schemas.</summary>
    public IReadOnlyList<string>? SchemaFilter { get; set; }

    /// <summary>
    /// Pre-extracted schema metadata — required when Provider is "schema". Used by callers
    /// (e.g. koru-main) that already introspect the source themselves and only ever send
    /// structural metadata onward, never raw file bytes or a live connection string.
    /// </summary>
    public PreExtractedSchemaDto? Schema { get; set; }
}

public sealed class PreExtractedSchemaDto
{
    public string DatabaseName { get; set; } = "";
    public List<PreExtractedTableDto> Tables { get; set; } = new();
}

public sealed class PreExtractedTableDto
{
    public string TableName { get; set; } = "";
    public long? ApproximateRowCount { get; set; }
    public List<PreExtractedColumnDto> Columns { get; set; } = new();
}

public sealed class PreExtractedColumnDto
{
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
}

public sealed class ConnectResponse
{
    public string SessionId { get; set; } = "";
    public string DataSource { get; set; } = "";
    public string? FileName { get; set; }
    public List<TableSummary> Tables { get; set; } = new();
    public int TotalColumns { get; set; }
    public string Summary { get; set; } = "";
}

public sealed class TableSummary
{
    public string Name { get; set; } = "";
    public int ColumnCount { get; set; }
    public long? ApproximateRowCount { get; set; }
    public List<string> ColumnNames { get; set; } = new();
}

public sealed class TransformRequest
{
    public string? UserPrompt { get; set; }
    public bool ApplyAllRecommendations { get; set; } = false;
}

public sealed class GenerateRequest
{
    public string? SelectedTemplateId { get; set; }
    public string? BusinessGoal { get; set; }
    public string? BusinessRequirements { get; set; }
    public string? Industry { get; set; }
    public string? KnowledgePack { get; set; }
}

public sealed class GenerateResponse
{
    public string SessionId { get; set; } = "";
    public Blueprint Blueprint { get; set; } = new();
    public TemplateMatch? BestTemplateMatch { get; set; }
    public double Confidence { get; set; }
    public long GenerationTimeMs { get; set; }
}
