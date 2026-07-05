using System.Text.Json.Serialization;

namespace DashboardAgents.Core.Models;

public sealed class QualityFrameworks
{
    [JsonPropertyName("audit_readiness")] public AuditReadiness AuditReadiness { get; set; } = new();
    [JsonPropertyName("dashboard_quality")] public DashboardQuality DashboardQuality { get; set; } = new();
    [JsonPropertyName("kpi_quality")] public KpiQuality KpiQuality { get; set; } = new();
    [JsonPropertyName("semantic_model_quality")] public SemanticModelQuality SemanticModelQuality { get; set; } = new();
    [JsonPropertyName("governance_framework")] public GovernanceFramework GovernanceFramework { get; set; } = new();
}

public sealed class AuditReadiness
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("rating")] public string Rating { get; set; } = "";
    [JsonPropertyName("strengths")] public List<string> Strengths { get; set; } = new();
    [JsonPropertyName("risks")] public List<string> Risks { get; set; } = new();
    [JsonPropertyName("missing_requirements")] public List<string> MissingRequirements { get; set; } = new();
    [JsonPropertyName("checklist")] public List<ChecklistItem> Checklist { get; set; } = new();
}

public sealed class ChecklistItem
{
    [JsonPropertyName("item")] public string Item { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = ""; // PASS | WARN | FAIL
    [JsonPropertyName("evidence")] public string Evidence { get; set; } = "";
    [JsonPropertyName("priority")] public string Priority { get; set; } = "";
}

public sealed class DashboardQuality
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("rating")] public string Rating { get; set; } = "";
    [JsonPropertyName("strengths")] public List<string> Strengths { get; set; } = new();
    [JsonPropertyName("risks")] public List<string> Risks { get; set; } = new();
    [JsonPropertyName("recommendations")] public List<string> Recommendations { get; set; } = new();
}

public sealed class KpiQuality
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("rating")] public string Rating { get; set; } = "";
    [JsonPropertyName("missing_ownership")] public List<string> MissingOwnership { get; set; } = new();
    [JsonPropertyName("missing_targets")] public List<string> MissingTargets { get; set; } = new();
    [JsonPropertyName("recommendations")] public List<string> Recommendations { get; set; } = new();
}

public sealed class SemanticModelQuality
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("rating")] public string Rating { get; set; } = "";
    [JsonPropertyName("dimensions")] public Dictionary<string, ScoredDimension> Dimensions { get; set; } = new();
    [JsonPropertyName("strengths")] public List<string> Strengths { get; set; } = new();
    [JsonPropertyName("risks")] public List<string> Risks { get; set; } = new();
    [JsonPropertyName("recommendations")] public List<string> Recommendations { get; set; } = new();
}

public sealed class ScoredDimension
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
}

public sealed class GovernanceFramework
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("rating")] public string Rating { get; set; } = "";
    [JsonPropertyName("industry_obligations")] public List<string> IndustryObligations { get; set; } = new();
    [JsonPropertyName("compliance_controls")] public List<string> ComplianceControls { get; set; } = new();
    [JsonPropertyName("data_stewardship")] public List<DataStewardship> DataStewardship { get; set; } = new();
    [JsonPropertyName("recommended_policies")] public List<string> RecommendedPolicies { get; set; } = new();
    [JsonPropertyName("gaps")] public List<string> Gaps { get; set; } = new();
}

public sealed class DataStewardship
{
    [JsonPropertyName("domain")] public string Domain { get; set; } = "";
    [JsonPropertyName("owner")] public string Owner { get; set; } = "";
    [JsonPropertyName("responsibility")] public string Responsibility { get; set; } = "";
}

