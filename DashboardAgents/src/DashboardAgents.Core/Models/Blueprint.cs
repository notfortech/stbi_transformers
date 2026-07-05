using System.Text.Json.Serialization;

namespace DashboardAgents.Core.Models;

/// <summary>
/// Root object for a generated Analytics Blueprint.
/// Mirrors prompts/DashboardBlueprintSchema.md field-for-field so that the
/// LLM's JSON output deserializes directly into this graph with no manual mapping.
/// </summary>
public sealed class Blueprint
{
    [JsonPropertyName("meta")] public BlueprintMeta Meta { get; set; } = new();
    [JsonPropertyName("detection")] public DetectionResult Detection { get; set; } = new();
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = new();
    [JsonPropertyName("data_model")] public DataModel DataModel { get; set; } = new();
    [JsonPropertyName("measures")] public List<Measure> Measures { get; set; } = new();
    [JsonPropertyName("kpis")] public List<Kpi> Kpis { get; set; } = new();
    [JsonPropertyName("pages")] public List<DashboardPage> Pages { get; set; } = new();
    [JsonPropertyName("executive_questions")] public List<string> ExecutiveQuestions { get; set; } = new();
    [JsonPropertyName("security")] public SecurityBlock Security { get; set; } = new();
    [JsonPropertyName("governance")] public GovernanceBlock Governance { get; set; } = new();
    [JsonPropertyName("semantic_notes")] public List<string> SemanticNotes { get; set; } = new();
    [JsonPropertyName("quality_frameworks")] public QualityFrameworks QualityFrameworks { get; set; } = new();
    [JsonPropertyName("expected_targets")] public ExpectedTargets ExpectedTargets { get; set; } = new();
    [JsonPropertyName("self_review")] public SelfReview SelfReview { get; set; } = new();
    [JsonPropertyName("confidence")] public ConfidenceBlock Confidence { get; set; } = new();

    /// <summary>Server-assigned identifier used to retrieve/adapt this blueprint later. Not part of the LLM contract.</summary>
    [JsonIgnore] public string? BlueprintId { get; set; }
}

public sealed class BlueprintMeta
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("industry")] public string Industry { get; set; } = "";
    [JsonPropertyName("capability_domain")] public string CapabilityDomain { get; set; } = "";
    [JsonPropertyName("business_goal")] public string BusinessGoal { get; set; } = "";
    [JsonPropertyName("primary_audience")] public string PrimaryAudience { get; set; } = "";
    [JsonPropertyName("fiscal_year_start")] public string FiscalYearStart { get; set; } = "";
    [JsonPropertyName("fiscal_year_end")] public string FiscalYearEnd { get; set; } = "";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "";
    [JsonPropertyName("refresh_cadence")] public string RefreshCadence { get; set; } = "";
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = "";
}

public sealed class DetectionResult
{
    [JsonPropertyName("industry")] public string Industry { get; set; } = "";
    [JsonPropertyName("confidence")] public int Confidence { get; set; }
    [JsonPropertyName("tier")] public int Tier { get; set; }
    [JsonPropertyName("signals_matched")] public List<string> SignalsMatched { get; set; } = new();
    [JsonPropertyName("pack_applied")] public string PackApplied { get; set; } = "";
    [JsonPropertyName("capability_domain")] public string CapabilityDomain { get; set; } = "";
    [JsonPropertyName("domain_confidence")] public int DomainConfidence { get; set; }
}

public sealed class DataModel
{
    [JsonPropertyName("fact_tables")] public List<FactTable> FactTables { get; set; } = new();
    [JsonPropertyName("dimension_tables")] public List<DimensionTable> DimensionTables { get; set; } = new();
    [JsonPropertyName("relationships")] public List<Relationship> Relationships { get; set; } = new();
    [JsonPropertyName("date_table")] public DateTableSpec DateTable { get; set; } = new();
}

public sealed class FactTable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("grain")] public string Grain { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("columns")] public List<ColumnSpec> Columns { get; set; } = new();
}

public sealed class ColumnSpec
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
}

public sealed class DimensionTable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = ""; // Standard | SCD2
    [JsonPropertyName("scd_justification")] public string? ScdJustification { get; set; }
    [JsonPropertyName("hierarchies")] public List<string> Hierarchies { get; set; } = new();
    [JsonPropertyName("key_columns")] public List<string> KeyColumns { get; set; } = new();
}

public sealed class Relationship
{
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonPropertyName("cardinality")] public string Cardinality { get; set; } = "";
    [JsonPropertyName("direction")] public string Direction { get; set; } = "";
    [JsonPropertyName("active")] public bool Active { get; set; } = true;
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

public sealed class DateTableSpec
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("spine")] public string Spine { get; set; } = "";
    [JsonPropertyName("fiscal_offset")] public int FiscalOffset { get; set; }
    [JsonPropertyName("key_columns")] public List<string> KeyColumns { get; set; } = new();
}

