# NexaOps — Phase 1 Final Report

Phase 1 (platform foundation) + Incident Management + Demo Environment.
7 September 2026.

---

## 1. Architecture

**Modular monolith**, four projects, with a strictly one-way dependency graph:

```
NexaOps.Api ──► NexaOps.Application ──► NexaOps.Domain
                        ▲
NexaOps.Infrastructure ─┘
```

`NexaOps.Domain` has **zero package references** — enforced by its csproj, not by convention. It
contains entities, invariants, the incident state machine and business-hours SLA arithmetic, and
knows nothing about EF Core, ASP.NET or Azure. The consequence is that the rules most expensive to
get wrong are testable as pure functions in under a second, and 113 tests do exactly that.

`Application` defines use cases and **ports** (`IIncidentRepository`, `IAiCompletionService`,
`IEmailSender`, `IBlobStore`, `ICache`). `Infrastructure` implements them. `Api` composes.

**Why a monolith and not microservices.** ITSM is one transactional consistency boundary — an
incident, its SLA clocks, its audit rows and its notifications must commit together or not at
all. Splitting that across services buys distributed-transaction problems and pays nothing back
at this scale. The module boundaries are drawn now so a split is possible later; the split is not
made now because nothing requires it.

Cross-cutting concerns are implemented once, as infrastructure, so no feature can forget them:
tenant filtering (an EF convention), audit and provenance (a `SaveChangesInterceptor`), error
translation (middleware), correlation ids (middleware), and authorization (a policy provider that
generates a policy per permission on demand).

Full detail: [ARCHITECTURE.md](ARCHITECTURE.md).

---

## 2. Repository structure

```
NexaOps.slnx                 .NET 10 XML solution format (there is no .sln)
global.json                  Pins SDK 10.0.301
Directory.Build.props        net10.0, nullable, NuGetAudit=all at moderate
Directory.Packages.props     Central Package Management

src/
  NexaOps.Domain/            Entities, invariants, SLA arithmetic. No packages.
  NexaOps.Application/       Use cases, DTOs, validators, ports, AI tool registry
  NexaOps.Infrastructure/    EF Core, identity, Azure adapters, migrations
  NexaOps.Api/               Controllers, auth wiring, middleware, workers, seeding
  NexaOps.Web/               React 19 + TypeScript + Vite + MUI

tests/
  NexaOps.Domain.Tests/          113 tests
  NexaOps.Application.Tests/      31 tests
  NexaOps.Api.IntegrationTests/   76 tests — real HTTP, real SQL Server

infra/
  bicep/                     main.bicep + 10 modules + 4 parameter files
  scripts/reseed-demo.ps1    Drops, rebuilds, reseeds, prints the queue shape

.github/workflows/
  ci.yml                     4 parallel jobs
  deploy.yml                 OIDC, migration bundles, staged rollout, rollback

docs/                        12 documents
```

---

## 3. Database schema

Azure SQL. One database, shared schema, tenant discriminator. **30 tables across 5 schemas.**

| Schema | Tables |
|---|---|
| `identity` | Tenants, Organizations, Departments, Users, Roles, RolePermissions, UserRoles, Groups, GroupMembers, RefreshTokens |
| `servicedesk` | Incidents, IncidentComments, IncidentTags, Categories, Subcategories, PriorityMatrix, RecordRelations |
| `sla` | BusinessCalendars, BusinessCalendarWindows, BusinessCalendarHolidays, SlaDefinitions, SlaPolicies, SlaInstances |
| `audit` | AuditEvents |
| `platform` | Attachments, Notifications, NumberSequences, SystemSettings |

**Conventions applied by convention, not per entity:** UUID v7 keys (time-ordered, so the
clustered index does not fragment the way random v4 keys do on SQL Server); `nvarchar(512)`
default (an unbounded column cannot be indexed); `decimal(18,4)`; `DeleteBehavior.Restrict`
everywhere (a stray cascade across a tenant boundary would be catastrophic and silent);
`rowversion` optimistic concurrency; UTC `datetimeoffset`; archive rather than delete.

