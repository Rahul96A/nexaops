# NexaOps — Azure deployment

Everything here is provisioned by Bicep in `infra/bicep`. There is no portal step in the happy
path, and no secret is typed by a human anywhere in it.

---

## 1. What gets deployed

| Resource | Purpose | Notes |
|---|---|---|
| Log Analytics + Application Insights | Telemetry sink | Deployed **first**; everything else sends diagnostics here |
| Key Vault | Configuration secrets | RBAC authorization, purge protection, soft delete |
| Azure SQL Database | Everything transactional | **Entra-only authentication** |
| Storage account | Attachments | **Shared key access disabled** |
| Service Bus | Background work | **`disableLocalAuth: true`** |
| Azure Cache for Redis | Distributed cache | TLS 1.2 floor |
| Container Registry | The API image | Managed identity `AcrPull` |
| Container Apps environment + app | The API | Scales on HTTP concurrency |
| User-assigned managed identity | The application's identity | The only credential in the system |
| Azure OpenAI + AI Search | The assistant | Optional; `disableLocalAuth: true` |
| Front Door Premium + API Management | Edge | Production only; WAF in **Prevention** mode |

Region defaults to **Central India** for data residency.

### Optional components

```
deployAiServices    = true   // false where the subscription has no AI quota
deployEdgeServices  = (environment == 'prod')
```

Turning off AI is not a degraded deployment: the API reports the assistant as unconfigured and
every other feature works normally.

---

## 2. Sizing per environment

| | dev | test | stg | prod |
|---|---|---|---|---|
| SQL | GP_S_Gen5_1 serverless | GP_S_Gen5_2 serverless | GP_Gen5_2 | **BC_Gen5_4** |
| SQL max size | 32 GB | 64 GB | 128 GB | 256 GB |
| Zone redundant | no | no | yes | yes |
| Backup storage | Local | Local | Zone | **Geo** |
| Redis | Basic C0 | Basic C1 | Standard C1 | Premium P1 |
| Service Bus | Standard | Standard | Standard | Premium |
| API replicas | 0–2 | 1–3 | 1–5 | **2–20** |
| API CPU / memory | 0.5 / 1 Gi | 0.5 / 1 Gi | 1.0 / 2 Gi | 2.0 / 4 Gi |
| Log retention | 30 d | 30 d | 60 d | 365 d |

Two deliberate choices worth stating:

- **Dev scales to zero; nothing else does.** Auto-pause is a false economy above development —
  the first request after a pause pays a cold start of tens of seconds, which a service desk
  agent experiences as a broken product.
- **Production is Business Critical, not General Purpose.** Local SSD and a built-in readable
  replica are what make the RTO in [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md) achievable.

---

## 3. Identity: there is no password

The application authenticates to every Azure service with a **user-assigned managed identity**.
The Bicep grants exactly these roles, scoped to individual resources rather than the resource
group:

| Resource | Role |
|---|---|
| Key Vault | Key Vault Secrets User |
| Storage | Storage Blob Data Contributor |
| Service Bus | Azure Service Bus Data Owner |
| Container Registry | AcrPull |
| Azure OpenAI | Cognitive Services OpenAI User |
| AI Search | Search Index Data Contributor, Search Service Contributor |

SQL is the one worth dwelling on. `azureADOnlyAuthentication: true` **disables SQL authentication
outright**. There is no `sa`, no application login, no password — the connection string is:

```
Server=tcp:sql-nexaops-prod-xxxxxx.database.windows.net,1433;
Database=sqldb-nexaops;Authentication=Active Directory Default;Encrypt=True;
```

No user id, no password, nothing to rotate, nothing to leak. The consequence is that
`sqlAdminGroupObjectId` is a **required parameter**: with SQL auth disabled and no Entra
administrator set, nobody can administer the database.

The managed identity still needs database-level roles, which Bicep cannot grant because they are
data-plane objects. Run once, as a member of the SQL admin group:

```sql
CREATE USER [id-nexaops-prod-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-nexaops-prod-api];
ALTER ROLE db_datawriter ADD MEMBER [id-nexaops-prod-api];
ALTER ROLE db_ddladmin  ADD MEMBER [id-nexaops-prod-api];  -- migrations only
```

`db_ddladmin` is only needed if the migration bundle runs as the application identity. Running
migrations under a separate deployment identity and dropping `db_ddladmin` from the app is
stronger, and is the recommended posture for a customer who asks.

---

## 4. First-time setup

### Prerequisites

- Azure CLI with the Bicep extension, Contributor **and** User Access Administrator on the
  subscription (the role assignments need the latter).
- An Entra group for SQL administrators. Note its object id.
- A GitHub environment per target with an OIDC federated credential.

### Provision

```bash
az group create --name rg-nexaops-prod --location centralindia
```

Set `sqlAdminGroupObjectId` in `infra/bicep/parameters/prod.bicepparam`, then preview before you
commit:

```bash
az deployment group what-if --resource-group rg-nexaops-prod --parameters infra/bicep/parameters/prod.bicepparam
```

```bash
az deployment group create --resource-group rg-nexaops-prod --parameters infra/bicep/parameters/prod.bicepparam
```

### The one secret

The JWT signing key. Generate it *in* Key Vault so no human sees it and it never touches a
terminal history:

```bash
az keyvault secret set --vault-name kv-nexaopsprodxxxxxx --name Auth--SigningKey --value "$(openssl rand -base64 48)"
```

The API layers Key Vault over configuration at startup and maps `--` to the configuration
separator, so this surfaces as `Auth:SigningKey`. It refuses to start without it.

If `Auth:Mode` is `EntraId`, no signing key is needed at all — Entra issues the tokens.

### The step Bicep cannot do

