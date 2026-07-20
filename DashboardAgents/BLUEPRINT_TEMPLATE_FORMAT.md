# Blueprint → Template → TMDL → Report Format

## Purpose

`DashboardBlueprintSchema.md` (in this project, at
`src/DashboardAgents.BlueprintAgent/Prompts/`) is the contract for what the
Blueprint Generator produces. It says nothing about how a blueprint becomes
a Power BI template — that logic today lives in three other places that
don't talk to each other:

- **koru-main**'s `Template`/`SchemaModel` catalog (flat column lists, no
  reference to any Blueprint JSON)
- **koru-main**'s `DashboardTemplateLibrary/` (hand-authored TMDL, no
  reference to the catalog rows, no report/visuals file at all)
- **stbi-bind-deploy**, which pushes raw TMDL text to Power BI and has never
  seen a Blueprint JSON

This document is the missing link: it extends the existing Blueprint JSON
(additively — nothing here redefines a field `DashboardBlueprintSchema.md`
already owns) so the same JSON that drives the Blueprint Generator screen
can also drive template matching, TMDL generation, and Power BI report
generation for the planned **Template Creator agent**.

Scope of this document: **format and mapping rules only.** No code,
migration, or agent implementation ships with it — see "Non-goals" at the
end for what's deliberately deferred.

---

## 1. Provenance — how a blueprint says where it came from

Every blueprint the Template Creator agent touches gets one new root field.
This is what makes a blueprint traceable to (and reconcilable with) a
`Template`/`SchemaModel` row — today there is no such link anywhere in the
schema.

```json
"provenance": {
  "source": "string — user_generated | ai_proposed_from_schema | published_template",
  "origin_schema_hash": "string | null — ExtractedSchemaDto.SchemaHash that produced this blueprint, if any",
  "confidence": "number 0-100 — carried from confidence.score at proposal time",
  "review_status": "string — Approved | PendingReview | Rejected",
  "linked_template_id": "string | null — Template.Id (koru-main) once one exists",
  "linked_schema_model_id": "string | null — SchemaModel.Id (koru-main) once one exists"
}
```

`review_status` deliberately reuses `SchemaModel.ReviewStatus`'s exact
three values (`Approved`/`PendingReview`/`Rejected` —
`koru-main/.../Domain/Entities/SchemaModel.cs`) rather than inventing new
terminology. A blueprint with `source: "user_generated"` (produced by the
existing Blueprint Generator screen, no schema involved) always carries
`review_status: "Approved"` and null `origin_schema_hash`.

---

## 2. `data_model` → TMDL mapping

Deterministic, table-driven — no AI involved in this step. Verified against
the one fully-built example in the library
(`DashboardTemplateLibrary/templates/ndis/participant-service-delivery/`).

| Blueprint field | TMDL output | Rule |
|---|---|---|
| `data_model.fact_tables[].name` | `tables/<name>.tmdl`, `table <name>` | 1:1, e.g. `Fact_ServiceDelivery` → `Fact_ServiceDelivery.tmdl` |
| `data_model.fact_tables[].columns[]` | `column <name>` blocks | `type` maps to TMDL `dataType` (`INT`→`int64`, `DECIMAL`→`double`, `VARCHAR`→`string`, `DATE`→`dateTime`, `BIT`→`boolean`); every column gets `summarizeBy: none` (fact measures are additive only through explicit `measures[]`, never implicit column aggregation — matches every column in `Fact_ServiceDelivery.tmdl`) |
| `data_model.dimension_tables[].name` | `tables/<name>.tmdl` | 1:1, e.g. `Dim_Participant` |
| `data_model.dimension_tables[].type == "SCD2"` | adds `RowStartDate`/`RowEndDate`/`IsCurrent` columns + `scd_justification` becomes a TMDL `/// <summary>` comment above the table | Standard TMDL has no native SCD2 marker — the justification is preserved as a comment so a human reviewer can see *why* before publishing |
| `data_model.relationships[]` | `relationships.tmdl` | `from: "Fact_X[ColA]"` / `to: "Dim_Y[ColB]"` → `relationship Fact_X_ColA_Dim_Y \n fromColumn: Fact_X.ColA \n toColumn: Dim_Y.ColB` (exact naming convention already used in `relationships.tmdl`) |
| `data_model.date_table` | `tables/<name>.tmdl` + `cultures/<culture>.tmdl` | Follows the existing `Dim_Date.tmdl`/`cultures/en-US.tmdl` pattern; `spine` becomes the generated date range, `fiscal_offset` feeds the fiscal-year columns |
| `measures[]` | `tables/_Measures.tmdl` | Every measure folds onto the `_Measures` calculated table (`partition _Measures = calculated \n source = {BLANK()}`), **not** onto a real table — this matches both the schema doc (`measures[].table` is always `_Measures`) and `stbi-bind-deploy`'s existing behavior in `DeploymentService.DeployDatasetAsync`, which already folds `_Measures`-only tables onto the first real table before calling Push Dataset API (the API rejects columnless tables). `dax`/`format`/`display_folder` map directly to `measure <name> = <dax>`, `formatString:`, `displayFolder:` |