`Incidents` carries nine indexes, **every one leading with `TenantId`**, because every query is
tenant-filtered and an index that did not lead with it would be unusable. `NextSlaDueAt` and
`HasBreachedSla` are denormalised onto the incident by the SLA service — without them the queue
page would join and aggregate SLA clocks on every row of every page.

One migration, `InitialSchema`. Full detail: [DATABASE.md](DATABASE.md).

---

## 4. Azure resources

| Resource | Notes |
|---|---|
| Log Analytics + Application Insights | Deployed first; everything else sends diagnostics here |
| Key Vault | RBAC authorization, purge protection, soft delete |
| Azure SQL | **`azureADOnlyAuthentication: true`** — SQL auth disabled outright |
| Storage | **`allowSharedKeyAccess: false`** |
| Service Bus | **`disableLocalAuth: true`** |
| Redis | TLS 1.2 floor |
| Container Registry | Managed identity `AcrPull` |
| Container Apps env + app | Scales on HTTP concurrency, 50/replica |
| User-assigned managed identity | The only credential in the system |
| Azure OpenAI + AI Search | Optional; **`disableLocalAuth: true`** |
| Front Door Premium + APIM | Production only; WAF in **Prevention** mode |

Region defaults to **Central India**. Four sized environments; production is Business Critical
SQL with geo-redundant backups, zone redundancy and 2–20 replicas. Dev scales to zero; nothing
else does, because a cold start of tens of seconds is experienced by an agent as a broken
product.

`az bicep build` produces **0 warnings**.

---

## 5. Azure deployment instructions

```bash
az group create --name rg-nexaops-prod --location centralindia
```

Set `sqlAdminGroupObjectId` in `infra/bicep/parameters/prod.bicepparam` — it is **required**,
because with SQL authentication disabled and no Entra administrator, nobody can administer the
database.

```bash
az deployment group what-if --resource-group rg-nexaops-prod --parameters infra/bicep/parameters/prod.bicepparam
```

```bash
az deployment group create --resource-group rg-nexaops-prod --parameters infra/bicep/parameters/prod.bicepparam
```

Then two manual steps that Bicep cannot do:

**The one secret** — generate it *in* Key Vault so no human ever sees it:

```bash
az keyvault secret set --vault-name kv-nexaopsprodxxxxxx --name Auth--SigningKey --value "$(openssl rand -base64 48)"
```

**Database roles for the managed identity** (data-plane objects, run as a SQL admin):

```sql
CREATE USER [id-nexaops-prod-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-nexaops-prod-api];
ALTER ROLE db_datawriter ADD MEMBER [id-nexaops-prod-api];
```

The pipeline then handles everything: `what-if` → infrastructure → migration bundle → image
release → smoke test (which asserts a 401 on unauthenticated `/api/v1/incidents`, so a deployment
that accidentally opens the API fails rather than serving traffic) → rollback on failure.

**Migrations run before the new image is released**, so every migration must be backward
compatible with the currently running revision. Full detail:
[AZURE-DEPLOYMENT.md](AZURE-DEPLOYMENT.md).

---

## 6. Authentication

Two modes selected by `Auth:Mode`, converging on an identical `ClaimsPrincipal` so every
authorization decision is written once.

**Local JWT — fully implemented.** PBKDF2/HMAC-SHA512 at 210,000 iterations; 12-character
minimum; 5-attempt lockout for 15 minutes; 30-minute HS256 access tokens; refresh tokens of 256
random bits stored **only as a SHA-256 hash**, single-use and rotated, with presentation of an
already-rotated token revoking the **entire session chain**.

**The signing key is never defaulted.** The API refuses to start without one, because a defaulted
key would let anyone who reads the repository mint a valid token for any tenant.

