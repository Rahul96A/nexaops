# NexaOps — Status

**As of 8 September 2026.** Phase 1 (platform foundation), Incident Management, Phase 2
(Service Requests, Service Catalogue and Approvals) Phase 3 (Problem Management), Phase 4
(Change Management and the CAB) Phase 5 (Knowledge Base), Phase 6 (CMDB),
Phase 7 (Asset Management), Phase 8 (workflow automation), Phase 9 (reporting) and
Phase 10 (tenant administration) and Phase 11 (the virtual agent), plus a demo
environment.

This document is written to be handed to someone who has to decide whether to rely on this. It
lists what works, what does not, and what is deliberately absent — with the gaps in the same
detail as the achievements.

---

## 1. Quality gates — all passing

| Gate | Result |
|---|---|
| `dotnet build NexaOps.slnx -warnaserror` | **0 warnings, 0 errors** |
| `dotnet test NexaOps.slnx` | **642 passing** |
| `npm run typecheck` | Clean |
| `npm run lint` | Clean |
| `npm run test` | **38 passing** |
| `npm run build` | Succeeds — ~129 KB gzipped entry, routes code-split |
| `az bicep build --file infra/bicep/main.bicep` | **0 warnings** |
| NuGet audit (`NuGetAuditMode=all`, moderate) | No advisories |
| `npm audit` | No advisories |
| gitleaks (full history) | No secrets |

680 tests total (344 domain, 69 application, 229 integration, 38 front end). Breakdown and
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

### Workflow automation — complete

- **A rule list, not a flowchart.** A canvas designer is the feature customers ask for and the
  feature nobody can debug at three in the morning. A rule is a trigger, an AND-list of
  conditions and an ordered list of actions — readable in the shape it is stored in, which is
  what makes the run history explainable afterwards.
- **Conditions combine with AND, always.** There is no OR and no nesting. Both are easy to store
  and hard to read back, and a rule whose behaviour cannot be predicted by reading it gets
  switched off after the first surprise. Two rules express an OR perfectly well.
- **Every action is backed by a service that already exists.** There is no "run a script" and no
  "call a webhook": both would make the engine look more capable than it is, and both need a
  security model — sandboxing, egress control, secret handling — that has not been built.
- **A rule can raise priority and never lower it.** Raising is recoverable and is the direction
  every real escalation rule needs; a rule that can de-prioritise can quietly bury somebody's
  outage. Priority set this way is marked as an override with the reason stated, so it stays
  visible in reporting rather than looking like the matrix decided it.
- **Automation does not trigger automation.** An action that changes a record raises the trigger
  that change corresponds to, exactly as a person doing the same thing would — and the engine's
  re-entrancy guard then refuses it and records why. Raising it and refusing it, rather than
  never raising it, is what makes the guarantee testable and what tells an administrator whose
  rule sits on `PriorityChanged` that another rule's escalation will not fire it.
- **A failing rule cannot fail the user's action.** The engine runs after the record is
  committed, catches per action, and continues. One bad rule must not stop a service desk from
  logging incidents; the failure belongs in the run history, not in a 500.
- **Runs are recorded even when nothing happened.** "Why did my rule not fire" is the question
  people actually have about automation, and a log of successes cannot answer it. A skipped run
  names the condition that stopped it and what the record held instead.
- **A partly-successful run reports itself as partly successful**, not as either extreme.
  "Succeeded" would hide a failure somebody needs to see; "failed" would suggest nothing happened
  when the record was in fact rerouted.
- **The engine mutates records through a module-agnostic contract** (`IWorkflowTarget`), the same
  shape as the SLA engine's. Each module publishes an explicit list of facts a rule may test
  rather than exposing its properties by reflection — and a test checks that list against what
  the editor offers, because a field offered but not published is a rule that looks configured
  and silently never matches.
- **Changes made by a rule are audited to the rule**, not to whoever's action triggered it. An
  audit trail saying an agent reassigned a ticket they never touched is worse than no entry.
- Actions carry **no foreign keys to users or groups**. A rule pointing at somebody who has left
  should fail loudly in the run history, not block that person from being deactivated.
- Reading rules and their history is a **wider permission than writing them**: an agent whose
  ticket rerouted itself needs to be able to find out why.

**Not built:** scheduled rules (nothing fires on a timer — every trigger is a record changing),
approval-outcome and SLA-breach triggers, actions that write arbitrary fields or work notes, and
any outbound integration. Rules run only against incidents, requests, problems and changes; the
other modules do not raise triggers, and the field list for them is empty rather than
misleadingly populated.

### Reporting — complete

- **Every figure is computed from the records, on request.** Nothing is cached, pre-aggregated or
  estimated. A report that disagrees with the list view behind it is worse than no report, and a
  nightly roll-up is a second source of truth waiting to drift.
