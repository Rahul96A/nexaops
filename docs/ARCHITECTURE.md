# NexaOps — Architecture

> NexaOps is an original, independently designed AI-native enterprise IT Service Management
> and workflow automation platform. It is **not** derived from, and contains no source code,
> trademarks, branding, schema, or copyrighted material from any commercial ITSM vendor.

---

## 1. Architectural style

NexaOps is a **modular monolith** with a strict, one-directional dependency graph. Modules are
enforced by project and namespace boundaries inside a small number of deployable units, not by
network hops. This keeps transactional consistency simple (a single relational database, a
single `SaveChanges` unit of work) while leaving clean seams to extract a module into its own
service later if a genuine scaling or ownership reason appears.

We deliberately **do not** use microservices at this stage. Splitting a twenty-module ITSM
domain across process boundaries before the domain has stabilised buys distributed-systems
cost — eventual consistency, saga orchestration, N deployment pipelines — and returns nothing
that a module boundary inside one process does not already provide.

```
+--------------------------------------------------------------+
|  React 19 SPA (TypeScript, Vite, MUI, TanStack Query)         |
|  No secrets. No direct DB access. No direct AI provider calls.|
+---------------------------+----------------------------------+
                            |  HTTPS / REST + OpenAPI, Bearer JWT
+---------------------------v----------------------------------+
|  NexaOps.Api  (ASP.NET Core 10)                               |
|  Endpoints - AuthN/AuthZ - Rate limiting - Validation         |
|  Problem Details - Security headers - Correlation IDs         |
+---------------------------+----------------------------------+
+---------------------------v----------------------------------+
|  NexaOps.Application                                          |
|  Use cases - DTOs - FluentValidation - Permission catalogue   |
|  AI orchestration + typed AI tool registry                    |
+---------------------------+----------------------------------+
+---------------------------v----------------------------------+
|  NexaOps.Domain                                               |
|  Entities - Value objects - Domain rules - Domain events      |
|  Zero framework dependencies (no EF, no ASP.NET, no Azure)    |
+---------------------------+----------------------------------+
+---------------------------v----------------------------------+
|  NexaOps.Infrastructure                                       |
|  EF Core 10 - Persistence - Identity - Audit - SLA clock      |
|  Azure adapters: Blob, Service Bus, Redis, Key Vault,         |
|  OpenAI, AI Search, App Insights, Communication Services      |
+---------------------------+----------------------------------+
                            v
      Azure SQL Database - Blob Storage - Service Bus - Redis
      Key Vault - Azure OpenAI - Azure AI Search - App Insights
```

### Dependency rule

`Api -> Application -> Domain` and `Infrastructure -> Application -> Domain`.
`Domain` depends on nothing. `Application` defines **interfaces** (ports); `Infrastructure`
provides **implementations** (adapters). The API composes them at startup. This is enforced by
project references — `NexaOps.Domain.csproj` carries no package references at all, so an
accidental `using Microsoft.EntityFrameworkCore` in the domain simply will not compile.

---

## 2. Projects

| Project | Responsibility |
|---|---|
| `src/NexaOps.Domain` | Entities, enums, value objects, domain invariants, domain events. Pure C#. |
| `src/NexaOps.Application` | Use-case services, DTOs, validators, port interfaces, AI tool contracts, permission catalogue. |
| `src/NexaOps.Infrastructure` | `NexaOpsDbContext`, EF configurations, migrations, audit interceptor, tenant filters, SLA calculator, Azure adapters, AI providers. |
| `src/NexaOps.Api` | HTTP surface. Controllers, auth wiring, DI composition root, OpenAPI, health checks, middleware pipeline, background workers. |
| `src/NexaOps.Web` | React SPA. |
| `tests/NexaOps.Domain.Tests` | Pure domain rules: priority matrix, state machine, SLA clock arithmetic. |
| `tests/NexaOps.Application.Tests` | Use-case behaviour against an in-memory relational provider. |
| `tests/NexaOps.Api.IntegrationTests` | End-to-end HTTP: authentication, authorization, tenant isolation, incident lifecycle. |
| `infra/bicep` | Azure infrastructure as code with per-environment parameter files. |

---

## 3. Multi-tenancy

Full design and threat model: [MULTI-TENANCY.md](MULTI-TENANCY.md).

**Shared database, shared schema, tenant discriminator column**, enforced at three independent
layers so that a bug in any one layer is not by itself sufficient to leak data:

1. **EF Core global query filters** on every `ITenantOwned` entity — every read is filtered.
2. **A `SaveChanges` interceptor** — stamps `TenantId` on insert and *rejects* any write whose
   `TenantId` does not match the ambient tenant, including an update that tries to move a row
   from one tenant to another.
3. **Explicit resource checks** in application services when one aggregate references another.

The tenant is resolved **exclusively** from the authenticated server-side principal (the
`nexaops:tid` claim, issued by our token service after a database lookup). A `tenantId` in a
request body, query string, route, or header is ignored. Platform-administration endpoints that
legitimately need to act across tenants take an explicit impersonation path that checks the
`platform.tenant.manage` permission and writes an audit record.

---

## 4. Identity and access