public sealed class Measure
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("table")] public string Table { get; set; } = "_Measures";
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("dax")] public string Dax { get; set; } = "";
    [JsonPropertyName("dependencies")] public List<string> Dependencies { get; set; } = new();
    [JsonPropertyName("display_folder")] public string DisplayFolder { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("business_goal_ref")] public string BusinessGoalRef { get; set; } = "";
}

public sealed class Kpi
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("measure_ref")] public string MeasureRef { get; set; } = "";
    [JsonPropertyName("target_logic")] public string TargetLogic { get; set; } = "";
    [JsonPropertyName("thresholds")] public KpiThresholds Thresholds { get; set; } = new();
    [JsonPropertyName("owner")] public string Owner { get; set; } = "";
    [JsonPropertyName("cadence")] public string Cadence { get; set; } = "";
    [JsonPropertyName("actionability")] public string Actionability { get; set; } = "";
    [JsonPropertyName("business_goal_ref")] public string BusinessGoalRef { get; set; } = "";
    [JsonPropertyName("data_source_ref")] public string DataSourceRef { get; set; } = "";
}

public sealed class KpiThresholds
{
    [JsonPropertyName("good")] public string Good { get; set; } = "";
    [JsonPropertyName("warning")] public string Warning { get; set; } = "";
    [JsonPropertyName("critical")] public string Critical { get; set; } = "";
}

public sealed class DashboardPage
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("purpose")] public string Purpose { get; set; } = "";
    [JsonPropertyName("audience")] public string Audience { get; set; } = "";
    [JsonPropertyName("layout")] public string Layout { get; set; } = ""; // Executive | Analytical | Operational | Detail
    [JsonPropertyName("storytelling_flow")] public string StorytellingFlow { get; set; } = "";
    [JsonPropertyName("slicers")] public List<Slicer> Slicers { get; set; } = new();
    [JsonPropertyName("visuals")] public List<Visual> Visuals { get; set; } = new();
    [JsonPropertyName("drill_through")] public DrillThrough? DrillThrough { get; set; }

    /// <summary>True when this page was produced by the tweak agent rather than the original generation pass.</summary>
    [JsonPropertyName("generated_by_tweak_agent")] public bool GeneratedByTweakAgent { get; set; } = false;
}

public sealed class Slicer
{
    [JsonPropertyName("field")] public string Field { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("synced")] public bool Synced { get; set; }
}

public sealed class Visual
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("position")] public string Position { get; set; } = "";
    [JsonPropertyName("measures")] public List<string> Measures { get; set; } = new();
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
}

public sealed class DrillThrough
{
    [JsonPropertyName("target_page")] public string TargetPage { get; set; } = "";
    [JsonPropertyName("trigger_fields")] public List<string> TriggerFields { get; set; } = new();
}

public sealed class SecurityBlock
{
    [JsonPropertyName("rls_required")] public bool RlsRequired { get; set; }
    [JsonPropertyName("roles")] public List<RlsRole> Roles { get; set; } = new();
    [JsonPropertyName("sensitivity_label")] public string SensitivityLabel { get; set; } = "";
    [JsonPropertyName("pii_columns")] public List<string> PiiColumns { get; set; } = new();
    [JsonPropertyName("compliance_obligations")] public List<string> ComplianceObligations { get; set; } = new();
    [JsonPropertyName("data_retention_notes")] public List<string> DataRetentionNotes { get; set; } = new();
    [JsonPropertyName("audit_trail_requirements")] public List<string> AuditTrailRequirements { get; set; } = new();
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = new();
}

public sealed class RlsRole
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("filter_table")] public string FilterTable { get; set; } = "";
    [JsonPropertyName("filter_dax")] public string FilterDax { get; set; } = "";
    [JsonPropertyName("business_owner")] public string BusinessOwner { get; set; } = "";
    [JsonPropertyName("access_level")] public string AccessLevel { get; set; } = "";
}

public sealed class GovernanceBlock
{
    [JsonPropertyName("data_owner")] public string DataOwner { get; set; } = "";
    [JsonPropertyName("kpi_owner")] public string KpiOwner { get; set; } = "";
    [JsonPropertyName("report_owner")] public string ReportOwner { get; set; } = "";
    [JsonPropertyName("business_steward")] public string BusinessSteward { get; set; } = "";
    [JsonPropertyName("access_steward")] public string AccessSteward { get; set; } = "";
    [JsonPropertyName("review_cadence")] public string ReviewCadence { get; set; } = "";
    [JsonPropertyName("change_control")] public string ChangeControl { get; set; } = "";
    [JsonPropertyName("roles_and_responsibilities")] public List<GovernanceRole> RolesAndResponsibilities { get; set; } = new();
}

public sealed class GovernanceRole
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("responsibility")] public string Responsibility { get; set; } = "";
    [JsonPropertyName("named_owner")] public string NamedOwner { get; set; } = "";
}