public sealed class ExpectedTargets
{
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("fact_tables")] public List<string> FactTables { get; set; } = new();
    [JsonPropertyName("dimension_tables")] public List<string> DimensionTables { get; set; } = new();
    [JsonPropertyName("kpi_names")] public List<string> KpiNames { get; set; } = new();
    [JsonPropertyName("page_names")] public List<string> PageNames { get; set; } = new();
    [JsonPropertyName("security_roles")] public List<string> SecurityRoles { get; set; } = new();
    [JsonPropertyName("pii_columns")] public List<string> PiiColumns { get; set; } = new();
    [JsonPropertyName("measure_count_minimum")] public int MeasureCountMinimum { get; set; }
    [JsonPropertyName("kpi_count_minimum")] public int KpiCountMinimum { get; set; }
    [JsonPropertyName("page_count_minimum")] public int PageCountMinimum { get; set; }
    [JsonPropertyName("rls_role_count_minimum")] public int RlsRoleCountMinimum { get; set; }
    [JsonPropertyName("compliance_checks")] public List<ComplianceCheck> ComplianceChecks { get; set; } = new();
}

public sealed class ComplianceCheck
{
    [JsonPropertyName("check")] public string Check { get; set; } = "";
    [JsonPropertyName("expected")] public string Expected { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
}

public sealed class SelfReview
{
    [JsonPropertyName("gates")] public List<ReviewGate> Gates { get; set; } = new();
    [JsonPropertyName("overall_verdict")] public string OverallVerdict { get; set; } = ""; // PASS | PASS_WITH_NOTES | REVISE
    [JsonPropertyName("composite_score")] public double CompositeScore { get; set; }
    [JsonPropertyName("design_recommendations")] public List<DesignRecommendation> DesignRecommendations { get; set; } = new();
    [JsonPropertyName("assumptions")] public List<string> Assumptions { get; set; } = new();
    [JsonPropertyName("design_risks")] public List<DesignRisk> DesignRisks { get; set; } = new();
    [JsonPropertyName("implementation_gaps")] public List<string> ImplementationGaps { get; set; } = new();
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
    [JsonPropertyName("implementation_risks")] public List<string> ImplementationRisks { get; set; } = new();
}

public sealed class ReviewGate
{
    [JsonPropertyName("gate_name")] public string GateName { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = ""; // PASS | WARN | FAIL
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("findings")] public List<string> Findings { get; set; } = new();
    [JsonPropertyName("recommendations")] public List<string> Recommendations { get; set; } = new();
}

public sealed class DesignRecommendation
{
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("recommendation")] public string Recommendation { get; set; } = "";
    [JsonPropertyName("rationale")] public string Rationale { get; set; } = "";
    [JsonPropertyName("priority")] public string Priority { get; set; } = "";
}

public sealed class DesignRisk
{
    [JsonPropertyName("risk")] public string Risk { get; set; } = "";
    [JsonPropertyName("mitigation")] public string Mitigation { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
}

public sealed class ConfidenceBlock
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("band")] public string Band { get; set; } = "";
    [JsonPropertyName("basis")] public string Basis { get; set; } = "";
    [JsonPropertyName("dimension_scores")] public ConfidenceDimensions DimensionScores { get; set; } = new();
    [JsonPropertyName("assumptions")] public List<string> Assumptions { get; set; } = new();
    [JsonPropertyName("gaps")] public List<string> Gaps { get; set; } = new();
}

public sealed class ConfidenceDimensions
{
    [JsonPropertyName("industry_confidence")] public int IndustryConfidence { get; set; }
    [JsonPropertyName("capability_confidence")] public int CapabilityConfidence { get; set; }
    [JsonPropertyName("goal_confidence")] public int GoalConfidence { get; set; }
    [JsonPropertyName("semantic_model_completeness")] public int SemanticModelCompleteness { get; set; }
    [JsonPropertyName("kpi_completeness")] public int KpiCompleteness { get; set; }
    [JsonPropertyName("dashboard_completeness")] public int DashboardCompleteness { get; set; }
    [JsonPropertyName("governance_completeness")] public int GovernanceCompleteness { get; set; }
}