**Uniform failure.** Unknown email, wrong password, disabled, locked, federated-only — all return
the same 401 with the same message. The real reason is recorded server-side and surfaces as a
low-cardinality metric tag, so operators can distinguish "forgot password" from "credential
spraying" while an attacker learns nothing.

**The security stamp** is rotated on any credential or role change, embedded in each token and
validated per request against a 60-second cache. Without it, a token minted before a revocation
would keep working for its full 30-minute lifetime.

**Entra ID** is wired and validates against Entra's own metadata; MFA and Conditional Access are
Entra's job and deliberately not re-implemented. The first-sign-in user provisioning path is
**not built** — see §21.

---

## 7. Multi-tenancy

Shared database, shared schema, tenant discriminator. The honest cost of that choice: the
isolation boundary becomes something the software must get right on every query and every write.
That is why it is enforced three times independently.

**The tenant comes exclusively from the `nexaops:tid` claim on a signature-validated token.** A
`tenantId` in a body, query string, route or header is **ignored entirely** — there is no code
path that reads one. An authenticated token with no usable tenant claim is rejected with 401
rather than passing through unscoped.

| Layer | What it does |
|---|---|
| **Query filters** | Applied to every `ITenantOwned` entity by reflection in `OnModelCreating`. A developer adding an entity gets isolation for free and cannot forget it. Unscoped, `CurrentTenantId` is `Guid.Empty`, matching no row |
| **Write guard** | The interceptor refuses a foreign insert, an update to a row loaded unscoped, and any attempt to **move** a row between tenants — checked against the entry's *original* `TenantId`, since comparing only the current value would let a rewrite slip through |
| **Explicit service checks** | Cross-aggregate references (group, category, assignee) are verified through a tenant-filtered query, so a foreign reference reads as non-existent |

Three deliberate escapes — sign-in, provisioning/seeding, and the SLA monitor — each open a
**disposable** `SuppressTenantFilter()` scope. That design came from a real bug: it used to be a
plain flag, and a nested call's `finally` un-suppressed an outer scope.

**Cross-tenant records return 404, never 403.** A 403 confirms the id is real, which enables
enumeration.

18 integration tests, run as a fully authenticated *privileged* user of a neighbouring tenant
that has real data. Full detail: [MULTI-TENANCY.md](MULTI-TENANCY.md).

---

## 8. RBAC

**Permission-based, never role-based, at the point of enforcement.** No code path anywhere
branches on a role name. ~50 permission codes, 12 seeded system roles; a custom role is a
configuration change, not a code change.

**Enforced twice** — at the endpoint via `[RequiresPermission(...)]`, and again inside the
application service. The second check is what makes "the AI cannot bypass authorization" a
structural property rather than a promise: the service is also reachable from the AI tool
executor and, later, the workflow engine.

**Closed by default.** A fallback policy requires authentication, so a newly added controller
cannot be accidentally public; opting out needs an explicit `[AllowAnonymous]`.

**Separation of duties, tested.** `role.manage`, `user.reset_password` and `audit.read` are held
only by administrators; `incident.priority.override` and `incident.declare_major` by managers,
not agents. 14 tests assert this, including that no role references a permission outside
`Permissions.Catalogue` — a typo'd code grants nothing silently, so this makes it a build failure.

**Within a tenant, visibility is a permission question.** `incident.read.all` sees everything;
`incident.read` alone sees only what the user raised, is affected by, is assigned, or sits in
their group — applied as a **database predicate**, so counts, paging and aggregates are all
correct.

The UI hides what a user cannot do. That is presentation only; forcing a hidden button into view
gets a 403.

---

## 9. Audit

A `SaveChangesInterceptor` captures before/after snapshots of every tracked change, plus an
explicit `IAuditService` for events that are not entity mutations: sign-in, sign-in failure,
permission denial, AI tool execution, tenant isolation violation.

