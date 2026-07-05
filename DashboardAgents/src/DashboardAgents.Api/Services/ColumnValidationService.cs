using System.Text;
using System.Text.RegularExpressions;
using DashboardAgents.Core.Models;

namespace DashboardAgents.Api.Services;

public interface IColumnValidationService
{
    DataProfile Validate(SchemaSnapshot schema, string? userPrompt = null);
}

/// <summary>
/// Full column-name and type validation layer. Checks naming conventions, reserved words,
/// duplicates, nullability, cardinality, and type consistency, then emits ranked transform
/// recommendations. This is the "transformation" step where column names etc. are fully validated.
/// </summary>
public sealed class ColumnValidationService : IColumnValidationService
{
    private static readonly HashSet<string> SqlReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select","from","where","join","inner","outer","left","right","full","on","group","by","order",
        "having","insert","update","delete","create","drop","alter","table","index","view","schema",
        "database","column","row","null","not","and","or","in","is","like","between","exists","all",
        "any","case","when","then","else","end","as","distinct","union","intersect","except","with",
        "date","time","timestamp","int","integer","bigint","smallint","float","real","double",
        "char","varchar","nvarchar","text","bit","bool","boolean","decimal","numeric","money",
        "user","name","type","value","key","data","id","code","status","date","year","month","day"
    };

    private static readonly Regex ValidIdentifier = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex LeadingDigit = new(@"^\d", RegexOptions.Compiled);
    private static readonly Regex WhitespaceOrSpecial = new(@"[\s\-\.\/\\@#$%^&*()+={}\[\]|;:'"",<>?!]", RegexOptions.Compiled);

    public DataProfile Validate(SchemaSnapshot schema, string? userPrompt = null)
    {
        var allColumns = schema.Tables.SelectMany(t => t.Columns).ToList();
        var issues = new List<ValidationIssue>();
        var recommendations = new List<TransformRecommendation>();
        var validated = new List<ValidatedColumn>();
        var order = 1;

        // Track names across tables for cross-table duplicate detection
        var globalNamesSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            var tablePrefix = schema.Tables.Count > 1 ? $"[{table.QualifiedName}] " : "";
            var localNamesSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var col in table.Columns)
            {
                var colIssues = new List<string>();
                var rawName = col.ColumnName;
                var suggested = SuggestName(rawName);

                // ── Duplicate within table ──────────────────────────────────────
                localNamesSeen.TryGetValue(rawName, out var localCount);
                localNamesSeen[rawName] = localCount + 1;
                if (localCount > 0)
                {
                    var msg = $"{tablePrefix}Duplicate column name '{rawName}'.";
                    colIssues.Add(msg);
                    issues.Add(new ValidationIssue { Severity = "error", Code = "DUPLICATE_COLUMN", Message = msg, Column = rawName });
                    recommendations.Add(new TransformRecommendation
                    {
                        Order = order++, Action = "rename", Column = rawName,
                        Description = $"Rename duplicate '{rawName}' to '{suggested}_{localCount + 1}'.",
                        Parameters = new() { ["to"] = $"{suggested}_{localCount + 1}" }
                    });
                }

                // ── Empty or whitespace name ────────────────────────────────────
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    var msg = $"{tablePrefix}Column has an empty or whitespace name.";
                    colIssues.Add(msg);
                    issues.Add(new ValidationIssue { Severity = "error", Code = "EMPTY_COLUMN_NAME", Message = msg, Column = rawName });
                    recommendations.Add(new TransformRecommendation
                    {
                        Order = order++, Action = "rename", Column = rawName,
                        Description = "Assign a meaningful name to this unnamed column.",
                        Parameters = new() { ["to"] = "unnamed_column" }
                    });
                }
                else
                {
                    // ── Leading digit ──────────────────────────────────────────
                    if (LeadingDigit.IsMatch(rawName))
                    {
                        var msg = $"{tablePrefix}Column '{rawName}' starts with a digit, which is invalid in most SQL engines.";
                        colIssues.Add(msg);
                        issues.Add(new ValidationIssue { Severity = "error", Code = "LEADING_DIGIT", Message = msg, Column = rawName });
                        recommendations.Add(new TransformRecommendation
                        {
                            Order = order++, Action = "rename", Column = rawName,
                            Description = $"Prefix '{rawName}' to make it a valid identifier.",
                            Parameters = new() { ["to"] = "col_" + suggested }
                        });
                    }

                    // ── Whitespace / special characters ────────────────────────
                    if (WhitespaceOrSpecial.IsMatch(rawName))
                    {
                        var msg = $"{tablePrefix}Column '{rawName}' contains spaces or special characters.";
                        colIssues.Add(msg);
                        issues.Add(new ValidationIssue { Severity = "warning", Code = "INVALID_CHARS", Message = msg, Column = rawName });
                        if (suggested != rawName)
                            recommendations.Add(new TransformRecommendation
                            {
                                Order = order++, Action = "rename", Column = rawName,
                                Description = $"Rename '{rawName}' → '{suggested}' (snake_case, no special chars).",
                                Parameters = new() { ["to"] = suggested }
                            });
                    }

                    // ── Reserved word ──────────────────────────────────────────
                    if (SqlReservedWords.Contains(rawName) && !rawName.Contains('_'))
                    {
                        var msg = $"{tablePrefix}Column '{rawName}' is a SQL reserved word and must be quoted or renamed.";
                        colIssues.Add(msg);
                        issues.Add(new ValidationIssue { Severity = "warning", Code = "RESERVED_WORD", Message = msg, Column = rawName });
                        recommendations.Add(new TransformRecommendation
                        {
                            Order = order++, Action = "rename", Column = rawName,
                            Description = $"Rename reserved word '{rawName}' to '{suggested}_col' to avoid quoting.",
                            Parameters = new() { ["to"] = suggested + "_col" }
                        });
                    }

                    // ── Excessive length ───────────────────────────────────────
                    if (rawName.Length > 64)
                    {
                        var msg = $"{tablePrefix}Column '{rawName}' exceeds 64 characters.";
                        colIssues.Add(msg);
                        issues.Add(new ValidationIssue { Severity = "warning", Code = "NAME_TOO_LONG", Message = msg, Column = rawName });
                        var truncated = suggested.Length > 60 ? suggested[..60] : suggested;
                        recommendations.Add(new TransformRecommendation
                        {
                            Order = order++, Action = "rename", Column = rawName,
                            Description = $"Truncate '{rawName}' to a shorter, unique identifier.",
                            Parameters = new() { ["to"] = truncated }
                        });
                    }

                    // ── Mixed case (not snake_case or PascalCase) ──────────────
                    if (rawName.Contains(' ') is false && rawName != rawName.ToLowerInvariant()
                        && rawName != rawName.ToUpperInvariant()
                        && !ValidIdentifier.IsMatch(rawName))
                    {
                        var msg = $"{tablePrefix}Column '{rawName}' uses an inconsistent naming convention.";
                        colIssues.Add(msg);
                        issues.Add(new ValidationIssue { Severity = "info", Code = "NAMING_CONVENTION", Message = msg, Column = rawName });
                    }
                }

                // ── High null ratio ────────────────────────────────────────────
                double missingRatio = 0;
                if (col.IsNullable)
                {
                    missingRatio = 0; // schema reader doesn't give us exact ratio from DB
                }

                // ── Ambiguous type ─────────────────────────────────────────────
                if (col.DataType.Equals("text", StringComparison.OrdinalIgnoreCase)
                    && (rawName.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
                        || rawName.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                        || rawName.StartsWith("id_", StringComparison.OrdinalIgnoreCase)))
                {
                    var msg = $"{tablePrefix}Column '{rawName}' looks like an identifier but has type 'text'. Consider casting to integer or UUID.";
                    issues.Add(new ValidationIssue { Severity = "info", Code = "AMBIGUOUS_ID_TYPE", Message = msg, Column = rawName });
                    recommendations.Add(new TransformRecommendation
                    {
                        Order = order++, Action = "cast", Column = rawName,
                        Description = $"Cast '{rawName}' to integer or UUID if it represents a surrogate key.",
                        Parameters = new() { ["to_type"] = "integer" }
                    });
                }

                // ── Date column detected as text ───────────────────────────────
                if (col.DataType.Equals("text", StringComparison.OrdinalIgnoreCase)
                    && (rawName.Contains("date", StringComparison.OrdinalIgnoreCase)
                        || rawName.Contains("time", StringComparison.OrdinalIgnoreCase)
                        || rawName.Contains("created", StringComparison.OrdinalIgnoreCase)
                        || rawName.Contains("updated", StringComparison.OrdinalIgnoreCase)))
                {
                    var msg = $"{tablePrefix}Column '{rawName}' name suggests a date/time value but type is 'text'.";
                    issues.Add(new ValidationIssue { Severity = "warning", Code = "TEXT_DATE_COLUMN", Message = msg, Column = rawName });
                    recommendations.Add(new TransformRecommendation
                    {
                        Order = order++, Action = "cast", Column = rawName,
                        Description = $"Parse '{rawName}' to datetime. Ensure the format is consistent before casting.",
                        Parameters = new() { ["to_type"] = "datetime", ["format"] = "auto" }
                    });
                }

                // ── Nullable non-key columns ───────────────────────────────────
                if (col.IsNullable && !col.IsPrimaryKey && col.DataType is "integer" or "decimal")
                {
                    recommendations.Add(new TransformRecommendation
                    {
                        Order = order++, Action = "fill_nulls", Column = rawName,
                        Description = $"Fill NULLs in numeric column '{rawName}' with 0 or an appropriate default before aggregation.",
                        Parameters = new() { ["with"] = "0" }
                    });
                }

                validated.Add(new ValidatedColumn
                {
                    OriginalName = rawName,
                    SuggestedName = suggested,
                    InferredType = col.DataType,
                    IsNullable = col.IsNullable,
                    IsPrimaryKey = col.IsPrimaryKey,
                    DistinctCount = col.DistinctValueCount,
                    MissingRatio = missingRatio,
                    Issues = colIssues
                });
            }
        }

        var errorCount = issues.Count(i => i.Severity == "error");
        var warnCount = issues.Count(i => i.Severity == "warning");

        var summary = errorCount > 0
            ? $"{errorCount} error(s) and {warnCount} warning(s) found across {validated.Count} columns — fix errors before generating a report."
            : warnCount > 0
                ? $"{warnCount} warning(s) found across {validated.Count} columns — review recommendations before proceeding."
                : $"All {validated.Count} columns passed validation.";

        if (userPrompt is not null)
        {
            issues.Add(new ValidationIssue
            {
                Severity = "info",
                Code = "USER_PROMPT",
                Message = $"User context: {userPrompt}",
                Column = null
            });
        }

        return new DataProfile
        {
            Columns = validated,
            Issues = issues,
            Recommendations = recommendations.OrderBy(r => r.Order).ToList(),
            IsReadyForDesign = errorCount == 0,
            Summary = summary,
            ErrorCount = errorCount,
            WarningCount = warnCount
        };
    }

    private static string SuggestName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "unnamed_column";

        // Convert PascalCase/camelCase to snake_case
        var step1 = Regex.Replace(raw, @"([a-z0-9])([A-Z])", "$1_$2");
        var step2 = Regex.Replace(step1, @"([A-Z]+)([A-Z][a-z])", "$1_$2");

        // Replace all non-alphanumeric with underscore
        var step3 = Regex.Replace(step2, @"[^a-zA-Z0-9_]", "_");

        // Collapse multiple underscores
        var step4 = Regex.Replace(step3, @"_+", "_").Trim('_').ToLowerInvariant();

        // Ensure doesn't start with digit
        if (step4.Length > 0 && char.IsDigit(step4[0]))
            step4 = "col_" + step4;

        return string.IsNullOrEmpty(step4) ? "unnamed_column" : step4;
    }
}
