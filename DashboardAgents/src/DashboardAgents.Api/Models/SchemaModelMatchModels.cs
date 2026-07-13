namespace DashboardAgents.Api.Models;

/// <summary>
/// Request for the AI-assisted schema/model matching step: koru-main sends the
/// connecting client's column headers/types plus its own Approved SchemaModel
/// directory (name, industry, fields) as candidates. No row-level data is ever
/// included — this is the same "structural metadata only" boundary the rest of
/// the pipeline already enforces.
/// </summary>
public sealed class SchemaModelMatchRequest
{
    public List<SchemaModelMatchColumn> Columns { get; set; } = new();
    public List<SchemaModelMatchCandidate> CandidateModels { get; set; } = new();
}

public sealed class SchemaModelMatchColumn
{
    public string ColumnName { get; set; } = string.Empty;
    public string? DataType { get; set; }
}

public sealed class SchemaModelMatchCandidate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public List<SchemaModelMatchCandidateField> Fields { get; set; } = new();
}

public sealed class SchemaModelMatchCandidateField
{
    public string FieldName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
}

/// <summary>
/// Exactly one of <see cref="MatchedModelId"/> or <see cref="ProposedModel"/> is set —
/// the AI either picks one candidate by id, or proposes a brand-new model when nothing
/// in the directory fits well. <see cref="Reasoning"/> is a short human-readable
/// explanation of why, surfaced alongside the result for traceability.
/// </summary>
public sealed class SchemaModelMatchResult
{
    public string? MatchedModelId { get; set; }
    public double Confidence { get; set; }
    public ProposedSchemaModel? ProposedModel { get; set; }
    public string? Reasoning { get; set; }
}

public sealed class ProposedSchemaModel
{
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public List<SchemaModelMatchCandidateField> Fields { get; set; } = new();

    /// <summary>Name for the dashboard template koru-main creates alongside this model — no design/sections, just a label (e.g. "Government Annual Report — Overview").</summary>
    public string TemplateName { get; set; } = string.Empty;
}