- **A rate with no denominator is absent, not zero.** A month in which nothing was raised has no
  breach rate; showing 0% would read as a perfect month. The same applies to resolution time:
  where nothing was resolved there is no average, which is not an average of zero.
- **Every rate carries its denominator.** "94% of 17" and "94% of 1,700" are different facts, and
  a figure shown alone invites the smaller to be read as the larger.
- **Mean and median together, never one alone.** The mean of ticket durations is dominated by the
  handful nobody closed; the median hides a tail that is somebody's whole month. Where the two
  disagree sharply, the disagreement is the finding.
- **A period ending today says so.** Its last day is partial, and a chart comparing it against
  complete days understates the final point unless the reader is told.
- **Every day in the window appears, including empty ones.** A chart that omits quiet days
  compresses a quiet week into a busy-looking line.
- **SLA attainment counts only clocks that finished.** One still running is not evidence either
  way; one cancelled because the record was cancelled was neither met nor missed. Both are
  reported separately rather than folded into the number.
- **Change success excludes "successful with issues".** An overrun or an unplanned side effect is
  exactly what a change process exists to reduce, and folding it into the headline rate would
  hide the thing being measured.
- **Change outcomes are counted at review, not at implementation.** An implemented change nobody
  has reviewed has no outcome; counting it as successful would be an assumption presented as a
  measurement.
- **Backlog is reported as "open now", never as "open on a past date".** No status history is
  stored, so a historic backlog cannot be reconstructed and is not claimed.
- **A window longer than a year is refused.** Not a licensing limit: the daily series is a row per
  day and the breakdowns scan the period, so an unbounded range is a way for one request to hold
  the database for minutes. A period ending in the future is clamped rather than refused, because
  "this month" run on the eighth is a reasonable thing to ask.
- **Exporting is a separate permission from viewing, and is audited.** A figure on a screen stays
  inside the application; a file leaves with whoever downloaded it.
- **CSV exports neutralise spreadsheet formulas.** A ticket title is written by whoever raised it
  and read by somebody who trusts the export — `=cmd|...` in a title is a real path from "anyone
  can raise a ticket" to code on a manager's laptop. Cells that would be interpreted as formulas
  are prefixed so the spreadsheet treats them as text, and everything is written in invariant
  culture with a UTF-8 BOM so the file opens the same way wherever it is opened.
- The trend chart is **hand-drawn SVG rather than a charting library**: one chart does not justify
  four hundred kilobytes of dependency, and the same numbers are exposed as a table to assistive
  technology.

**Not built:** scheduled or emailed reports, a custom report builder, per-agent performance
reporting, and any figure that would need status history (time in each state, backlog as at a
past date). Reports read the whole tenant — there is no row-level scoping beyond an optional
assignment-group filter, so `report.view` is a permission to see the tenant's aggregate position.

### Tenant administration — complete

This is the module that decides whether a customer can run NexaOps without a developer. Before
it, categories, groups, roles and the directory were all API-only.

- **Revoking access takes effect on the next request, not at token expiry.** Every access token
  carries a security stamp that the API compares on each call; changing somebody's roles,
  disabling their account or changing their sign-in address rotates it.
- **Granting a role is a different permission from editing a person.** `user.manage` maintains
  job titles and phone numbers; `role.manage` decides what somebody can do. A service desk
  manager holds neither.
- **A tenant cannot award itself platform permissions.** They are refused on the way in, filtered
  on the way out, and withheld from the permission catalogue the editor renders — a tenant
  administrator is on the wrong side of that boundary and there is no point advertising it.
- **An unknown permission code is refused rather than stored.** Stored, it would be a grant that
  matches nothing: access apparently given and none actually given.
- **Built-in roles cannot be edited or deleted.** The seeder maintains them, so an edit would be
  silently reverted on the next provisioning run. Refusing is better than accepting and losing it.
- **A role somebody holds cannot be deleted**, and neither can a category records classify to.
  Both would strip something from records that depend on it, and the audit trail would record the
  wrong event — a role deletion rather than the access change each person experienced.
  Deactivation is offered instead, and the refusal message says so.
- **A category cannot be moved between modules.** Records already classified there would be left
  under a taxonomy that no longer claims them: visible in a list, unreachable from any filter.
- **An administrator cannot disable their own account.** Recoverable only by somebody else, and
  in a tenant with one administrator there may be nobody else.
- **A new account's password is shown once and never stored in the clear.** It is generated from
  a cryptographic source, and under federated authentication none is issued at all — minting a
  local password in an Entra tenant would create a second, unmanaged way in.
- Disabled people **stay listed in their groups** rather than vanishing: a team that still
  formally contains somebody who has left is a fact worth seeing.

**Not built:** organisations and departments, SLA definitions, business calendars and holidays,
system settings, and subcategory editing — all still API-only. Administrator-initiated password
resets are not built either: the flow needs email delivery and a token, and half of one would be
worse than none.

### Virtual agent — complete

