using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DashboardAgents.Core.Models;
using DashboardAgents.Core.Services;
using DashboardAgents.Llm;
using Microsoft.Extensions.Logging;

namespace DashboardAgents.BlueprintAgent;

public interface ITmdlAuthoringService
{
    Task<TmdlAuthoringResult> AuthorAsync(Blueprint blueprint, string correlationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// S7 — the "Claude-agent" story: turns an already-approved Blueprint (business-level JSON —
/// tables, measures, KPIs, pages) into an actual TMDL semantic model definition (database.tmdl,
/// model.tmdl, relationships.tmdl, expressions.tmdl, cultures/en-US.tmdl, tables/*.tmdl) — the
/// same file layout as the hand-authored DashboardTemplateLibrary templates in koru-main.
///
/// This step is deliberately LLM-driven rather than a fixed deterministic mapping: the blueprint
/// schema only gives dimension tables a name, type, key_columns, and hierarchies — not a full
/// column list the way fact tables get one (see DataModel.DimensionTable in Blueprint.cs).
/// Synthesizing a coherent, correctly-typed column set from those signals (plus what measures
/// and relationships imply about the table) needs semantic judgement a fixed mapping can't
/// reliably provide. What CAN be verified deterministically — is the output syntactically valid
/// TMDL, does every relationship/measure reference a column that actually exists — is S8's job,
/// not this one's. This service only proposes; it never deploys and never touches real data.
/// </summary>
public sealed class TmdlAuthoringService : ITmdlAuthoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILlmClient _llm;
    private readonly ILogger<TmdlAuthoringService> _logger;
    private readonly IAiBoundaryAuditPublisher _audit;

    public TmdlAuthoringService(ILlmClient llm, ILogger<TmdlAuthoringService> logger, IAiBoundaryAuditPublisher audit)
    {
        _llm = llm;
        _logger = logger;
        _audit = audit;
    }

    public async Task<TmdlAuthoringResult> AuthorAsync(Blueprint blueprint, string correlationId, CancellationToken cancellationToken = default)
    {
        if (blueprint.DataModel.FactTables.Count == 0)
            throw new ArgumentException("Blueprint has no fact tables — nothing to author a semantic model from.");

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(blueprint);

        _logger.LogInformation(
            "TmdlAuthoring.Requested BlueprintId={BlueprintId} FactTables={FactTables} DimensionTables={DimensionTables} Measures={Measures}",
            blueprint.BlueprintId, blueprint.DataModel.FactTables.Count, blueprint.DataModel.DimensionTables.Count, blueprint.Measures.Count);

        await _audit.LogSentAsync(
            "TmdlAuthoringService", "AuthorTmdl", correlationId, _llm.ProviderName,
            new
            {
                blueprintId = blueprint.BlueprintId,
                factTableCount = blueprint.DataModel.FactTables.Count,
                dimensionTableCount = blueprint.DataModel.DimensionTables.Count,
                measureCount = blueprint.Measures.Count
            },
            cancellationToken);

        var sw = Stopwatch.StartNew();
        string rawResponse;
        try
        {
            rawResponse = await _llm.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            await _audit.LogFailedAsync(
                "TmdlAuthoringService", "AuthorTmdl", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, $"LLM call failed: {ex.GetType().Name}", cancellationToken);
            throw;
        }
        sw.Stop();

        TmdlAuthoringResult result;
        try
        {
            using var doc = JsonExtraction.ExtractJsonDocument(rawResponse);
            result = JsonSerializer.Deserialize<TmdlAuthoringResult>(doc.RootElement.GetRawText(), JsonOptions)
                ?? throw new BlueprintParseException("Deserialized TMDL authoring result was null.");
        }
        catch (BlueprintParseException ex)
        {
            _logger.LogError(ex, "TmdlAuthoring.ParseFailed BlueprintId={BlueprintId}", blueprint.BlueprintId);
            await _audit.LogFailedAsync(
                "TmdlAuthoringService", "AuthorTmdl", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, "Response could not be parsed.", cancellationToken);
            throw;
        }

        if (result.Files.Count == 0)
        {
            await _audit.LogFailedAsync(
                "TmdlAuthoringService", "AuthorTmdl", correlationId, _llm.ProviderName,
                sw.ElapsedMilliseconds, "Result had no files.", cancellationToken);
            throw new BlueprintParseException("TMDL authoring result had no files.");
        }

        _logger.LogInformation(
            "TmdlAuthoring.Completed BlueprintId={BlueprintId} FileCount={FileCount} DurationMs={DurationMs}",
            blueprint.BlueprintId, result.Files.Count, sw.ElapsedMilliseconds);

        await _audit.LogReceivedAsync(
            "TmdlAuthoringService", "AuthorTmdl", correlationId, _llm.ProviderName,
            new { fileCount = result.Files.Count, paths = result.Files.Select(f => f.Path) },
            sw.ElapsedMilliseconds, cancellationToken);

        return result;
    }

    private static string BuildSystemPrompt() => """
        You are a Power BI semantic-model author. You convert an approved analytics blueprint
        (business-level JSON: fact/dimension tables, measures, KPIs) into a complete TMDL
        (Tabular Model Definition Language) semantic model definition — the same file layout
        Power BI Desktop / Tabular Editor produces under a *.SemanticModel/definition/ folder.

        Produce ONLY a single JSON object, no prose, no markdown fences, matching exactly:
        {"files": [{"path": "<relative path>", "content": "<raw TMDL text>"}, ...], "reasoning": "<one sentence>"}

        Required files (always produce exactly these, using this relative path convention):
        - "database.tmdl" — minimal database header with a compatibility level.
        - "model.tmdl" — lists every table (ref table <Name>), every relationship-bearing model
          setting, `culture: en-US`, and `annotation`s for query-ordering; does not itself define
          columns/measures.
        - "relationships.tmdl" — one `relationship <guid>` block per Blueprint relationship, using
          `fromColumn: '<Table>'[<Column>]` / `toColumn: '<Table>'[<Column>]` syntax and the
          cardinality/crossFilteringBehavior implied by the blueprint's `cardinality`/`direction`.
        - "expressions.tmdl" — shared M query parameters only if the blueprint's data sources need
          one (e.g. a source-file-path parameter); otherwise an empty/minimal file.
        - "cultures/en-US.tmdl" — `linguisticMetadata` block giving each table/column/measure a
          human-readable display name (title-cased from its TMDL name) — required by the format,
          keep it short.
        - "tables/<Name>.tmdl" — one file per fact table, dimension table, AND the date table.
          Each has `column` blocks (name, dataType: int64/string/double/dateTime/boolean,
          sourceColumn, summarizeBy: none for non-numeric / sum or none for numeric as
          appropriate) and a single `partition <Name> = m` block with a placeholder
          `source = let Source = "" in Source` expression — this step never has real data access,
          so partitions are always placeholders; a real connection gets bound at deploy time (S8),
          not authored here.
        - "tables/_Measures.tmdl" — one file holding every measure as a `measure '<Name>' =
          <dax>` block with `formatString` set from the blueprint's `format`, and
          `displayFolder` from the blueprint's `display_folder`. Does not itself have a
          `partition` (it's a measures-only table with no rows of its own).

        Column-authoring rule for dimension tables: the blueprint only gives you name, type,
        key_columns, and hierarchies for each dimension table — never a full column list (unlike
        fact tables, which do get one). You must synthesize a complete, sensible column set:
        include every key_column, every level named in every hierarchy, and any column referenced
        by a relationship or a measure's DAX that points at this table. Use your judgement for
        data types (IDs are usually int64 or string, hierarchy levels are usually string, dates
        are dateTime) — you are not fabricating business facts here, you are inferring a schema
        shape the blueprint already implies but didn't spell out column-by-column.

        Naming: table names and file names must match exactly (e.g. blueprint fact table
        "Fact_Orders" -> "tables/Fact_Orders.tmdl", `table Fact_Orders` inside it). Never invent a
        fact or dimension table that isn't in the blueprint's data_model. Never invent a measure
        that isn't in the blueprint's measures list — author exactly the ones given, using their
        own `dax` field content directly (don't rewrite the DAX).
        """;

    private static string BuildUserPrompt(Blueprint blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Model title: {blueprint.Meta.Title}");
        sb.AppendLine($"Industry: {blueprint.Meta.Industry}");
        sb.AppendLine();

        sb.AppendLine("Fact tables:");
        foreach (var fact in blueprint.DataModel.FactTables)
        {
            sb.AppendLine($"- {fact.Name} (grain: {fact.Grain})");
            foreach (var col in fact.Columns)
                sb.AppendLine($"    column: {col.Name} ({col.Type}) — {col.Description}");
        }

        sb.AppendLine();
        sb.AppendLine("Dimension tables (no explicit column list — synthesize per the system prompt's rule):");
        foreach (var dim in blueprint.DataModel.DimensionTables)
        {
            sb.AppendLine($"- {dim.Name} (type: {dim.Type})");
            sb.AppendLine($"    key_columns: {string.Join(", ", dim.KeyColumns)}");
            sb.AppendLine($"    hierarchies: {string.Join(" | ", dim.Hierarchies)}");
        }

        sb.AppendLine();
        var dateTable = blueprint.DataModel.DateTable;
        sb.AppendLine($"Date table: {dateTable.Name} (spine: {dateTable.Spine}, fiscal_offset: {dateTable.FiscalOffset})");
        sb.AppendLine($"    key_columns: {string.Join(", ", dateTable.KeyColumns)}");

        sb.AppendLine();
        sb.AppendLine("Relationships:");
        foreach (var rel in blueprint.DataModel.Relationships)
            sb.AppendLine($"- {rel.From} -> {rel.To} (cardinality: {rel.Cardinality}, direction: {rel.Direction}, active: {rel.Active})");

        sb.AppendLine();
        sb.AppendLine("Measures:");
        foreach (var m in blueprint.Measures)
            sb.AppendLine($"- '{m.Name}' (table: {m.Table}, format: {m.Format}, display_folder: {m.DisplayFolder}) = {m.Dax}");

        return sb.ToString();
    }
}