Each row carries tenant, actor id/name/email, UTC timestamp, action, entity type and id, label,
JSON before and after, changed field list, source (`Api`/`Ai`/`Workflow`/`System`/`Integration`),
outcome, correlation id, IP and user agent.

**Append-only by construction.** No update or delete path is exposed anywhere — not in the API,
not in a service, not in a repository. It is append-only because there is no code that could
modify it, not because of a policy.

`PasswordHash`, `SecurityStamp`, `TokenHash` and `ReplacedByTokenHash` are **redacted** in every
snapshot; without that, a read-only audit permission would become a credential disclosure.

Security events are written **immediately, in their own transaction**, because they must survive
the failed operation that triggered them. Ordinary changes are audited in the caller's unit of
work, so the audit row and the change commit together.

Bulk provisioning and seeding suppress capture — nobody performed those writes, and the volume
caused real command timeouts.

---

## 10. Incident functionality

Complete lifecycle, governed by `IncidentStateMachine` as the single source of truth for legal
transitions. `Closed` and `Cancelled` are terminal with empty transition sets.

```
New ──► Assigned ──► In progress ──► Resolved ──► Closed
             │            ▲   │
             └──► Pending ┘   └──► Cancelled
```

- **Priority** derived from a per-tenant impact × urgency matrix. A manager override requires a
  **mandatory reason** — an override changes what the customer is owed, so it is never anonymous.
- **Assignment** to a user and/or group, with cross-tenant references rejected as 404.
- **First response** recorded idempotently — recording it twice does not restart or double-count
  the clock.
- **Comments** split into public replies and internal work notes. Work notes are filtered **at the
  query level** for callers without `incident.worknote.read`, and the comment count is filtered
  with them, because a count including hidden notes leaks their existence.
- **Major incident** declaration, permission-gated.
- **Optimistic concurrency**: a stale edit returns 409 `concurrency.conflict`, never a silent
  overwrite.
- **Number sequences**: `INC0000042`, per tenant, gapless, allocated under an update lock inside
  the caller's transaction so a rolled-back creation does not consume a number.
- **Activity timeline** and a per-record audit view.
- Search, filtering, sorting (allow-listed fields; an unrecognised field is a 400 with field
  errors, never interpolated into SQL) and paging.
- Notifications on assignment, comment, status change and SLA warning/breach.

23 integration tests.

---

## 11. SLA implementation

`BusinessSchedule` is a pure domain value object doing all business-hours arithmetic in memory —
no SLA calculation happens in SQL.

- Working windows per day of week (seeded default Mon–Fri 09:00–18:00 IST), holidays, and a time
  zone.
- `ResolveTimeZone` accepts Windows or IANA identifiers, falling back to IST rather than throwing.
- Malformed windows (end before start) are skipped rather than producing a negative duration.
- A `MaxDaysToWalk = 3660` guard means a pathological customer calendar terminates rather than
  hanging.

**A P2 raised at 17:30 on Friday with a 4-hour target is due at 12:30 on Monday.** 18 tests assert
this class of behaviour to the minute.

`SlaDefinition` → `SlaPolicy` (ordered, module-scoped) → `SlaInstance` (the live clock). The clock
**copies** `DurationMinutes`, `WarningThresholdPercent`, `PauseWhenPending` and
`BusinessCalendarId` at attach time rather than reaching through a navigation — so a commitment
made under yesterday's policy does not silently change when the policy is edited.

Pause on `Pending` with **paused minutes credited back** on resume. Re-targeting on priority
change applies to `InProgress`, `Paused` **and** `Breached` clocks. A breach is dated to the
deadline, not to when the monitor noticed it. Only `InProgress` clocks can breach.

A background `SlaMonitorWorker` crosses tenants deliberately, stamping everything it writes with
the tenant of the record it derives from.

---

## 12. React pages

React 19 / TypeScript 5.9 / Vite 7 / MUI 7 / TanStack Query 5 / React Hook Form 7 / Zod 4.

