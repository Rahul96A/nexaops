# NexaOps — Status

**As of 8 September 2026.** Phase 1 (platform foundation), Incident Management, Phase 2
(Service Requests, Service Catalogue and Approvals) Phase 3 (Problem Management), Phase 4
(Change Management and the CAB) Phase 5 (Knowledge Base), Phase 6 (CMDB) and
Phase 7 (Asset Management), plus a demo environment.

This document is written to be handed to someone who has to decide whether to rely on this. It
lists what works, what does not, and what is deliberately absent — with the gaps in the same
detail as the achievements.

---

## 1. Quality gates — all passing

| Gate | Result |
|---|---|
| `dotnet build NexaOps.slnx -warnaserror` | **0 warnings, 0 errors** |
| `dotnet test NexaOps.slnx` | **515 passing** |
| `npm run typecheck` | Clean |
| `npm run lint` | Clean |
| `npm run test` | **38 passing** |
| `npm run build` | Succeeds — ~274 KB gzipped, 4 chunks |
| `az bicep build --file infra/bicep/main.bicep` | **0 warnings** |
| NuGet audit (`NuGetAuditMode=all`, moderate) | No advisories |
| `npm audit` | No advisories |
| gitleaks (full history) | No secrets |

553 tests total (308 domain, 31 application, 176 integration, 38 front end). Breakdown and
strategy in [TESTING.md](TESTING.md).

> Two of these gates were previously reported as passing when they were not. `dotnet build`
> reported zero warnings because the verification build was incremental and never recompiled the
> test project; CI builds with `/warnaserror` and had five. And `npm ci` failed from a clean
> clone because the lock file was out of sync, which local builds never exercised. Both are
> fixed, and the build is now verified with `--no-incremental -warnaserror`.

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

### Service Requests, Catalogue and Approvals — complete

- **Service catalogue** with per-item fields of nine types. Every submitted answer is validated
  server-side against the item's own definition; an answer to a field the item does not define is
  refused, so a crafted payload cannot write arbitrary keys onto a record. An item cannot be
  published without a fulfilment group or a configured approver.
- **Ordering** snapshots each line's name and price, so editing the catalogue later cannot rewrite
  what somebody ordered or what an approver authorised on that basis.
- **Approvals** are module-agnostic (`Module` + `RecordId`), so change management will reuse the
  table and the stage arithmetic unchanged. Manager approval resolves to a person at submission
  time rather than storing a rule that could go stale. Two items needing the same approver produce
  one approval, not two. Holding `approval.act` is not sufficient to decide — the record itself
  says who may, and a non-addressee gets 404 rather than 403.
- **Fulfilment** per line, with the request completing only once every line has settled.
- **A fulfilment SLA clock**, paused for the whole time the request waits on an approver. The
  desk is not charged for time it has not been authorised to act in, and a cancelled request
  abandons its clock rather than breaching it. Targets are working days — one, two, three, five
  and ten — because that is how delivery is actually promised.
- A requester may withdraw their own request without holding `request.cancel`.

### Asset Management — complete (API and tests; no UI yet)

- **Custody is history, not a field.** "Who had this laptop in March" is what an audit or a
  security incident actually asks, and a single mutable holder cannot answer it. Issuing,
  returning and disposing all close the open custody record.
- **Disposal closes custody first**, so nobody stays apparently accountable for a thing that no
  longer exists — and it has its own action rather than being reachable through a general edit,
  which would skip both that and the disposal date.
- **A refresh date is absent rather than guessed.** One invented from a missing purchase date
  would be worse than none, because somebody would budget against it.
- **Licence compliance is computed, not stored.** A register that can disagree with itself
  answers nothing. Expiry beats everything: every deployment against a lapsed agreement is
  unlicensed however comfortable the seat count looks. Usage at 80% or more reads as compliant
  rather than under-used, so nobody cancels seats they are about to need.
- Disposal and licence administration sit with the asset manager, not the service desk manager:
  disposal removes something a finance audit expects to be able to count.
- Assets and configuration items are **linked, not merged**. A CI answers what a thing supports;
  an asset answers who has it and what it cost. Merging produces a record that serves neither.

- **A handback cannot end an asset's life.** The return endpoint takes its destination status
  from the caller and sits behind `asset.assign`, which a service desk manager holds; disposal
  sits behind `asset.dispose`, which they do not. Returns are constrained to stock, repair, or
  unaccounted-for, so handing a laptop back is not a route around that split — and cannot leave a
  disposed asset with no disposal date for finance to explain.
- **Unaccounted-for is distinct from disposed**, and coloured as a problem in the UI. A device
  nobody can find is a security question; recording it as disposal answers the wrong one.

