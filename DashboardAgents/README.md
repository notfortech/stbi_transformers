# DashboardAgents — .NET API

Three agents behind one ASP.NET Core Web API, wired together:

```
Live DB ──► SchemaConnector ──► BlueprintAgent ──► Blueprint (JSON)
                                                          │
                                                          ▼
                                        Scenario ──► TweakAgent ──► adapted page(s)
```

| Project | Responsibility |
|---|---|
| `DashboardAgents.Core` | Domain models — mirrors `DashboardBlueprintSchema.md` field-for-field |
| `DashboardAgents.SchemaConnector` | Read-only live DB introspection (SQL Server, PostgreSQL) → schema text |
| `DashboardAgents.Llm` | Thin Anthropic Messages API client, shared by both agents |
| `DashboardAgents.BlueprintAgent` | Ports the original tool's 8-step workflow: prompts embedded verbatim from `prompts/*.md`, calls the LLM, parses + validates the JSON blueprint |
| `DashboardAgents.TweakAgent` | New — adapts an existing blueprint to a use-case scenario, constrained to a field allow-list built from that blueprint |
| `DashboardAgents.Api` | Controllers + DI wiring exposing all of the above over HTTP |

## Why this shape

- **The DAX/semantic model engine is untouched.** Per your call to keep Power BI as the output,
  `BlueprintAgent` is a faithful port of the existing prompt files (`AgentInstructions.md`,
  `IndustryDetectionRules.md`, `BusinessCapabilityMappings.md`, `DashboardDesignRules.md`,
  `DashboardBlueprintSchema.md`) — copied byte-for-byte into `Prompts/` and embedded as resources.
  Nothing about industry detection, KPI design, or DAX rules changed; only the transport
  (JS in a browser → C# behind an API) did.
- **The schema connector is a drop-in for the existing textarea**, not a new input type. It
  introspects a live database and formats the result into the exact same "DATASET SCHEMA /
  HEADERS" text block a human used to paste — so `SystemPromptBuilder`/`UserPromptBuilder` need
  no awareness of where the schema came from.
- **The tweak agent never invents fields.** `FieldAllowlistBuilder` extracts every valid
  measure/KPI/table/column name from the blueprint and passes it to the LLM as an explicit
  allow-list; `TweakOutputValidator` then rejects (throws `TweakValidationException`, surfaced
  as HTTP 422) any response that references something outside that list. This is the guardrail
  discussed earlier — enforced in code, not just requested in the prompt.
- **Validation is server-side, not just prompted.** `BlueprintValidator` re-checks the mandatory
  fields table from `DashboardBlueprintSchema.md` (minimum fact/dimension tables, KPI
  completeness, all 9 self-review gates, etc.) and fails closed (HTTP 422) rather than silently
  returning a blueprint that violates its own schema contract.

## Setup

Requires the .NET 8 SDK:

```bash
cd DashboardAgents
dotnet restore
dotnet build
```

### Configure the OpenAI API key (local dev)

Never commit a real key. Use user-secrets for local development:

```bash
cd src/DashboardAgents.Api
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
```

Or set the `OPENAI_API_KEY` environment variable. To use Anthropic instead, also set `LLM_PROVIDER=anthropic` and `ANTHROPIC_API_KEY`.

---

## Azure Deployment

The API is deployed to **Azure App Service (Linux, .NET 8)** via GitHub Actions.

### Azure Web App settings

In the Azure Portal, go to **App Service → Configuration → General settings** and set:
- **Stack**: `.NET`
- **.NET version**: `.NET 8 (LTS)`

### GitHub Actions — required secrets

Add these in your GitHub repo under **Settings → Secrets and variables → Actions**:

| Secret | Where to get it |
|---|---|
| `AZUREAPPSERVICE_CLIENTID` | Azure AD App Registration → Application (client) ID |
| `AZUREAPPSERVICE_TENANTID` | Azure AD → Directory (tenant) ID |
| `AZUREAPPSERVICE_SUBSCRIPTIONID` | Azure Portal → Subscriptions |

> The workflow uses OIDC federated identity — no client secret needed. Set up a Federated Credential on the App Registration with subject `repo:notfortech/stbi_transformers:ref:refs/heads/main`.

Update the `app-name` in `.github/workflows/deploy.yml` to match your Azure Web App name.

### Azure App Service — Application Settings (environment variables)

| Setting Name | Required | Description |
|---|---|---|
| `OPENAI_API_KEY` | **Yes** | OpenAI API key |
| `JWT_KEY` | Yes (if auth on) | Must match koru-main's JWT signing key |
| `JWT_ISSUER` | Recommended | JWT issuer claim |
| `JWT_AUDIENCE` | Recommended | JWT audience claim |
| `KORU_BASE_URL` | Recommended | koru-main API base URL |
| `KORU_API_KEY` | Recommended | Service-to-service API key for koru-main |
| `REDIS_CONNECTION` | Recommended | Azure Cache for Redis connection string |
| `CORS_ORIGINS` | **Yes** | Frontend URL, e.g. `https://app.studiotechbi.com` |
| `KEY_VAULT_URL` | Optional | Azure Key Vault URL — pulls secrets at startup via Managed Identity |
| `ANTHROPIC_API_KEY` | Optional | Only needed if switching provider to Anthropic |
| `LLM_PROVIDER` | Optional | `openai` (default) or `anthropic` |

### Redis (session durability)

Without Redis, pipeline sessions and blueprints are held in memory and are lost on restart or across multiple instances. Provision an **Azure Cache for Redis** (Basic C0 is sufficient) and set the primary connection string as `REDIS_CONNECTION`.

### Azure Key Vault (optional)

1. Create a Key Vault and enable **System-assigned Managed Identity** on the App Service.
2. Grant the identity `Key Vault Secrets User` role on the vault.
3. Add secrets using the config key name with `:` replaced by `--` (e.g. `OpenAI--ApiKey`).
4. Set `KEY_VAULT_URL=https://your-vault.vault.azure.net/` in App Service Application Settings.

The app loads Key Vault secrets at startup and they override `appsettings.json` values.

### Run

```bash
cd src/DashboardAgents.Api
dotnet run
```

Swagger UI is available at `/swagger` in the Development environment.

## API Reference

### 1. Introspect a live schema (no generation)

```
POST /api/schema/introspect
{
  "provider": "SqlServer",
  "connectionString": "Server=...;Database=...;User Id=...;Password=...;",
  "schemaFilter": ["dbo"]
}
```

Returns the structural snapshot plus the formatted schema text the blueprint agent would use.
Useful for letting the portal show the user what was detected before committing to generation.

### 2. Generate a blueprint from requirements or pasted schema

```
POST /api/blueprint/generate
{
  "options": {
    "mode": "schema",
    "schemaText": "Table: invoices ...",
    "audience": "CFO",
    "currency": "AUD",
    "fiscalYearStart": "July",
    "rlsRequired": true,
    "refreshCadence": "Daily"
  }
}
```

### 3. Connect + generate in one call (the full pipeline)

```
POST /api/blueprint/from-connection
{
  "provider": "PostgreSql",
  "connectionString": "Host=...;Database=...;Username=...;Password=...",
  "schemaFilter": ["public"],
  "options": { "mode": "schema", "audience": "COO", "currency": "AUD" }
}
```

Both generation endpoints return a `Blueprint` JSON object (identical shape to
`DashboardBlueprintSchema.md`) with a server-assigned `blueprintId` for use in step 4.

### 4. Adapt a blueprint to a use-case scenario

```
POST /api/blueprint/{blueprintId}/adapt
{
  "scenario": "Show churn risk for enterprise customers over the last quarter",
  "persistToBlueprint": true
}
```

Returns:

```json
{
  "mode": "matched_existing | composed_new",
  "pages": [ /* one or more page objects, same shape as blueprint.pages[] */ ],
  "explanation": "...",
  "fieldsUsed": ["Measure or field names referenced"]
}
```

If `persistToBlueprint` is true and the agent composed a new page, it's appended to the stored
blueprint so a subsequent `GET /api/blueprint/{blueprintId}` includes it.

## Extending the schema connector

`IDbSchemaReader` is the extension point — add a `MySqlSchemaReader` or `SnowflakeSchemaReader`
the same way `SqlServerSchemaReader`/`PostgresSchemaReader` are built, register it in
`Program.cs`, and it's automatically resolvable via `ISchemaReaderFactory`.

## Extending persistence

`IBlueprintStore` currently ships as an in-memory singleton (`InMemoryBlueprintStore`) —
sufficient for a demo or single-instance deployment, but blueprints won't survive a restart and
won't be visible across multiple API instances behind a load balancer. Swap in a real
implementation (blueprints are plain JSON, so a `jsonb` column in Postgres or a document store
like Cosmos DB both work with minimal code) before production use.

## Security notes

- The schema connector only ever runs metadata queries (`INFORMATION_SCHEMA`, `sys.*`,
  `pg_catalog`) plus bounded `COUNT(DISTINCT ...)`/sampling on columns already identified as
  low-cardinality (≤20 distinct values) — it never runs `SELECT *` or reads arbitrary row data.
- Connection strings should use a read-only, schema-scoped database role — the connector doesn't
  need write access or access to tables outside the schemas you intend to model.
- The Anthropic API key must never be logged, returned in a response, or committed to source
  control — `AnthropicClient` logs only a warning if it's missing, never the key value.
