# Template Matching Format

## Purpose

**Revision note:** this document originally specified a "Template Creator
agent" that would both match *and generate* new dashboard templates
(Blueprint JSON → TMDL → Power BI report) from a client's uploaded schema.
That generation capability has since been ruled explicitly out of scope
for this platform — dashboard templates and their semantic models are
authored manually (Power BI Desktop / Tabular Editor) and registered
through the existing admin upload flow. The sections below reflect the
narrower, current scope: **AI is used only to match a client's schema
against the existing, manually-curated template catalog** — never to
create new catalog entries.

Two tracks, cleanly separated:

- **Manual template authoring** (not this platform's code): a designer
  builds the TMDL semantic model and the Power BI report/visuals for an
  industry using standard PBI tooling, then registers the finished
  template — including its required/optional column list — through
  `AdminTemplatesController`/`TemplatesPage.tsx`.
- **In-app AI matching** (the only in-scope AI work, this document's
  actual subject): a client's uploaded schema is scored against that
  catalog, and either an exact match deploys/refreshes immediately, or a
  partial match surfaces a column remap for the user to confirm before
  deploy.

`DashboardBlueprintSchema.md`'s Blueprint JSON format is unrelated to this
matching flow and is not extended by this document — the Blueprint
Generator screen and the template catalog remain two separate systems.

---

## 1. Matching contract

No new agent or service — this wires up code that already exists and
works, described here for anyone implementing the orchestration:

1. Client uploads an Excel/CSV → `ExcelSchemaExtractor.ExtractAsync`
   (`koru-main/.../Application/Utilities/ExcelSchemaExtractor.cs`) produces
   an `ExtractedSchemaDto`:
   ```
   ExtractedSchemaDto(Source, FileName, Tables: List<TableSchemaDto>, SchemaHash, ExtractedAt)
   TableSchemaDto(TableName, SheetName, RowCount, Columns: List<ColumnSchemaDto>)
   ColumnSchemaDto(ColumnName, DataType, IsNullable, MaxLength)
   ```
2. `TemplateMatchingService.MatchFromBlobAsync`/`MatchFromColumnsAsync`
   (`koru-main/.../Application/Services/TemplateMatchingService.cs`) scores
   every `Template` by `RequiredColumnsJson`/`OptionalColumnsJson` overlap
   (`0.8 * requiredRatio + 0.2 * optionalRatio`), optionally refined by AI
   below the confidence threshold, and returns:
   ```
   TemplateMatchResponse { ClientCode, BlobPath, Templates: [
     TemplateMatchResult { TemplateId, Name, Confidence, MappingPreview {
       Mapping: [{ TemplateColumn, ClientColumn, Transform }], Transformations, Notes
     } }
   ]}
   ```
   (`koru-main/.../DTOs/Templates/TemplateMatchDtos.cs` — unchanged, reused
   as-is.)
3. Caller branches on the top result's `Confidence`:
   - **High confidence** (all required columns present, no transform
     needed): call the new rebind/refresh capability (§2) immediately —
     refreshable right away, no user interaction required.
   - **Partial match**: surface `MappingPreview` to the user as a
     column-remap confirmation step before calling §2.

---

## 2. Rebind/refresh contract (new capability, `stbi-bind-deploy`)

`stbi-bind-deploy` today only supports creating a brand-new dataset from
raw TMDL text (`DeployDatasetRequest` in
`src/StbiBindDeploy.Api/Models/PushDatasetModels.cs`). It has no way to
take an *existing* template's dataset and point it at a specific client's
data, and refresh scheduling is explicitly listed as not built in its own
README. This is the one piece of new backend work this matching flow
needs:

```
RebindDatasetRequest(
  string TemplateBlobPath,       // matched Template.BlobPath
  string ClientDataLocation,     // the client's uploaded file/blob location
  List<TemplateColumnMapping>? ConfirmedMapping,  // null when the match was exact
  string ClientName)

RebindDatasetResult(
  string WorkspaceId, string DatasetId, bool RefreshTriggered, IReadOnlyList<string> Steps)
```

`ConfirmedMapping` reuses `TemplateColumnMapping` from
`TemplateMatchDtos.cs` verbatim — the same shape the matching response
already returns, just echoed back once the user confirms it.

---

## 3. Logging

`AuditLog` (`koru-main/.../Domain/Entities/AuditLog.cs`) already has every
field this needs (`Action`, `EntityType`, `EntityId`, `OldValue`/
`NewValue`) — nothing currently writes to it for match/rebind events. New
call sites only, no entity change:

- `Action: "SchemaMatched"`, `EntityType: "Template"` — on every match
  attempt, matched or not.
- `Action: "DatasetRebound"`, `EntityType: "Template"` — on a successful
  §2 rebind/refresh.

---

## 4. Non-goals

- **No AI-generated templates, ever, in this platform.** The catalog only
  grows through the manual admin upload flow. There is no "propose a new
  template from an unmatched schema" path, and none should be built.
- No Blueprint JSON ↔ TMDL conversion tooling — manual authors build TMDL
  directly in Tabular Editor/Power BI Desktop, independent of the
  Blueprint Generator screen's JSON format.
- No `.pbir`/report-visual generation code — report visuals are
  hand-built by the same manual authoring process, not derived from
  `pages[].visuals[]` (which remains a design note in the unrelated
  Blueprint Generator flow, not a generation contract).
- No new entity/migration changes — `Template.RequiredColumnsJson`/
  `OptionalColumnsJson` and `TemplateMatchDtos.cs` already carry everything
  this flow needs.
