# DashboardAgents.ReportAgent.Api

The deterministic, no-AI engine behind the portal's "Report Generator"
screen. Given a connected Excel/CSV file, it profiles columns (numeric /
date / categorical / identifier), matches a template from the local
registry by a fixed rule, and computes real KPI/chart values with pandas —
no LLM call, no network I/O, and (per platform policy) no data is ever sent
to an AI model at any point in this path.

This is a **separate deployable** from `DashboardAgents.Api` (the AI
Report Designer pipeline). `DashboardAgents.Api` keeps running unchanged on
the existing Windows App Service. This project needs a **Linux container**
instead — .NET's own Windows App Service Plan can't host the Python engine
(POSIX-only sandboxing in `python_agent/lib/security.py`, and Linux-only
process isolation) — so it ships as its own Docker image on Azure Container
Apps.

## Local run

```bash
cd DashboardAgents/src/DashboardAgents.ReportAgent.Api
python3 -m venv .venv && .venv/bin/pip install -r python_agent/requirements.txt
dotnet run
# POST http://localhost:5080/api/reports/generate  (multipart: file=<.xlsx|.csv>, templateId=<optional>)
# GET  http://localhost:5080/api/templates
```

`appsettings.Development.json` (optional, gitignored) can point
`PythonAgent:PythonExecutable` at `.venv/bin/python3` for local runs instead
of the container's `/opt/venv/bin/python3`.

## Adding a template

Drop a new `{ "sections": [...] }` JSON file in `python_agent/templates/`
and add an entry to `python_agent/templates/index.json` with an `id`,
`file`, `name`, optional `industry`, and `requires` (minimum
numeric/date/categorical column counts needed to match). No code change,
no redeploy of the matching logic — just a new file. Both the `.NET`
`GET /api/templates` endpoint and the Python engine read the same
`index.json`, so they can never drift.

## Azure deployment (one-time setup)

This runs as an **Azure Container App** — a new resource, separate from the
existing `stbitransformers` Windows Web App. `DashboardAgents.Api` and its
deployment are untouched by any of this.

```bash
# Adjust to match your subscription/existing resources.
RG=stbi-platform-rg                 # reuse your existing resource group if you have one
LOCATION=australiasoutheast         # match the other StudioTechBI services
ACR_NAME=stbireportagentacr         # must be globally unique, alphanumeric only (no dashes)
ENV_NAME=stbi-reportagent-env
APP_NAME=stbi-reportagent

az extension add --name containerapp --upgrade

# 1. Resource group (skip if reusing an existing one)
az group create --name $RG --location $LOCATION

# 2. Container registry — holds the built image
az acr create --resource-group $RG --name $ACR_NAME --sku Basic --admin-enabled true

# 3. Container Apps environment (the hosting "cluster")
az containerapp env create --name $ENV_NAME --resource-group $RG --location $LOCATION

# 4. Build & push the image directly from source (no local Docker needed —
#    ACR builds it in the cloud)
az acr build --registry $ACR_NAME --image reportagent:latest \
  DashboardAgents/src/DashboardAgents.ReportAgent.Api

# 5. Generate a strong API key koru-main will send as X-Api-Key
API_KEY=$(openssl rand -hex 32)
echo "Save this — it also goes into koru-main's config: $API_KEY"

# 6. Create the Container App
ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --query loginServer -o tsv)
ACR_USER=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASS=$(az acr credential show --name $ACR_NAME --query "passwords[0].value" -o tsv)

az containerapp create \
  --name $APP_NAME \
  --resource-group $RG \
  --environment $ENV_NAME \
  --image $ACR_LOGIN_SERVER/reportagent:latest \
  --registry-server $ACR_LOGIN_SERVER \
  --registry-username $ACR_USER \
  --registry-password $ACR_PASS \
  --target-port 5080 \
  --ingress external \
  --min-replicas 0 \
  --max-replicas 3 \
  --cpu 1.0 --memory 2.0Gi \
  --secrets reportagent-api-key=$API_KEY \
  --env-vars ReportAgent__ApiKey=secretref:reportagent-api-key

# 7. Get the public URL to configure in koru-main
az containerapp show --name $APP_NAME --resource-group $RG \
  --query properties.configuration.ingress.fqdn -o tsv
```

Notes:
- `--ingress external` gives it a public HTTPS endpoint (Azure-managed TLS),
  same security model as stbi-agenthost/stbi_transformers today: public URL
  + mandatory `X-Api-Key`. This avoids needing to VNet-integrate koru-main's
  App Service into the Container Apps environment just to reach an
  internal-only endpoint. Tightening to `--ingress internal` + VNet
  integration is a later hardening step, not required for the MVP.
- `--min-replicas 0` scales to zero when idle — you only pay while a report
  is actually being generated.
- Take the printed FQDN and the `$API_KEY` from step 5 and set them in
  koru-main's configuration (`ReportGenerator:BaseUrl` /
  `ReportGenerator:ApiKey`, added in the koru-main integration batch).

### Subsequent deploys

```bash
az acr build --registry $ACR_NAME --image reportagent:latest \
  DashboardAgents/src/DashboardAgents.ReportAgent.Api
az containerapp update --name $APP_NAME --resource-group $RG \
  --image $ACR_LOGIN_SERVER/reportagent:latest
```

A GitHub Actions workflow to automate this on push is a follow-up once the
resource exists (needs an `AZURE_CREDENTIALS` service-principal secret —
`az ad sp create-for-rbac --scopes /subscriptions/<sub-id>/resourceGroups/$RG
--role Contributor --sdk-auth`).
