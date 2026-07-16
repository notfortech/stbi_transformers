namespace DashboardAgents.Core.Models;

/// <summary>Options every blueprint generation call needs, independent of how the schema was sourced.</summary>
public sealed class BlueprintGenerationOptions
{
    /// <summary>"requirements" or "schema" — mirrors the two input modes in the original tool.</summary>
    public string Mode { get; set; } = "schema";
    public string? Requirements { get; set; }
    public string? SchemaText { get; set; }
    public string? IndustryExplicit { get; set; }
    public string Audience { get; set; } = "Executive";
    public string Currency { get; set; } = "AUD";
    public string FiscalYearStart { get; set; } = "July";
    public bool RlsRequired { get; set; } = true;
    public string RefreshCadence { get; set; } = "Daily";
    public string? BusinessGoal { get; set; }
    public string? KnowledgePack { get; set; }
}

/// <summary>Request to introspect a live database and turn it directly into a blueprint.</summary>
public sealed class ConnectAndGenerateRequest
{
    public DbProvider Provider { get; set; }
    public string ConnectionString { get; set; } = "";

    /// <summary>Optional allow-list of schemas to introspect (e.g. ["dbo"]). Empty = all non-system schemas.</summary>
    public List<string>? SchemaFilter { get; set; }

    public BlueprintGenerationOptions Options { get; set; } = new();
}

/// <summary>Request to introspect a live database without generating a blueprint yet.</summary>
public sealed class IntrospectRequest
{
    public DbProvider Provider { get; set; }
    public string ConnectionString { get; set; } = "";
    public List<string>? SchemaFilter { get; set; }
}

/// <summary>Request to generate a blueprint from already-known inputs (pasted schema or requirements text).</summary>
public sealed class GenerateBlueprintRequest
{
    public BlueprintGenerationOptions Options { get; set; } = new();
}

/// <summary>
/// Request to author TMDL directly from an in-hand blueprint, rather than one looked up by id
/// from IBlueprintStore. Needed because PipelineController.Generate (the AI-assisted flow
/// koru-main's ReportDesignerClient actually calls) returns its Blueprint straight in the HTTP
/// response and never persists it — unlike BlueprintController.Generate/FromConnection, which
/// does save to IBlueprintStore. Rather than making the pipeline flow persist a blueprint it
/// never needed to before, the caller (koru-main) just sends back the blueprint it already has.
/// </summary>
public sealed class AuthorTmdlRequest
{
    public Blueprint Blueprint { get; set; } = new();
}

/// <summary>Request to adapt an existing blueprint to a specific use-case scenario.</summary>
public sealed class TweakRequest
{
    public string BlueprintId { get; set; } = "";
    public string Scenario { get; set; } = "";

    /// <summary>If true, the new/matched page(s) are appended to the stored blueprint. If false, they are returned only.</summary>
    public bool PersistToBlueprint { get; set; } = true;
}

public sealed class TweakResult
{
    public string Mode { get; set; } = ""; // "matched_existing" | "composed_new"
    public List<DashboardPage> Pages { get; set; } = new();
    public string Explanation { get; set; } = "";

    /// <summary>Field names the tweak agent referenced, for auditability of the allow-list constraint.</summary>
    public List<string> FieldsUsed { get; set; } = new();
}