Two authentication modes ship, selected by configuration (`Auth:Mode`):

* **`Local`** — a first-party JWT issued by NexaOps. Passwords are hashed with ASP.NET Core's
  PBKDF2 `PasswordHasher` (format marker v3, HMAC-SHA512, 210 000 iterations). Refresh tokens
  rotate, are single-use, are stored only as SHA-256 hashes, and are revocable per session or
  per user. This mode exists so demos, pilots, and customers without Entra ID have a real,
  fully functional login rather than a stub.
* **`EntraId`** — Microsoft Entra ID (OpenID Connect / JWT bearer). The token's `oid` and `tid`
  are mapped to a NexaOps user and tenant at first sign-in. MFA and Conditional Access are
  enforced by Entra and deliberately not re-implemented here.

Both modes converge on an identical `ClaimsPrincipal` shape, so every authorization decision in
the codebase is written once and works under either mode.

Authorization is **permission-based, not role-based**, at the point of enforcement. Roles are a
grouping construct for administrators; the code always asks "does this principal hold
`incident.assign`?", never "is this principal a Manager?". A custom role therefore becomes a
configuration change rather than a code change. See [SECURITY.md](SECURITY.md).

---

## 5. Audit

Auditing is an EF Core `SaveChangesInterceptor` that captures before/after snapshots of tracked
entity changes, plus an explicit `IAuditService` for events that are not entity mutations
(sign-in, sign-in failure, permission denial, AI tool execution, export, impersonation).

Every record carries tenant, actor, UTC timestamp, entity type and id, action, JSON before and
after states, correlation id, IP address, and user agent.

Audit rows are append-only. No update or delete path is exposed anywhere in the application,
and reading them requires the `audit.read` permission.

---

## 6. AI architecture

The browser never talks to an AI provider. Full detail: [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md).

```
React -> /api/v1/ai/*  ->  AI Orchestrator  ->  Tool Registry (typed, permissioned)
                                            ->  Azure OpenAI (chat + embeddings)
                                            ->  Azure AI Search (RAG retrieval)
                                 |
                                 v
                        Domain services (the same ones the REST API uses)
                                 |
                                 v
                          Azure SQL (tenant-filtered)
```

The model can only affect the system by invoking a **registered tool**. Each tool declares a
JSON input schema, an output schema, a required permission, and whether it mutates state.
Before any tool executes the orchestrator runs: schema validation, permission check, tenant
check, business validation, execution, audit. Mutating tools never execute directly — they
return a **proposal** which a human must explicitly confirm in the UI.

If Azure OpenAI is not configured the AI endpoints return `503` with an `ai_not_configured`
problem type. **No canned or simulated AI answers are ever returned.** A demo without AI
credentials shows AI as unavailable rather than pretending to be intelligent.

---

## 7. Cross-cutting concerns

| Concern | Implementation |
|---|---|
| Logging | Serilog, structured; compact JSON in production. Correlation id, tenant id, and user id enriched onto every event. |
| Tracing | OpenTelemetry (ASP.NET Core, HttpClient, EF Core instrumentation) exported to Azure Monitor. |
| Metrics | OpenTelemetry meters plus custom counters (`nexaops.incidents.created`, `nexaops.sla.breached`, `nexaops.ai.tool.invoked`). |
| Errors | RFC 9457 Problem Details on every failure. Stack traces never leave the server outside Development. |
| Validation | FluentValidation in Application; a filter converts failures into `400` with per-field errors. |
| Caching | An `IApplicationCache` port — in-memory locally, Azure Cache for Redis in Azure. Every key is tenant-prefixed. |
| Background work | Hosted services for the SLA monitor and the notification dispatcher; Azure Service Bus and Functions for out-of-process work. |
| Concurrency | SQL `rowversion` on mutable aggregates; optimistic concurrency failures surface as `409`. |
| Money | `decimal(18,4)`. Never `float` or `double`. |
| Time | All timestamps stored in UTC. Rendered in the tenant's timezone, default `India Standard Time`. |

---

## 8. Data model conventions

Every business entity derives from `TenantEntity` and therefore carries `Id` (uniqueidentifier),
`TenantId`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, and `RowVersion`.

Soft-archive uses `IsArchived` plus `ArchivedAt`. We do not hard-delete business records, which
preserves audit integrity and referential history. See [DATABASE.md](DATABASE.md).

---

## 9. Deployment topology

| Environment | Compute | Database | Notes |
|---|---|---|---|
| Local | Kestrel + Vite dev server | SQL Server LocalDB or Docker `mssql` | No Azure dependency required. |
| Development | Azure Container Apps | Azure SQL (S0) | Auto-deploy on merge to `main`. |
| Staging | Azure Container Apps | Azure SQL (S1, zone redundant) | Smoke tests gate promotion. |
| Production | Azure Container Apps + Front Door + APIM | Azure SQL Business Critical | Manual approval gate. |

All Azure access uses **Managed Identity**. No connection string, API key, or client secret is
present in source, container images, or plain app settings — secrets live in Key Vault and are
referenced by URI, and in Azure the database uses Entra authentication with no password at all.

See [AZURE-DEPLOYMENT.md](AZURE-DEPLOYMENT.md).