**Not built:** any automatic discovery of software installations. `DeployedCount` is maintained
by whoever knows — an inventory feed or a person. NexaOps does not discover installations itself,
and the compliance position is only as good as that number. Assets are created and disposed of
through the API; the UI covers the register, custody and the licence position.

### CMDB — complete

- **Impact analysis is pure graph arithmetic**, testable without a database and identical
  whether called from a change's impact assessment or a CI record page. It answers both
  directions: what breaks if this fails, and what this relies on.
- **Direction is not symmetric.** A database depending on a server is a different statement from
  the reverse, and treating the edge as undirected would make the answers useless.
- **The shallowest path wins**, so an item reachable at both one and four hops is reported as
  directly affected rather than distantly. Depth is capped at six — a real CMDB accumulates long
  chains, and a walk that follows all of them turns a record page into a timeout.
- **Cyclic graphs terminate.** Two servers that each fail over to the other is a cycle, and a
  correct one.
- **Deliberately not a state machine.** A CMDB reflects reality rather than governing it: a
  server disposed of and then found in a cupboard really can return to service, and refusing that
  only teaches people to keep a spreadsheet instead.
- Out-of-support cover is a first-class question rather than a report, because an expired
  warranty discovered during an outage is the most expensive way to learn about it.
- The graph cannot be made to span a tenant boundary: the target look-up is tenant-filtered, so
  a neighbour's item reads as non-existent.

- The record page shows impact and dependency as **two separate lists rather than one merged
  graph**, because they answer different questions during an outage: who to warn, and what to
  check first. Depth is shown, because a directly affected item is somebody's immediate problem
  and one four hops away is a heads-up.

**Not built:** item and relationship editing in the UI. Both are managed through the API; the
pages read the register and the graph.

### Knowledge Base — complete

- **A stale article stays readable.** Withdrawing guidance the moment its review date passes
  leaves the service desk with nothing, which is worse than guidance that is merely old. It is
  flagged as unverified wherever it appears rather than hidden.
- **Publishing is a separate permission from writing**, so an author cannot self-publish
  unreviewed guidance to the whole organisation. Publishing also sets the next review date rather
  than leaving it open-ended.
- **An unrated article has no rating, not a bad one.** Showing 0% would quietly condemn every new
  article, so the ratio is null until somebody votes.
- **Feedback is stored per reader**, so changing your mind moves the vote instead of adding a
  second one, and "who found this unhelpful and why" is answerable.
- A reader without `knowledge.read.internal` cannot see that an internal runbook exists at all —
  invisible, not merely unopenable.
- **Retired is not terminal.** Withdrawn guidance can be reinstated; forcing a copy would lose the
  article's history and its usage counters.

**Not built:** Markdown rendering (article bodies display as pre-wrapped plain text — a Markdown
renderer is a real XSS surface for user-written content and needs sanitising properly), article
version history, a create/edit form in the UI, and the background sweep that marks published
articles stale (the domain method and its index exist; nothing schedules it yet).

### Change Management and the CAB — complete

- **Three change types that genuinely differ.** A standard change proceeds on a procedure that
  was approved once; a normal change goes to the advisory board; an emergency change proceeds now
  and is reviewed afterwards. Treating all three alike is how an unofficial process grows up
  beside the official one.
- **Raising an emergency change needs its own permission.** That is the control that stops the
  emergency path becoming the normal one, and the emergency rate is on the dashboard because a
  rising count means the normal process is failing people.
- **Review cannot be skipped.** There is no transition from Implementing to Closed, and a change
  cannot close without a recorded outcome — otherwise change success reporting counts records
  rather than measuring anything.
- **Anything above low risk needs a rollback plan.** A change nobody knows how to back out turns
  a bad hour into a bad week. Low risk is exempt, because the ceremony would outweigh the exposure.
- **Window collisions are a warning, not a block.** Two changes in one window is sometimes exactly
  the intent, and the person scheduling is better placed to judge than a rule is.
- Approvals reuse the module-agnostic infrastructure built for requests, unchanged.

### Problem Management — complete

- **Known error is a resting state, not a waypoint.** A published workaround delivers value even
  if the permanent fix is never funded, and the lifecycle says so — but it still counts as open
  work, so reporting cannot hide a backlog of unfunded fixes.
- Publishing a known error **requires both a root cause and a workaround**, because it tells the
  whole service desk there is something they can do. Resolving requires a recorded permanent fix.
- **Investigating and publishing are separate permissions.** An agent records findings; a manager
  commits the desk to them.
- Problems are readable tenant-wide, unlike incidents and requests — a known error the person on
  the phone cannot find helps nobody. Internal investigation notes are still filtered at the
  query level.