| Page | Route |
|---|---|
| Sign in | `/sign-in` |
| Service desk dashboard | `/` |
| My work | `/my-work` |
| Incident queue | `/incidents` |
| New incident | `/incidents/new` |
| Incident record (activity + audit tabs) | `/incidents/:id` |
| Audit trail | `/audit` |
| Change password | `/change-password` |
| Not found | `/404` |

Plus `AppShell` (navigation, with unbuilt modules marked **"Later"** and not clickable),
`NotificationBell`, `GlobalSearch`, `AiAssistantButton`, and shared `PageHeader` / `EmptyState` /
`ErrorState` / `StatCard` / `StatusChips`.

**Queue filter state lives in the URL**, so a view is shareable and the back button works.

`ApiError` exposes a stable machine-readable `code`; screens branch on it, never on prose.
`ai_not_configured` is distinguished specifically from a configured-but-failing provider, because
they need different UI. Token refresh is **single-flight** — concurrent 401s would otherwise each
rotate the refresh token and trip the theft detector.

Build: 4 chunks, **~264 KB gzipped**. Light and dark themes; theming and formatting configured
per tenant (IST, `en-IN`, lakh/crore grouping).

---

## 13. APIs

REST, versioned at `/api/v1`, RFC 9457 Problem Details, OpenAPI + Scalar at `/docs`
(Development only). **32 endpoints.**

| Group | Endpoints |
|---|---|
| **Auth** (5) | `POST sign-in`, `POST refresh`, `POST sign-out`, `GET me`, `POST change-password` |
| **Incidents** (12) | `GET` (search), `POST`, `GET summary`, `GET {id}`, `GET by-number/{n}`, `PATCH {id}`, `POST {id}/assign`, `POST {id}/status`, `POST {id}/priority`, `POST {id}/major`, `GET/POST {id}/comments`, `GET {id}/activity`, `DELETE {id}` (archive) |
| **Reference** (7) | categories, groups, group members, users, priority matrix, India states, my groups |
| **Notifications** (4) | list, unread count, mark read, mark all read |
| **Audit** (2) | search, by entity |
| **AI** (2) | `GET status`, `POST ask` |
| **Health** (2) | `/health/live`, `/health/ready` — anonymous, status names only |

Every response carries security headers (`nosniff`, `DENY`, a CSP of `default-src 'none'`,
`no-store`) and a correlation id. Rate limiting is partitioned per authenticated user and per
source IP for anonymous traffic, so one noisy tenant cannot exhaust another's budget. CORS has
**no wildcard fallback**.

---

## 14. AI architecture

**The browser never holds an AI key, endpoint, or model name, and cannot supply a system message**
— replayed history is filtered to `user` and `assistant` turns only.

**The tool registry is the security boundary.** `IAiTool` is the only route from a model to this
system; there is no path by which a completion can reach the database, compose SQL, or execute a
shell command. Each tool declares a name, a description, a `RequiredPermission`, an `IsMutating`
flag and a JSON Schema.

Three tools in this build, **all read-only**, all requiring `incident.read`: `search_incidents`,
`get_incident`, `get_service_desk_summary`. They call `IncidentQueryService`, so they inherit
tenant filtering, visibility scoping and work-note filtering without restating any of it — a
requester asking "what is open?" gets their own tickets.

The executor's four gates, **in this order**: registered? → permission? → mutating? → valid JSON?
Permission is checked **before arguments are even parsed**, asserted by a test named for it.

**Tools the caller cannot use are never described to the model.** That matters more than the
permission check: a model that has never been told a capability exists cannot be persuaded to
call it, name it, or hint at it. The permission check is the backstop.

**Grounding.** The system prompt forbids guessing an incident number, name, date, count or status,
and every answer returns `toolsUsed`, which the UI displays. Tool-calling is capped at 5 rounds.

**Prompt injection.** The system prompt tells the model that tool-result content is data written
by users, never instructions — that is the *mitigation*. The *boundary* is the registry: a
successful injection can at most produce a misleading sentence; it cannot call an unpermitted
tool, write data, or read another tenant.

