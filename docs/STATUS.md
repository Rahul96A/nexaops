# NexaOps — Status

**As of 7 September 2026.** Phase 1 (platform foundation) plus Incident Management plus a demo
environment.

This document is written to be handed to someone who has to decide whether to rely on this. It
lists what works, what does not, and what is deliberately absent — with the gaps in the same
detail as the achievements.

---

## 1. Quality gates — all passing

| Gate | Result |
|---|---|
| `dotnet build NexaOps.slnx` | **0 warnings, 0 errors** |
| `dotnet test NexaOps.slnx` | **220 passing** |
| `npm run typecheck` | Clean |
| `npm run lint` | Clean |
| `npm run test` | **38 passing** |
| `npm run build` | Succeeds — ~264 KB gzipped, 4 chunks |
| `az bicep build --file infra/bicep/main.bicep` | **0 warnings** |
| NuGet audit (`NuGetAuditMode=all`, moderate) | No advisories |
| `npm audit` | No advisories |
| gitleaks (full history) | No secrets |

258 tests total. Breakdown and strategy in [TESTING.md](TESTING.md).

---

## 2. Built and working

### Platform

| Capability | Notes |
|---|---|
| Multi-tenancy | Three independent enforcement layers, 18 integration tests |
| Authentication (local JWT) | PBKDF2 210k iterations, lockout, rotating single-use refresh tokens with theft detection |
| Authorization | ~50 permissions, 12 seeded roles, enforced at endpoint **and** service |
| Audit | Append-only, before/after snapshots, redaction, correlation ids |
| SLA engine | Business-hours arithmetic, Indian calendars, pause/resume with credit |
| Notifications | In-app bell + email dispatch worker (email needs ACS configured) |
| Number sequences | Per-tenant, gapless, `INC0000042` |
| Attachments | Upload, allow-listed extensions, SHA-256, tenant-prefixed paths |
| Caching | Redis or in-memory, every key tenant-prefixed |
| Error handling | RFC 9457 Problem Details with stable machine-readable codes |
| API | Versioned, OpenAPI + Scalar at `/docs`, rate limited |
| Health | Separate liveness and readiness |
| Telemetry | Serilog + OpenTelemetry traces and metrics, 7 product metrics |

### Incident Management — complete

Full lifecycle: New → Assigned → In progress → Pending → Resolved → Closed, with Cancelled, and
`Closed`/`Cancelled` terminal. Transitions are governed by a single state machine that is the
only source of truth for what is legal.

- Priority derived from a per-tenant impact × urgency matrix, with a manager override that
  **requires a reason**.
- Response and resolution SLA clocks attached on creation, paused on Pending, re-targeted when
  priority changes, and breached at the deadline — dated to the deadline, not to when the monitor
  noticed.
- Comments split into public replies and internal work notes, with work notes filtered **at the
  query level** and the comment count filtered with them.
- Major incident declaration.
- Optimistic concurrency: a stale edit is refused, never silently overwritten.
- Full activity timeline and per-record audit view.

**API surface:** 12 incident endpoints, 5 auth, 7 reference data, 4 notification, 2 audit, 2 AI.

### Front end

React 19 / TypeScript 5.9 / Vite 7 / MUI 7 / TanStack Query 5.

Service desk dashboard, incident queue with URL-driven filters, incident record page with
activity and audit tabs, new-incident form, My Work, audit browser, global search, notification
bell, AI assistant panel, sign-in, change password. Light and dark themes.

### Infrastructure

Bicep for four environments, CI with four parallel jobs, CD with OIDC federated auth, migration
bundles, staged rollout behind a protected environment, smoke tests, and automatic rollback.

### Demo

420 incidents across 2 tenants, 18 users, 6 groups, 5 offices, 30 realistic Indian-enterprise
scenarios. Deterministic seed. Every figure computed by the real engine.

---

## 3. Deliberately not built in this phase

These appear in the navigation marked **"Later"** and are not clickable. Nothing pretends to work.

Service requests, service catalogue, problem management, change management and CAB, knowledge
base, CMDB, asset management, the visual workflow engine, reporting and dashboard builder,
virtual agent, mobile apps, inbound email, third-party integrations.

The foundation they will share — tenancy, identity, permissions, audit, SLA, notifications,
attachments, number sequences, and a module-agnostic `RecordRelations` table — is built and
tested. `Category` already carries a `Module` discriminator; the SLA engine is already
module-agnostic. Adding a module is adding a module, not reworking the platform.

---

## 4. Known limitations

Grouped by how much they should worry you.

### Blocks a production deployment for a security-conscious customer

| Limitation | Consequence |
|---|---|
| **No malware scanning on attachments.** `AttachmentScanStatus` exists and a non-clean file is never served, but nothing sets it to `Clean` | Do not accept attachments from untrusted users until Defender for Storage is integrated |
| **No private endpoints.** SQL, Storage and Service Bus are reachable over the public network, restricted by firewall and Entra auth | A customer requiring no public network path cannot deploy as-is |
| **No SQL row-level security.** Isolation is enforced in the application | A direct database connection with sufficient privilege bypasses it |
| **No penetration test and no certification** | Every control in [SECURITY.md](SECURITY.md) is implemented; none is independently assessed |

### Blocks a customer with regulatory obligations

