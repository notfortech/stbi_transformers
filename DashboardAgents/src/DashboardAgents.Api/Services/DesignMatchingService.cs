using DashboardAgents.Core.Models;

namespace DashboardAgents.Api.Services;

public interface IDesignMatchingService
{
    Task<List<DesignOption>> MatchAsync(SchemaSnapshot schema, DataProfile profile, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ranks design options (blueprint templates) against the validated data profile using
/// keyword scoring against column names, types, and the detected industry signals.
/// When koru-main is configured, fetches the live template catalog; otherwise falls back
/// to a built-in set of design archetypes derived from the prompt knowledge packs.
/// </summary>
public sealed class DesignMatchingService : IDesignMatchingService
{
    private readonly KoruApiClient _koru;
    private readonly ILogger<DesignMatchingService> _logger;

    public DesignMatchingService(KoruApiClient koru, ILogger<DesignMatchingService> logger)
    {
        _koru = koru;
        _logger = logger;
    }

    public async Task<List<DesignOption>> MatchAsync(
        SchemaSnapshot schema,
        DataProfile profile,
        CancellationToken cancellationToken = default)
    {
        var columnNames = profile.Columns.Select(c => c.SuggestedName).ToList();
        var columnTypes = profile.Columns.ToDictionary(c => c.SuggestedName, c => c.InferredType);

        // Try to fetch templates from koru-main
        List<KoruTemplate>? koruTemplates = null;
        try
        {
            koruTemplates = await _koru.GetTemplatesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch templates from koru-main; using built-in archetypes.");
        }

        if (koruTemplates != null && koruTemplates.Count > 0)
            return RankKoruTemplates(koruTemplates, columnNames);

        return RankBuiltIn(columnNames, columnTypes, schema.DatabaseName);
    }

    private static List<DesignOption> RankKoruTemplates(List<KoruTemplate> templates, List<string> columns)
    {
        var colSet = new HashSet<string>(columns.Select(c => c.ToLowerInvariant()));

        return templates
            .Select(t =>
            {
                var reasons = new List<string>();
                var score = 0.0;

                var requiredCols = (t.RequiredColumns ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(c => c.ToLowerInvariant())
                    .ToList();

                var optionalCols = (t.OptionalColumns ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(c => c.ToLowerInvariant())
                    .ToList();

                var matchedRequired = requiredCols.Count(rc => colSet.Any(c => c.Contains(rc) || rc.Contains(c)));
                var matchedOptional = optionalCols.Count(oc => colSet.Any(c => c.Contains(oc) || oc.Contains(c)));

                if (requiredCols.Count > 0)
                {
                    score += (double)matchedRequired / requiredCols.Count * 0.7;
                    if (matchedRequired == requiredCols.Count)
                        reasons.Add("All required columns matched");
                    else if (matchedRequired > 0)
                        reasons.Add($"{matchedRequired}/{requiredCols.Count} required columns matched");
                }
                else
                {
                    score += 0.3;
                }

                if (optionalCols.Count > 0)
                {
                    score += (double)matchedOptional / optionalCols.Count * 0.3;
                    if (matchedOptional > 0)
                        reasons.Add($"{matchedOptional} optional column(s) matched");
                }

                // Industry keyword match in template name
                if (!string.IsNullOrEmpty(t.Industry))
                    reasons.Add($"Industry: {t.Industry}");

                return new DesignOption
                {
                    TemplateId = t.Id.ToString(),
                    Name = t.TemplateName ?? "Untitled",
                    Industry = t.Industry ?? "",
                    Description = t.Description,
                    MatchScore = Math.Round(Math.Min(score, 1.0), 3),
                    MatchReasons = reasons,
                    SupportedCapabilities = t.SupportedCapabilities ?? new()
                };
            })
            .OrderByDescending(o => o.MatchScore)
            .Take(5)
            .ToList();
    }

    private static List<DesignOption> RankBuiltIn(
        List<string> columns,
        Dictionary<string, string> columnTypes,
        string dbName)
    {
        var colStr = string.Join(" ", columns).ToLowerInvariant();
        var hasDate = columnTypes.Values.Any(t => t == "datetime") || colStr.Contains("date");
        var hasAmount = colStr.Contains("amount") || colStr.Contains("revenue") || colStr.Contains("cost")
                        || colStr.Contains("budget") || colStr.Contains("invoice") || colStr.Contains("payment");
        var hasClient = colStr.Contains("client") || colStr.Contains("customer") || colStr.Contains("participant");
        var hasStaff = colStr.Contains("staff") || colStr.Contains("employee") || colStr.Contains("worker");
        var hasNdis = colStr.Contains("ndis") || colStr.Contains("plan") || colStr.Contains("support_category");
        var hasBilling = colStr.Contains("bill") || colStr.Contains("invoice") || colStr.Contains("claim");

        var archetypes = new List<(string id, string name, string industry, string desc, double score, List<string> reasons, List<string> caps)>
        {
            ("built-in:financial-overview", "Financial Performance Overview",
                "Cross-Industry", "Revenue, cost and margin tracking across periods.",
                hasAmount && hasDate ? 0.85 : 0.4,
                BuildReasons(hasAmount, "financial columns", hasDate, "date columns"),
                new() { "Revenue Tracking", "Cost Analysis", "Margin Reporting" }),

            ("built-in:ndis-plan-utilisation", "NDIS Plan Utilisation & Budget Burn-Rate",
                "NDIS / Disability Services", "Track participant funding utilisation against plan budgets.",
                hasNdis ? 0.92 : hasClient && hasAmount ? 0.55 : 0.2,
                BuildReasons(hasNdis, "NDIS-specific columns", hasClient, "participant columns"),
                new() { "Budget Burn-Rate", "Plan Utilisation", "Support Category Breakdown" }),

            ("built-in:billing-revenue", "Billing & Revenue Reconciliation",
                "Professional Services", "Invoice ageing, payment tracking and revenue recognition.",
                hasBilling && hasAmount ? 0.88 : hasAmount ? 0.5 : 0.3,
                BuildReasons(hasBilling, "billing columns", hasAmount, "financial columns"),
                new() { "Invoice Ageing", "Payment Tracking", "Revenue Recognition" }),

            ("built-in:workforce-utilisation", "Workforce Utilisation Dashboard",
                "Professional Services / Healthcare", "Staff hours, utilisation rates and capacity planning.",
                hasStaff && hasDate ? 0.82 : hasStaff ? 0.6 : 0.25,
                BuildReasons(hasStaff, "staff/workforce columns", hasDate, "date/time columns"),
                new() { "Utilisation Rate", "Capacity Planning", "Staff Hours" }),

            ("built-in:client-engagement", "Client Engagement & Retention",
                "Cross-Industry", "Client activity, retention rates and engagement metrics.",
                hasClient && hasDate ? 0.78 : hasClient ? 0.55 : 0.2,
                BuildReasons(hasClient, "client/customer columns", hasDate, "date columns"),
                new() { "Client Activity", "Retention Rate", "Engagement Score" }),
        };

        return archetypes
            .Select(a => new DesignOption
            {
                TemplateId = a.id,
                Name = a.name,
                Industry = a.industry,
                Description = a.desc,
                MatchScore = a.score,
                MatchReasons = a.reasons,
                SupportedCapabilities = a.caps
            })
            .OrderByDescending(o => o.MatchScore)
            .ToList();
    }

    private static List<string> BuildReasons(bool cond1, string label1, bool cond2, string label2)
    {
        var r = new List<string>();
        if (cond1) r.Add($"Detected {label1}");
        if (cond2) r.Add($"Detected {label2}");
        if (r.Count == 0) r.Add("General purpose match");
        return r;
    }
}