This mapping is what lets `stbi-bind-deploy` eventually accept a Blueprint
JSON directly instead of only a pre-built `List<TmdlFileInput>` — see
§5, Non-goals.

---

## 3. `pages[].visuals[]` → report visual spec → `.pbir`

`DashboardBlueprintSchema.md`'s current `visuals[]` shape —
`{ type, title, position, measures[], notes }` — is a design note, not
enough to generate a real Power BI report. No file anywhere in this
org's repos builds `.pbir`/report.json today; this section defines the
extension a future generator would consume.

Extended visual object (superset of the existing one — `type`, `title`,
`measures[]` keep their existing meaning and values):

```json
{
  "visual_id": "string — stable GUID, new field",
  "type": "string — same enum as today (Card | KPI | Line | Bar | Column | Matrix | Table | Map | Gauge | Waterfall | Funnel | Treemap | Scatter | Decomp Tree | Donut | Ribbon), mapped below to Power BI visualType",
  "title": "string — unchanged",
  "layout": {
    "x": "number — px, replaces the free-text position string",
    "y": "number",
    "width": "number",
    "height": "number",
    "z": "number — stacking order"
  },
  "fieldWells": {
    "category": ["string — Dimension[Column]"],
    "values": ["string — measure names, subset of visuals[].measures"],
    "legend": ["string — Dimension[Column], optional"],
    "tooltips": ["string — measure or column names, optional"],
    "rows": ["string — Matrix only"],
    "columns": ["string — Matrix only"]
  },
  "formatting": {
    "dataLabels": "boolean",
    "conditionalFormatting": [
      { "field": "string", "rule": "string — e.g. matches a kpis[].thresholds band" }
    ]
  },
  "interactions": {
    "crossFilter": "boolean",
    "drillThrough": "boolean — true if this visual is a trigger_fields source for pages[].drill_through"
  },
  "measures": ["string — unchanged, kept for backward compat with today's schema"],
  "notes": "string — unchanged"
}
```

`layout` replaces `position: "Row N Col N"` as the authoritative field once
a template moves past the design stage — `position` stays valid input from
the Blueprint Generator (a human/LLM reasons in rows and columns, not
pixels); the Template Creator agent is what resolves `position` into
`layout` coordinates using a fixed grid (12 columns, 96px row height,
matching the "Executive layout, KPI cards first, ≤5 slicers" rule already
enforced by self-review gate 5).

Type mapping (`type` → Power BI `config.singleVisual.visualType`, the PBIR
convention — `visualContainer` nodes with `x/y/width/height/z` at the top
level and a `config` JSON string carrying `singleVisual.visualType` +
`projections` built from `fieldWells`):

| Blueprint `type` | PBIR `visualType` |
|---|---|
| Card, KPI | `card` / `kpi` |
| Line | `lineChart` |
| Bar | `barChart` |
| Column | `columnChart` |
| Matrix | `matrix` (uses `rows`/`columns`/`values`) |
| Table | `tableEx` |
| Map | `map` |
| Gauge | `gauge` |
| Waterfall | `waterfallChart` |
| Funnel | `funnel` |
| Treemap | `treemap` |
| Scatter | `scatterChart` |
| Decomp Tree | `decompositionTree` |
| Donut | `donutChart` |
| Ribbon | `ribbonChart` |

`fieldWells.category`/`values`/`legend`/`tooltips` become the visual's
`projections` object; `rows`/`columns` are Matrix-only and otherwise
omitted. Slicers (`pages[].slicers[]`) are a separate PBIR `visualType:
"slicer"` container, one per slicer, laid out above the row-1 visuals per
the existing "≤5 slicers" dashboard-quality rule.

---

## 4. Template Creator agent — I/O contract

Lives in `stbi_transformers`, sibling to `BlueprintAgent`/`TweakAgent`/
`DesignMatchingService`/`SchemaModelMatchingService` (per the chosen
placement — it reuses their AI-matching conventions rather than
introducing new ones).

**Input** — reuses the existing `ExtractedSchemaDto` shape verbatim
(`koru-main/.../DTOs/ReportDesigner/SchemaDto.cs`, already produced by the
real, working `ExcelSchemaExtractor.cs`):

```
ExtractedSchemaDto(Source, FileName, Tables: List<TableSchemaDto>, SchemaHash, ExtractedAt)
TableSchemaDto(TableName, SheetName, RowCount, Columns: List<ColumnSchemaDto>)
ColumnSchemaDto(ColumnName, DataType, IsNullable, MaxLength)
```

**Matching/decision thresholds** — reuse existing conventions rather than
inventing new numbers:
- Deterministic score = `0.8 * requiredColumnRatio + 0.2 * optionalColumnRatio`
  (identical formula to `TemplateMatchingService`/`ReportMatchService`)
- AI escalation triggers below **0.6** confidence (identical to
  `ReportMatchService.AiEscalationThreshold`)