**Unconfigured is honest.** No provider → `GET /ai/status` says so, `POST /ai/ask` returns 503
`ai_not_configured`, and the UI shows an explanatory panel. There is no canned answer and no demo
mode. In Azure, `UseManagedIdentity` defaults true and the resource has `disableLocalAuth: true`.

Every interaction is audited with `AuditSource.Ai`; unknown-tool requests and permission denials
are audited **immediately**. Full detail: [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md).

---

## 15. Demo users

Tenant **Acme Technologies India** (`acme-in`). Password for all: `NexaOps#Demo2026`

| Account | Role | Can |
|---|---|---|
| `priya.raghavan@acmetech.example.in` | Tenant Admin + IT Manager | Everything in the tenant |
| `arun.mehta@acmetech.example.in` | Service Desk Manager | Queue, priority override, major incidents, SLA config, audit |
| `kavya.nair@acmetech.example.in` | Service Desk Agent | Work the queue; no override, no audit |
| `aditya.menon@acmetech.example.in` | Requester | Own tickets only; never sees work notes |
| `vikram.iyer@acmetech.example.in` | Asset Manager | Read, reporting |
| `neha.gupta@acmetech.example.in` | CMDB Administrator | Read, reporting |

Plus 12 more across IT, Security, Finance, HR and Operations — 18 users in total.

Second tenant **Northwind Logistics India**: `deepak.varma@northwind.example.in`. It exists so
isolation can be demonstrated live against a neighbour that genuinely has data.

All synthetic. `example.in` is a reserved domain that cannot receive mail.

---

## 16. Demo data

**420 incidents across 2 tenants, over roughly 14 days**, generated with a fixed random seed
(`20260907`) so the data is reproducible.

18 users, 6 assignment groups (SD-L1, SD-L2, Network Operations, Application Support,
Infrastructure and Cloud, Security Operations), 5 offices (Bengaluru HQ, Pune, Hyderabad,
Gurugram, Chennai), a full category tree, a priority matrix and business calendars.

The generator aims for a queue that looks like a real service desk rather than a uniform sample:
~13% of incidents left open; open ages drawn per priority with ~21% deliberately past target;
response delays drawn against the response target with ~14% missing it; repeated scenario titles
qualified with office names so the queue does not read as a copy-paste; comment timestamps
clamped to now so nothing renders as "in 47 minutes".

**Every SLA figure is computed by the real engine over that data.** Nothing is a hard-coded
statistic. At the last reseed the queue showed 64 open, 13 breached (20% of open), 11 P1 and 14
P2 open, 3 due within two hours, and work spread across 6 agents — but open-age figures are
relative to seed time, so `reseed-demo.ps1` **prints the current figures** and those are the ones
to quote.

---

## 17. Demo scenarios

Five scripted demonstrations, in [DEMO.md](DEMO.md):

| Script | Shows | Time |
|---|---|---|
| **A — Service desk manager** | Live dashboard, URL-driven queue filters, business-hours SLA clocks, priority override with mandatory reason, audit | 5 min |
| **B — Agent** | My Work, lifecycle, pause on Pending with credited minutes, a permission she genuinely lacks | 3 min |
| **C — Requester** | Own tickets only; work notes absent from the *query*, not hidden in the browser | 2 min |
| **D — Tenant isolation** | Paste an Acme URL as a Northwind user → **404, not 403** — then run the 18 isolation tests on screen | 3 min |
| **E — AI assistant** | Either an honest "not configured", or grounded answers citing real incident numbers with the tools used shown — and the same question answered differently for a requester | 2 min |

Script D is the one that closes enterprise deals, and it is done live rather than described.

The 30 scenarios behind the data are drawn from what an Indian enterprise service desk actually
sees: branch VPN failures, SAP and Tally issues, biometric attendance devices, GST portal access,
leased-line outages.