| Limitation | Consequence |
|---|---|
| **Retention is not enforced.** `Tenant.DataRetentionDays` is stored; no purge job exists | Data is retained indefinitely regardless of the setting |
| **No data export** | A departing customer cannot extract their data through the product |
| **No subject-erasure workflow** | A DPDP erasure request has no supported path |
| **No customer-managed keys** | Encryption at rest uses Microsoft-managed keys |
| **No per-tenant restore.** A point-in-time restore is database-wide | Inherent to the shared-database model; a customer needing it belongs on a dedicated deployment |

### Operational gaps

| Limitation | Consequence |
|---|---|
| **No alert rules or action group in Bicep** | [OBSERVABILITY.md §8](OBSERVABILITY.md) specifies them; nothing deploys them |
| **No automated backup verification.** Quarterly manual restore testing only | An untested backup is a hypothesis |
| **The regional DR runbook has never been executed** | 8–12 hour RTO is an estimate, not a measurement |
| **No blob cross-region replication** | A regional disaster loses attachment bytes; metadata survives |
| **No load testing** | Indexes are designed for these queries; nothing has measured them under load |
| **No dashboards as code** | Queries are documented; no dashboard is provisioned |

### Functional gaps

| Limitation | Consequence |
|---|---|
| **Entra ID sign-in: token validation is complete, first-sign-in user provisioning is not** | Federated customers cannot onboard yet; local auth works fully |
| **No SCIM** | User lifecycle is manual |
| **Platform impersonation is designed, not implemented** | A platform admin cannot act inside a customer tenant |
| **No cross-tenant platform reporting** | |
| **English only** | `en-IN` is a formatting locale, not a translation. No i18n framework is wired in |
| **No SMS or WhatsApp channel** | Matters more in India than email for field staff |
| **Email needs Azure Communication Services** | Locally, notifications queue and honestly report as not sent |
| **No E2E browser test suite** | Journeys verified manually; that verification is not automated |
| **GSTIN and PAN are format-validated only** | Never verified against a registry |

### AI gaps

| Limitation | Consequence |
|---|---|
| **No retrieval-augmented generation** | Options and infrastructure exist; no index is populated, no retrieval tool registered |
| **No embeddings in use** | The adapter exposes them; nothing generates or stores a vector |
| **No mutating tools and no confirmation UI** | `IsMutating` is honoured everywhere — the refusal path exists before anything can take it |
| **No automatic categorisation, duplicate detection or resolution suggestion** | The assistant answers questions about data; it does not classify or predict |
| **Tokens are measured, not capped** | No per-tenant AI budget |

---

## 5. Technical debt carried deliberately

**API versioning analyzer warnings AV0029/AV0030 are suppressed** with a documented revisit note.
The clean fix is the versioning OpenAPI package, which was not available for this .NET 10
preview combination. Revisit when it is.

**Offset paging, not keyset.** Correct and adequate at demo volume; will need revisiting at
depth.

**No coverage measurement or gate.** A percentage would mostly measure how much of `Program.cs` a
test happened to execute.

**`Auth:Mode` is a string, not a strategy.** Fine for two modes; would want restructuring at
four.

---

## 6. Defects found and fixed during this phase

The suite has caught real bugs, which is the only reason "the tests pass" means anything. Full
detail in [SECURITY.md §7](SECURITY.md) and [TESTING.md §9](TESTING.md).

| Defect | Was |
|---|---|
| Optimistic concurrency silently did nothing | Lost updates between two agents editing one incident |
| A paused SLA clock could breach | Overstated breach rates; time the desk was blocked counted against us |
| `PauseWhenPending` read through an unloaded navigation | A commitment configured to run continuously silently paused |
| `SuppressTenantFilter` was a plain flag; a nested reset un-suppressed the outer scope | Tenant isolation violation during seeding. Fixed with a disposable scope |
| `HttpCurrentUser` captured the principal in its constructor | 403 on every permission check, because the exception middleware resolved it before authentication ran |
| Assigning a no-tracking entity to a navigation | PK violation on `sla.SlaDefinitions` |
| `GroupBy` with a key reaching through a navigation | 500 on the dashboard workload panel |
| Invalid sort field returned 409 | Wrong status; now 400 with field errors |
| Audit row volume during seeding | Command timeouts. Fixed with batching and an audit-suppression scope |

---

## 7. Next implementation phase

**Recommended: Service Request Management and the Service Catalogue.**

Why this and not something else:

- It reuses the entire foundation — tenancy, permissions, audit, SLA, notifications, number
  sequences, categories with a `Module` discriminator — and adds a genuinely new shape:
  catalogue items with variables, and approvals. That is the right second module because it
  proves the foundation is a foundation rather than incident-management scaffolding.
- Approvals are a prerequisite for change management, so building them here is not a detour.
- It is the highest-volume module in most Indian SMB and mid-market deployments. Requests
  outnumber incidents several times over.

Before or alongside it, three items should be treated as prerequisites rather than backlog:

1. **Malware scanning on attachments.** The product cannot responsibly accept files from
   untrusted users without it.
2. **Retention enforcement and a data export path.** The first customer with a DPDP question will
   ask for both.
3. **An E2E browser suite.** Manual verification does not survive a second module.

**Not recommended next:** the workflow engine. It is the most valuable long-term capability and
the easiest to build prematurely. It should be built after two or three modules exist, so it is
designed against real orchestration needs rather than imagined ones.