**The managed identity needs a database user, and no ARM template can create one.** Granting the
identity a role on the SQL *server* is an ARM operation; creating a principal *inside the
database* is a data-plane operation that only a SQL connection can perform.

Skip it and the deployment appears to succeed. The container starts, listens, and then fails
readiness for what looks like a database outage. The actual error is in the container logs:

```
Microsoft.Data.SqlClient.SqlException: Login failed for user '<token-identified principal>'.
```

Run this once per environment, connected as a member of the SQL administrator group:

```sql
CREATE USER [id-nexaops-dev-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-nexaops-dev-api];
ALTER ROLE db_datawriter ADD MEMBER [id-nexaops-dev-api];
```

The user name is the managed identity's resource name. Reader and writer only: the application
never changes the schema, because migrations run as their own pipeline step.

`sqlcmd` 15 cannot pass an Entra access token, so connect with a client that can — PowerShell
setting `SqlConnection.AccessToken` from `az account get-access-token --resource https://database.windows.net/`
is the shortest route on a workstation.

---

## 5. The pipeline

`.github/workflows/deploy.yml`. Authentication is **OIDC federated** — no service principal
secret is stored in GitHub.

```
build ──► staging ──► production (protected environment, manual approval)
```

Each deployment stage runs, in order:

1. **`what-if`** against the target resource group, and prints the diff into the job log.
2. **`az deployment group create`** — infrastructure first, so a new setting exists before the
   code that reads it.
3. **Apply migrations** using the self-contained migration bundle built in the `build` job. The
   application never migrates as a side effect of starting: `Database:MigrateOnStartup` is
   `false` in every deployed environment.
4. **Release the new image** onto the Container App revision.
5. **Smoke test** — including an assertion that `GET /api/v1/incidents` returns **401** without a
   token. A deployment that accidentally opens the API fails the pipeline rather than serving
   traffic.
6. **Roll back on failure** — traffic returns to the previous revision.

Migrations run before the new image is released, which means **every migration must be backward
compatible with the currently running revision**. Additive changes are safe; a column rename is
two deployments (add, backfill and dual-write, then remove).

---

## 6. Application settings

Non-secret settings are plain environment variables on the Container App; the two that carry
credentials are Container Apps secrets. Notable values:

| Setting | Production value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AZURE_CLIENT_ID` | The user-assigned identity's client id |
| `Auth__Mode` | `Local` — switch to `EntraId` for a federated customer |
| `Database__MigrateOnStartup` | `false` |
| `Demo__SeedOnStartup` | `false` |
| `AzureAi__UseManagedIdentity` | `true` |
| `Storage__BlobServiceUri`, `Messaging__FullyQualifiedNamespace` | Endpoints only — no keys |

Every adapter degrades honestly: an unset `Storage__BlobServiceUri` means attachments fall back
to the local filesystem and say so, rather than failing at upload time.

---

## 7. Health and scaling

Three probes, all distinct:

| Probe | Path | Means |
|---|---|---|
| Liveness | `/health/live` | The process is running. Failing restarts the container |
| Readiness | `/health/ready` | Dependencies answer. Failing removes it from rotation |
| Startup | `/health/live` | Grace period before liveness applies |

A readiness check that also gated liveness would restart a healthy container every time SQL had a
transient hiccup, turning a brief dependency blip into an outage. They are separated on purpose.

Scaling is on **HTTP concurrency, 50 concurrent requests per replica**, not CPU. This is a
request/response workload where latency degrades long before CPU saturates; CPU-based scaling
reacts after users have already noticed.

---

## 8. The edge (production only)

**Front Door Premium** terminates TLS, applies the managed WAF rule sets in **Prevention** mode
(not Detection — a rule that only logs is a rule that does not protect), enforces its own rate
limit ahead of the application's, and caches only the static front-end assets. API responses
carry `Cache-Control: no-store` and are never cached.

**API Management** sits in front for a customer needing subscription keys, per-consumer quotas or
a developer portal. It is optional; the Container App is directly reachable through Front Door
without it.

`customDomain` is empty by default. Set it once the DNS record and managed certificate exist.

---

## 9. Front end

The React app builds to static files uploaded as a pipeline artefact. This build serves them from
the same origin as the API, so there is no cross-origin configuration to get wrong.

Serving them from Front Door / Static Web Apps instead requires setting `Cors:AllowedOrigins`
explicitly. There is deliberately **no wildcard fallback** — an unconfigured CORS policy blocks
the browser rather than opening the API to every origin.

---

## 10. Cost

Rough monthly order of magnitude in Central India, excluding AI token consumption and egress.
Verify against the Azure pricing calculator before quoting anyone.

| Environment | Approximate |
|---|---|
| dev | Low — SQL serverless auto-pauses, API scales to zero |
| test | Modest |
| stg | Moderate — zone redundancy is the step up |
| prod | Dominated by Business Critical SQL, then Front Door Premium, then Redis Premium |

The largest single lever is the SQL tier. Business Critical is chosen for the RTO, not for
throughput; a customer accepting a longer recovery time can drop to General Purpose and save
most of the bill.

---

## 11. Not built

- **Private endpoints.** SQL, Storage and Service Bus are reachable over the public network,
  restricted by firewall rules and Entra authentication. A customer requiring no public network
  path needs private endpoints and a VNet-integrated Container Apps environment.
- **Multi-region.** Single region with geo-redundant backups. Active-active is discussed in
  [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md) and not implemented.
- **Customer-managed keys.** Encryption at rest uses Microsoft-managed keys.
- **Azure Policy / landing-zone guardrails.** The Bicep configures its own resources correctly
  but does not enforce anything at subscription scope.
- **Defender for Storage.** Required before accepting attachments from untrusted users; see
  [SECURITY.md §8](SECURITY.md).