An employee-facing agent, distinct from the staff assistant and not a wrapper around it. The
assistant answers questions for people working a queue; this talks to somebody who has a problem,
so it leads with published guidance and ends — when nothing else helped — with an offer to raise
a ticket.

- **The agent proposes; the person disposes.** It cannot create, change or close anything. A
  proposal is a filled-in form returned in the response, editable before it is accepted, and the
  ticket exists only once somebody presses the button. The architecture anticipated this — the
  tool executor has always refused mutating tools — and this is the first module to use it.
- **There is no server-side proposal store.** The confirmation carries the fields as shown on
  screen, so an edited title is the title that gets filed and nothing stale can be resurrected.
- **The confirmation runs without AI at all.** It calls the ordinary incident service as the
  signed-in user, so their permissions, their tenant, the priority matrix, the SLA clocks and the
  audit trail all apply. The agent is a different way in, not a different set of rules.
- **A malformed proposal is no proposal.** The parser is forgiving in exactly one direction:
  truncated JSON, a missing title, a non-object — all yield the reply and nothing else. A wrong
  ticket raised on somebody's behalf is worse than no offer to raise one.
- **An unrecognised urgency reads as Medium, never as the worst case.** A model writing "urgent"
  must not thereby put somebody ahead of everyone else in the queue. `Enum.IsDefined` is checked
  after parsing because `Enum.TryParse` accepts numeric strings — `"9"` would otherwise become an
  urgency no member has.
- **Citations come from tool results, not from the model's prose.** An article number the model
  invented cannot become a link.
- **Its own permission, held at baseline.** `ai.agent.use` is not `ai.assistant.use`: the
  assistant reads across the queue and stays closed to employees, while the agent reads published
  guidance and the caller's own records — exactly what they can already see. Sharing one
  permission would have forced a choice between withholding self-service and handing employees
  the staff tooling.
- **Agent-raised tickets are audited as AI actions**, so somebody reviewing how a ticket came to
  exist can see it began as a suggestion a person accepted.
- Three new read-only tools — published knowledge, the caller's own requests, the catalogue.
  "Mine" is resolved from the authenticated identity, never from an argument, so there is no
  parameter for a model to hallucinate a user id into.

**Not built:** the agent cannot raise a service request from the catalogue (it points at the item
instead), cannot chase or update an existing ticket, and has no voice or third-party chat channel.
Conversation history lives in the browser for the length of the session and is not stored.

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
| An agent-raised ticket's audit entry was never committed | `IAuditService.Record` queues into the current unit of work, and the incident's own save had already happened — so the entry was written to nothing and "traceable to the agent" was untrue |
| `Enum.TryParse` accepted numeric strings from the model | `"9"` parsed to an Urgency no member has, which would have travelled into an incident and out to every view that switches on it. Also fixed in the shared AI tool-argument helper |
| The security stamp was cached for 60s and never evicted | Revoking a role, disabling an account or changing a password took up to a minute to bite, while the code claimed "immediately". The cache also used the tenant-prefixed key while validation runs before a tenant scope exists, so a naive eviction would have looked right and done nothing |
| Deleting a role with permissions threw | The tracked grants were orphaned rather than cascaded; the delete returned 500 |
| The workflow recursion guard guarded a path nothing reached | A rule's own action never re-entered the engine, so "automation does not trigger automation" was an untested claim. Actions now raise their corresponding trigger, which the guard refuses and records |
| Asset return took its destination status from the caller, unconstrained | A service desk manager holding only `asset.assign` could return an asset straight to Disposed — around the narrower `asset.dispose` permission, and with no disposal date recorded |

---

## 7. Next implementation phase

**Recommended: finishing the administration surface, then the integration surface.**

Eleven modules exist and a tenant can now be configured through the product for the things that
matter most — people, roles, teams and the taxonomy. What is left of administration is narrower
but still forces a developer into the loop: SLA definitions, business calendars and holidays, and
system settings.

Still to build, in the order they earn their place:

1. **The rest of administration** — SLA definitions, calendars and holidays, system settings,
   organisations and departments, subcategory editing.
2. **Integration surface** — inbound email, a mobile client, third-party connectors.
4. **Scheduled and outcome-driven workflow triggers** — a timer, an approval outcome, an SLA
   breach. The engine's shape supports them; the triggers are not raised yet.
5. **Scheduled and emailed reports**, and a report builder.

Before or alongside them, four items should be treated as prerequisites rather than backlog:

1. **Malware scanning on attachments.** The product cannot responsibly accept files from
   untrusted users without it.
2. **Retention enforcement and a data export path.** Retention is stored and not acted on. The
   first customer with a DPDP question will ask for both, plus a subject-erasure workflow.
3. **An E2E browser suite.** Seven modules of UI are past what manual verification covers.
4. **A `LICENSE` file.** The repository is public and the README asserts all rights reserved,
   which is a statement without a licence file behind it.
