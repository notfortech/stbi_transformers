using DashboardAgents.Core.Models;

namespace DashboardAgents.BlueprintAgent;

public sealed class BlueprintValidationResult
{
    public bool IsValid => Violations.Count == 0;
    public List<string> Violations { get; } = new();
}

/// <summary>
/// Enforces the "Mandatory Fields" table in DashboardBlueprintSchema.md server-side. The system
/// prompt asks the model to self-enforce these, but the API must not silently accept a blueprint
/// that violates its own contract — this is the backstop.
/// </summary>
public static class BlueprintValidator
{
    private const int MinFactTables = 2;
    private const int MinDimensionTables = 3;
    private const int MinRelationships = 5;
    private const int MinMeasures = 10;
    private const int MinKpis = 6;
    private const int MinPages = 5;
    private const int MinExecutiveQuestions = 8;
    private const int RequiredGateCount = 9;

    // A single source table can't legitimately decompose into 2 fact tables (that implies 2
    // distinct business processes/grains), so the full star-schema minimums above only apply
    // when the input has 2+ source tables or table count is unknown (requirements mode / pasted
    // schema text). For a genuinely single-table input, one fact table plus whatever dimensions
    // (e.g. category, date) can be derived from its columns is the realistic ceiling.
    private const int SingleTableMinFactTables = 1;
    private const int SingleTableMinDimensionTables = 2;
    private const int SingleTableMinRelationships = 2;

    public static BlueprintValidationResult Validate(Blueprint blueprint, int? sourceTableCount = null)
    {
        var result = new BlueprintValidationResult();

        var isSingleTableInput = sourceTableCount == 1;
        var minFactTables = isSingleTableInput ? SingleTableMinFactTables : MinFactTables;
        var minDimensionTables = isSingleTableInput ? SingleTableMinDimensionTables : MinDimensionTables;
        var minRelationships = isSingleTableInput ? SingleTableMinRelationships : MinRelationships;

        if (blueprint.DataModel.FactTables.Count < minFactTables)
            result.Violations.Add($"data_model.fact_tables must have at least {minFactTables} entries (found {blueprint.DataModel.FactTables.Count}).");

        if (blueprint.DataModel.DimensionTables.Count < minDimensionTables)
            result.Violations.Add($"data_model.dimension_tables must have at least {minDimensionTables} entries (found {blueprint.DataModel.DimensionTables.Count}).");

        if (blueprint.DataModel.Relationships.Count < minRelationships)
            result.Violations.Add($"data_model.relationships must have at least {minRelationships} entries (found {blueprint.DataModel.Relationships.Count}).");

        if (string.IsNullOrWhiteSpace(blueprint.DataModel.DateTable.Name))
            result.Violations.Add("data_model.date_table is missing.");

        if (blueprint.Measures.Count < MinMeasures)
            result.Violations.Add($"measures must have at least {MinMeasures} entries (found {blueprint.Measures.Count}).");

        if (blueprint.Kpis.Count < MinKpis)
            result.Violations.Add($"kpis must have at least {MinKpis} entries (found {blueprint.Kpis.Count}).");
        foreach (var kpi in blueprint.Kpis)
        {
            if (string.IsNullOrWhiteSpace(kpi.Owner))
                result.Violations.Add($"KPI '{kpi.Name}' is missing a named owner.");
            if (string.IsNullOrWhiteSpace(kpi.Actionability))
                result.Violations.Add($"KPI '{kpi.Name}' is missing actionability.");
            if (string.IsNullOrWhiteSpace(kpi.BusinessGoalRef))
                result.Violations.Add($"KPI '{kpi.Name}' is missing business_goal_ref.");
        }

        if (blueprint.Pages.Count < MinPages)
            result.Violations.Add($"pages must have at least {MinPages} entries (found {blueprint.Pages.Count}).");
        if (!blueprint.Pages.Any(p => p.Layout.Equals("Executive", StringComparison.OrdinalIgnoreCase)))
            result.Violations.Add("pages must include at least one Executive layout page.");

        if (blueprint.ExecutiveQuestions.Count < MinExecutiveQuestions)
            result.Violations.Add($"executive_questions must have at least {MinExecutiveQuestions} entries (found {blueprint.ExecutiveQuestions.Count}).");

        if (blueprint.Governance.RolesAndResponsibilities.Count == 0
            || string.IsNullOrWhiteSpace(blueprint.Governance.DataOwner)
            || string.IsNullOrWhiteSpace(blueprint.Governance.KpiOwner)
            || string.IsNullOrWhiteSpace(blueprint.Governance.ReportOwner)
            || string.IsNullOrWhiteSpace(blueprint.Governance.BusinessSteward)
            || string.IsNullOrWhiteSpace(blueprint.Governance.AccessSteward))
        {
            result.Violations.Add("governance must define all 5 named roles (data_owner, kpi_owner, report_owner, business_steward, access_steward).");
        }

        if (blueprint.SelfReview.Gates.Count != RequiredGateCount)
            result.Violations.Add($"self_review.gates must contain exactly {RequiredGateCount} gates (found {blueprint.SelfReview.Gates.Count}).");

        if (string.IsNullOrWhiteSpace(blueprint.Confidence.Band))
            result.Violations.Add("confidence.band is missing.");

        ScanForForbiddenLanguage(blueprint, result);

        return result;
    }

    /// <summary>
    /// Server-side guardrail mirroring AgentInstructions.md's "Forbidden Output" table — catches
    /// data-quality/audit claims the agent must never make since it has no access to live data.
    /// </summary>
    private static void ScanForForbiddenLanguage(Blueprint blueprint, BlueprintValidationResult result)
    {
        string[] forbiddenPhrases =
        {
            "data quality issues detected", "missing values found", "duplicate records",
            "data has been cleaned", "auto-corrected", "rls has been verified",
            "security is complete", "compliance has been achieved", "data reliability is",
            "report performance is", "refresh failed on"
        };

        var haystack = string.Join(" ", blueprint.SelfReview.Assumptions
            .Concat(blueprint.SelfReview.Warnings)
            .Concat(blueprint.SemanticNotes)).ToLowerInvariant();

        foreach (var phrase in forbiddenPhrases)
        {
            if (haystack.Contains(phrase))
                result.Violations.Add($"Blueprint contains forbidden language pattern: \"{phrase}\". Agent must not make data-quality or audit claims.");
        }
    }
}