- Three outcomes, same as `ReportMatchService`'s `MatchSource` enum:
  `Deterministic` (high-confidence catalog hit), `AiMatched` (AI resolved a
  fuzzy hit), `AiProposedNew` (no acceptable match — see §6)

**Output:**

```json
{
  "blueprint": "<full Blueprint JSON, provenance.source = ai_proposed_from_schema | published_template>",
  "tmdlFiles": [{ "path": "string", "content": "string" }],
  "reportVisuals": ["<extended visual objects from §3>"],
  "confidence": "number 0-100",
  "matchSource": "string — Deterministic | AiMatched | AiProposedNew",
  "mappingPreview": {
    "mapping": [{ "templateColumn": "string", "clientColumn": "string", "transform": "string | null" }],
    "transformations": ["string"],
    "notes": "string | null"
  }
}
```

`mappingPreview` is exactly `TemplateMappingPreview`/`TemplateColumnMapping`
from `koru-main/.../DTOs/Templates/TemplateMatchDtos.cs`, reused rather
than redefined, so `.NET` callers already familiar with
`TemplateMatchResult` handle this response the same way.

---

## 5. Runtime workflow 1 — interactive report creation

1. User uploads an Excel/CSV in the .NET app → `ExcelSchemaExtractor`
   produces an `ExtractedSchemaDto` (existing, working code — no change).
2. .NET sends the `ExtractedSchemaDto` to the Template Creator agent.
3. Agent returns the §4 response.
4. .NET branches on `confidence`/`matchSource`:
   - **Exact match** (`Deterministic`, confidence at or near 100 — every
     required column present, no transform needed): mock data is swapped
     for the user's real data source binding and the dataset is deployed
     immediately via `stbi-bind-deploy`'s existing
     `POST /api/deployments/dataset` (`DeployDatasetRequest`), refreshable
     right away.
   - **Partial match**: `mappingPreview` is surfaced to the user as a
     column-remap confirmation step (which of *their* columns fills which
     *template* column) before deploy — same UX shape
     `TemplateMatchResponse` already supports today, just sourced from the
     Template Creator agent instead of `TemplateMatchingService` alone.

## 6. Runtime workflow 2 — backend reconciliation

1. The same `ExtractedSchemaDto` (its `SchemaHash` is already designed for
   exactly this — dedup) is checked against known `SchemaModel`s.
2. No good match → the schema seeds a full blueprint generation, reusing
   `BlueprintAgent`'s existing pipeline (`IPromptBuilder` →
   `IProviderRouter` → `IProvider.GenerateAsync` → `IBlueprintJsonValidator`
   → `IBlueprintMapper`) with the extracted schema in place of a
   business-requirement prompt.
3. Result passes the existing `BlueprintValidator` (unchanged — same 9
   gates, same mandatory-field table).
4. Persisted as: new `SchemaModel` + `SchemaModelField`s (from
   `TableSchemaDto.Columns`) + `Template` (`IsPublishReady: false`, since
   no `.pbix` exists yet — identical gating to every template in the
   library today) + `BlueprintVersion` (`GeneratedJsonContent` = the full
   blueprint, `provenance.source = "ai_proposed_from_schema"`,
   `provenance.review_status = "PendingReview"`).
5. An `AuditLog` row is written (`koru-main/.../Domain/Entities/AuditLog.cs`
   already has every field needed — `Action`, `EntityType`, `EntityId`,
   `OldValue`/`NewValue`; nothing currently writes to it for match events,
   this is new call sites only, not a new entity): `Action:
   "TemplateProposedFromClientSchema"`, `EntityType: "Template"`,
   `EntityId`: the new `Template.Id`.

This is the write-back path that doesn't exist anywhere today — it's what
turns isolated client uploads into a growing, shared template catalog that
both the Blueprint Generator and the Template Creator agent draw from.

---

## 7. Entity/schema changes implied (not built in this pass)

- `Template.SchemaModelId` already exists and is reused as-is.
- New: FK from `Template`/`BlueprintVersion` → each other, so a `Template`
  row can be traced back to the exact blueprint JSON that produced it (today
  `Template` and `Blueprint`/`BlueprintVersion` have no relationship at
  all).
- New: `ReportDefinitionBlobPath` on `Template`, mirroring the existing
  `BlobPath` convention, for the future `.pbir` artifact once a generator
  exists.
- New: `AuditLog.Action` constants — `TemplateProposedFromClientSchema`,
  `SchemaMatched` — no entity change, just call sites.

## 8. Non-goals for this pass

- No `.pbir`/report.json generator code — §3 is the contract a future
  generator implements against.
- No migrations, no new `stbi_transformers` project/service, no new
  `stbi-bind-deploy` endpoint accepting a Blueprint JSON directly (today it
  only accepts `List<TmdlFileInput>` via `DeployDatasetRequest`
  — `src/StbiBindDeploy.Api/Models/PushDatasetModels.cs`).
- No UI changes in `studiotechbi-ui-main`.
- No changes to `DashboardBlueprintSchema.md` itself — `provenance` and the
  extended `visuals[]` shape are additive and optional there until a
  follow-up pass formally merges them into the canonical schema doc.