---

## 18. Tests

**258 tests.** Strategy: test what would be catastrophic or expensive to get wrong, at the
cheapest layer that can prove it.

| Suite | Tests | Runs against | Time |
|---|---|---|---|
| Domain | 113 | Nothing — pure objects | < 1 s |
| Application | 31 | Fakes | < 1 s |
| Integration | 76 | Real HTTP, real SQL Server | ~49 s |
| Front end (Vitest) | 38 | jsdom | ~6 s |

The domain suite is largest because SLA arithmetic and the state machine are where mistakes cost
most and proof is cheapest. The integration suite is second because **tenant isolation and
authorization cannot be proved with a mock** — a test that mocks the DbContext proves the mock
was configured correctly. Two real tenants, real rows, real JWTs, a privileged neighbour trying
every route.

Notable coverage: 18 tenant isolation tests; 14 permission-catalogue tests; 10 AI security tests;
23 incident lifecycle tests; 18 business-schedule tests asserting to the minute.

Front-end tests query by role and text, not by CSS class or test id.

Full detail: [TESTING.md](TESTING.md).

---

## 19. Test results

All gates pass on the current tree, verified in this session.

| Gate | Result |
|---|---|
| `dotnet build NexaOps.slnx` | **Succeeded — 0 warnings, 0 errors** |
| `NexaOps.Domain.Tests` | **113 passed**, 0 failed, 0 skipped (195 ms) |
| `NexaOps.Application.Tests` | **31 passed**, 0 failed, 0 skipped (441 ms) |
| `NexaOps.Api.IntegrationTests` | **76 passed**, 0 failed, 0 skipped (49 s) |
| `npm run typecheck` | Clean |
| `npm run lint` | Clean |
| `npm run test` | **38 passed** (3 files) |
| `npm run build` | Succeeded — 1141 modules, 4 chunks, **264 KB gzipped** |
| `az bicep build` | Exit 0, **no warnings** |
| `npm audit --audit-level=moderate` | **0 vulnerabilities** |
| NuGet audit (`NuGetAuditMode=all`, moderate) | No advisories — build would fail otherwise |

**220 .NET + 38 front end = 258, all passing.**

One gate in the requested list is **not run: E2E tests. There is no E2E browser suite.** User
journeys were verified manually through a browser; that verification is not automated and does
not run in CI. Reporting it as passing would be false.

---

## 20. Security findings

Three genuine defects were found by the test suite during development and fixed. They are listed
because "the tests pass" only means something if the tests have ever caught anything.

| Finding | Impact | Fix |
|---|---|---|
| **Optimistic concurrency was not enforced.** Assigning a caller-supplied row version to the entity property does nothing — EF compares the version the tracker *loaded* | Silent lost updates: two agents edit one incident, last write wins, no warning | `SetExpectedVersion` on the persistence port, setting `OriginalValue` |
| **A paused SLA clock could breach.** `MarkBreachedIfOverdue` accepted `Paused` | Overstated breach rates; customers charged for time the desk was blocked on them | Only `InProgress` can breach; the monitor no longer scans paused clocks |
| **`PauseWhenPending` read through an unloaded navigation**, silently defaulting to `true` | A commitment configured to run continuously silently paused | Copied onto the clock at attach time |

Two more found and fixed in the same period:

- **`SuppressTenantFilter` was a plain flag.** A nested call's `finally` un-suppressed an outer
  scope, producing a real `TenantIsolationViolationException` during seeding — the guard worked
  correctly and caught it. Replaced with a disposable scope that restores the previous value.
- **`HttpCurrentUser` captured the principal in its constructor.** The exception middleware
  resolves `IAuditService` → `ICurrentUser` *before* authentication runs, freezing an anonymous
  principal and producing a 403 on every permission check. Now read per access.