- Incidents are attributed to a problem and the count is recomputed from the incidents
  themselves, so a link made by any other route still produces a correct figure.
- **Deliberately not SLA-tracked.** Investigation is open-ended work whose value is in being done
  properly; a countdown would push teams to close problems rather than solve them.

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

**API surface:** 47 endpoints — 12 incident, 12 request, 6 catalogue, 2 approval, 5 auth,
7 reference data, 4 notification, 2 audit, 2 AI, plus 2 health.

### Front end

React 19 / TypeScript 5.9 / Vite 7 / MUI 7 / TanStack Query 5.

Service desk dashboard, incident queue with URL-driven filters, incident record page with
activity and audit tabs, new-incident form, My Work, service catalogue, a catalogue order form
built from each item's own field definitions, request queue, request record page with lines and
approvals, approvals queue, audit browser, global search, notification bell, AI assistant panel,
sign-in, change password. Light and dark themes.

### Infrastructure

Bicep for four environments, CI with four parallel jobs, CD with OIDC federated auth, migration
bundles, staged rollout behind a protected environment, smoke tests, and automatic rollback.

### Demo

420 incidents across 2 tenants, 18 users, 6 groups, 5 offices, 30 realistic Indian-enterprise
scenarios. Deterministic seed. Every figure computed by the real engine.

**The catalogue is not seeded.** A fresh demo starts with no catalogue items, so requests cannot
be demonstrated until some are created through the API.

---

## 3. Deliberately not built in this phase

These appear in the navigation marked **"Later"** and are not clickable. Nothing pretends to work.

The visual workflow engine, reporting and dashboard builder, settings, virtual agent, mobile
apps, inbound email, third-party integrations.

Problems and Changes have list and record pages; neither has a create form in the UI yet, so
raising one goes through the API. Every other operation on them is available in the browser.

The foundation they share — tenancy, identity, permissions, audit, notifications, attachments,
number sequences, module-agnostic `RecordRelations`, and now module-agnostic approvals — is built
and tested. `Category` carries a `Module` discriminator, and Phase 2 exercised all of it without
reworking the platform.

The SLA service was the one part that did **not** turn out to be module-agnostic — its storage
was addressed by `Module` + `RecordId`, but the service was typed against `Incident` throughout.
It has since been generalised against an `ISlaTracked` domain interface, and each module
translates its own lifecycle into a shared `SlaStatusChange` vocabulary. Requests now carry
fulfilment clocks.

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

### Functional gaps introduced by Phase 2

| Limitation | Consequence |
|---|---|
| **No catalogue editor in the UI** | Items are created, published and retired through the API only |
| **No demo seed data for the catalogue** | A fresh demo starts with an empty catalogue; items must be created before requests can be raised |
| **Approval stages are single-stage in practice** | The model supports ordered stages and the arithmetic is tested, but nothing configures more than one |

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
| Asset return took its destination status from the caller, unconstrained | A service desk manager holding only `asset.assign` could return an asset straight to Disposed — around the narrower `asset.dispose` permission, and with no disposal date recorded |

---

## 7. Next implementation phase

**Recommended: the workflow engine, then reporting.**

Seven modules now exist — incidents, requests and the catalogue, problems, changes, knowledge,
CMDB and assets. That is the condition the workflow engine was deliberately waiting on: it is the
most valuable long-term capability and the easiest to build prematurely, and it should be
designed against seven real lifecycles rather than one imagined one. The orchestration each
module actually asked for — approval routing, fulfilment hand-offs, CAB gates, review reminders,
refresh and expiry dates that need to become work — is now observable rather than guessed at.

Reporting follows it, because a workflow nobody can measure is a workflow nobody trusts.

Still to build, in the order they earn their place:

1. **Workflow engine** — a designer, a runtime, and triggers on the records above.
2. **Reporting and dashboards** beyond the service desk view.
3. **Settings and administration UI.** Categories, groups, roles, SLA definitions and calendars
   are all API-only today; a customer cannot configure their own tenant without a developer.
4. **Virtual agent** on the existing grounded AI abstraction.
5. **Integration surface** — inbound email, a mobile client, third-party connectors.

Before or alongside them, four items should be treated as prerequisites rather than backlog:

1. **Malware scanning on attachments.** The product cannot responsibly accept files from
   untrusted users without it.
2. **Retention enforcement and a data export path.** Retention is stored and not acted on. The
   first customer with a DPDP question will ask for both, plus a subject-erasure workflow.
3. **An E2E browser suite.** Seven modules of UI are past what manual verification covers.
4. **A `LICENSE` file.** The repository is public and the README asserts all rights reserved,
   which is a statement without a licence file behind it.