One test was itself wrong: it asserted malformed arguments were rejected before the permission
check. The implementation had the ordering right, so the test was corrected and renamed
`Permission_is_checked_before_tool_arguments_are_even_parsed`. Changing the code to satisfy a
wrong test would have made the product worse.

Threat coverage — SQL injection, XSS, CSRF, IDOR, broken access control, privilege escalation,
file upload, secret leakage, session attacks, API abuse, mass assignment, error disclosure — is
tabulated in [SECURITY.md §4](SECURITY.md).

**No compliance certification, no penetration test, no independent assessment is claimed.**

---

## 21. Known limitations

Grouped by how much they should worry you. Complete list: [STATUS.md §4](STATUS.md).

**Blocks a production deployment for a security-conscious customer**
- **No malware scanning on attachments.** `AttachmentScanStatus` exists and a non-clean file is
  never served, but nothing sets it to `Clean`. Do not accept files from untrusted users yet.
- **No private endpoints.** SQL, Storage and Service Bus are publicly reachable, restricted by
  firewall and Entra auth.
- **No SQL row-level security.** Isolation is enforced in the application; a privileged direct
  database connection bypasses it.
- **No penetration test, no certification.**

**Blocks a customer with regulatory obligations**
- **Retention is not enforced.** `Tenant.DataRetentionDays` is stored; no purge job exists.
- **No data export.** A departing customer cannot extract their data through the product.
- **No subject-erasure workflow.**
- **No customer-managed keys.**
- **No per-tenant restore** — a point-in-time restore is database-wide. Inherent to the shared
  database model.

**Operational**
- **No alert rules or action group in Bicep** — [OBSERVABILITY.md §8](OBSERVABILITY.md) specifies
  them; nothing deploys them.
- **No automated backup verification**; quarterly manual restore testing only.
- **The regional DR runbook has never been executed.** The 8–12 hour RTO is an estimate.
- **No blob cross-region replication** — a regional disaster loses attachment bytes.
- **No load testing.**

**Functional**
- **Entra ID: token validation complete, first-sign-in user provisioning not built.** No SCIM.
- **Platform impersonation designed, not implemented.** No cross-tenant platform reporting.
- **English only** — `en-IN` is a formatting locale, not a translation.
- **No SMS or WhatsApp channel**, which matter more than email for Indian field staff.
- **Email requires Azure Communication Services**; locally it queues and honestly says so.
- **No E2E browser suite.**
- **GSTIN/PAN are format-validated only**, never verified against a registry.

**AI**
- **No RAG, no embeddings in use, no mutating tools and no confirmation UI, no automatic
  categorisation or duplicate detection. Tokens are measured, not capped.**

**Carried deliberately:** API-versioning analyzer warnings AV0029/AV0030 suppressed with a
documented revisit note; offset paging rather than keyset; no coverage gate.

---

## 22. Next implementation phase

**Recommended: Service Request Management and the Service Catalogue.**

Why this and not something else:

1. It reuses the entire foundation — tenancy, permissions, audit, SLA, notifications, number
   sequences, categories with a `Module` discriminator, module-agnostic `RecordRelations` — and
   adds a genuinely new shape: **catalogue items with variables, and approvals**. That is the
   right second module because it proves the foundation is a foundation rather than
   incident-management scaffolding.
2. **Approvals are a prerequisite for change management**, so building them here is not a detour.
3. Requests outnumber incidents several times over in most Indian SMB and mid-market
   deployments, so it is the highest-volume module.

Three items should be treated as **prerequisites rather than backlog**, done before or alongside
it:

1. **Malware scanning on attachments.** The product cannot responsibly accept files from
   untrusted users without it.
2. **Retention enforcement and a data export path.** The first customer with a DPDP question asks
   for both.
3. **An E2E browser suite.** Manual verification does not survive a second module.

**Not recommended next: the workflow engine.** It is the most valuable long-term capability and
the easiest to build prematurely. Build it after two or three modules exist, so it is designed
against real orchestration needs rather than imagined ones.
