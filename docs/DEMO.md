# NexaOps — Demo guide

Scripts for demonstrating what is built, and an explicit list of what is not, so you never
promise something the product does not do.

**The rule for demoing NexaOps: everything in the navigation marked "Later" is not built and is
not clickable. Do not describe it as working. It is marked in the UI precisely so nobody has to
remember.**

---

## 1. Before the demo

```bash
pwsh ./infra/scripts/reseed-demo.ps1
```

This stops the API, drops the database, rebuilds, reseeds, signs in, and **prints the resulting
queue figures** — open count, breached count, P1/P2 counts, due-soon count, workload spread. Read
those numbers before you present them. If the shape is wrong for the story you want to tell,
better to find out now than on screen.

Seeding uses a fixed random seed (`20260907`), so the data is the same every time. The *ages* of
open incidents are relative to when you seed, so reseed on the day you demo — a queue seeded a
week ago shows a wall of red.

Then:

```bash
dotnet run --project src/NexaOps.Api
```

```bash
cd src/NexaOps.Web && npm run dev
```

Open `http://localhost:5173`.

---

## 2. The demo environment

**Acme Technologies India** (`acme-in`) — 18 users, 6 assignment groups, 5 offices (Bengaluru
HQ, Pune, Hyderabad, Gurugram, Chennai), ~420 incidents over the last two weeks.

**Northwind Logistics India** — a second, smaller tenant that exists for one purpose: to
demonstrate isolation against a neighbour that genuinely has data.

**NexaOps** — the service provider's own tenant, holding one platform operator. It is what makes
the onboarding script below possible, and it is deliberately a separate tenant rather than an
operator sitting inside Acme: a customer must never see the platform's tools in their navigation.

Password for every account: `NexaOps#Demo2026`

| Sign in as | Role | Shows |
|---|---|---|
| `priya.raghavan@acmetech.example.in` | Tenant Admin + IT Manager | Everything |
| `arun.mehta@acmetech.example.in` | Service Desk Manager | Queue, overrides, audit |
| `kavya.nair@acmetech.example.in` | Service Desk Agent | Working the queue |
| `aditya.menon@acmetech.example.in` | Requester | The employee's view |
| `deepak.varma@northwind.example.in` | Other tenant | The neighbour |
| `nandini.iyer@nexaops.example.in` | Platform operator | Onboarding and managing customers |

All data is synthetic. `example.in` is a reserved domain that cannot receive mail. No real
person's data is used.

---

## 3. Script A — the service desk manager (5 minutes)

**Sign in as Arun Mehta.**

**Dashboard.** Open incidents, breached, unassigned, due soon; volume by priority; workload by
agent.

> Say: *"Every figure here is computed from the database at request time. Nothing on this screen
> is hard-coded."*

Prove it if challenged: open an incident, resolve it, come back. The counters move.

**Queue.** Filter to P1 and P2, then to breached. Note the URL changes with every filter.

> Say: *"The filter state is in the URL, so an agent can send a colleague a link to exactly the
> view they are looking at, and the back button works."*

**Open a breached incident.** Point at the SLA panel: response and resolution clocks, elapsed,
remaining, the breach.

> Say: *"These are business-hours clocks against an Indian working calendar — nine to six, Monday
> to Friday, IST, with holidays. A ticket raised at 5:30 on Friday afternoon with a four-hour
> target is due at half past twelve on Monday, not half past nine on Friday night."*

That single sentence is the most convincing thing in the demo to anyone who has run a service
desk.

**Override the priority.** The form requires a reason. Show that it will not submit without one.

> Say: *"A priority override changes what the customer is owed, so it is never anonymous. The
> reason is mandatory and the whole change goes into the audit trail."*

**Audit tab.** Show the change that was just made — actor, timestamp, before and after.

---

## 4. Script B — the agent (3 minutes)

**Sign in as Kavya Nair.**

**My Work.** Her assigned queue, ordered by what is closest to breaching.

**Open one. Move it through the lifecycle.** Add a work note, set it Pending, come back.

> Say: *"The clock is paused. It is waiting on the requester, and the customer is not charged for
> time our desk is blocked on them. When it resumes, the paused minutes are credited back."*

**Try to override a priority.** The control is not there.

> Say: *"Not hidden by a role check in the UI — she does not hold `incident.priority.override`.
> If she forced the button into view, the API would still refuse. The UI is presentation; the
> permission is enforced twice on the server."*

**Resolve it.** Resolution notes are required.

---

## 5. Script C — the requester (2 minutes)

**Sign in as Aditya Menon.**

He sees his own tickets only. The queue, the audit, the configuration are all absent.

**Open one of his own incidents.** The internal work notes an agent wrote are **not there**.

> Say: *"Work notes are filtered out in the database query, not hidden in the browser. The comment
> count is filtered too — a count that included notes he cannot read would tell him they exist."*

**Raise a new incident.** Impact and urgency; the priority is derived from the tenant's matrix,
not chosen by him.

---

## 6. Script D — tenant isolation (3 minutes)

This is the demo that closes enterprise deals. Do it live; do not describe it.

1. As Arun (Acme), copy the URL of any incident. Note the incident number.
2. Sign out. Sign in as **Deepak Varma** at Northwind.
3. Paste the Acme URL.

**404.** Not "access denied" — not found.

> Say: *"He gets a 404, not a 403, and that is deliberate. A 403 would confirm the record exists,
> which lets someone enumerate another customer's ticket numbers. A 404 is indistinguishable from
> an id that was never issued."*

4. Search for the Acme incident number. Nothing.
5. Compare the dashboard counters. Completely different data.

> Say: *"This is enforced three times independently — a query filter applied to every tenant-owned
> entity by convention, a write guard on every save, and explicit checks whenever one record
> references another. Eighteen integration tests run as a privileged user of one tenant trying to
> reach the other's data through every route we could think of."*

Then run them on screen if the room is technical:

```bash
dotnet test tests/NexaOps.Api.IntegrationTests --filter "FullyQualifiedName~TenantIsolation"
```

---

## 7. Script E — onboarding a customer (3 minutes)

The question behind this one is "how long until we are live?", and the answer is better shown
than described.

**Sign in as Nandini Iyer.** Note the **Platform** section at the bottom of the navigation.

1. Open **Platform → Customers**. Four tenants, with how many people are active in each and when
   they were onboarded.
2. Click **Onboard customer**. Fill in a code, a name, and the first administrator's name and
   email. Submit.
3. The tenant appears immediately, and a dialog shows a one-time password.

> Say: *"That created the tenant, its roles and permission grants, its priority matrix, its
> business calendars including Indian public holidays, its SLA definitions and policies, its
> record number sequences and its change advisory board. Then it created one administrator who
> can do everything else from inside the product."*

4. Sign out. Sign in as the new administrator using the one-time password.
5. They are asked to change the password before anything else.

> Say: *"That is not a formality. We generated that password, so we knew it. Forcing the change
> is what stops our copy from working."*

6. Show that their tenant is empty — no incidents, no people but themselves — then raise one
   incident and watch it get INC0000001 and a priority derived from the matrix.
7. **Look at their navigation.** There is no Platform section.

> Say: *"They hold every permission that exists inside their tenant. The platform permissions are
> not among them and cannot be granted to them from inside a tenant, so this is not a hidden menu
> — the API refuses them too."*

If the room asks about suspending a customer for non-payment, go back to Nandini and suspend one.
It asks for a reason, records it in that customer's own audit trail, signs their users out and
refuses their next sign-in.

---

## 8. Script F — the AI assistant (2 minutes)

**This behaves differently depending on whether Azure OpenAI is configured. Know which one you
are about to show.**

### Not configured (the default local setup)

Open the assistant. It states plainly that no AI provider is configured for this environment.

> Say: *"That is the intended behaviour, and it is worth a moment. There is no demo mode, no
> canned answer, no fixture. If the AI is not available, it says so. An ITSM assistant that
> invents a plausible incident number is worse than no assistant at all."*

### Configured

Ask: *"How many P1 incidents are open and who is working on them?"*

The answer cites real incident numbers, and the UI shows **which tools ran** to produce it.

> Say: *"The model cannot reach the database. It can call three registered read-only tools, each
> declaring a permission, each running the same tenant-filtered queries the REST API runs, under
> this user's own identity. Ask it as a requester and it sees only that requester's tickets."*

Demonstrate that: sign in as Aditya and ask the same question. Different answer, correctly.

> Say: *"And a tool the caller lacks permission for is never described to the model at all. It is
> not in its vocabulary, so it cannot be persuaded to call it. There are no write tools in this
> build — the assistant will tell you where in the product to make a change, and refuse to make
> it."*

---

## 9. Questions you will be asked

**"Is this a ServiceNow clone?"**
No. The data model, UX, architecture and code are original. No ServiceNow source, schema,
branding or copyrighted material is used anywhere.

**"Are these numbers real?"**
Every figure is computed from the database at request time. The data is synthetic — 420 seeded
incidents — but the counts, SLA clocks and breach figures are produced by the real engine over
that data. Resolve a ticket and watch the counters move.

**"What about compliance?"**
NexaOps provides configurable technical controls that help a customer meet their own obligations
— tenant isolation, permission-based access, an append-only audit trail, Indian data residency,
configurable retention. It carries **no certification** and makes no compliance claim. See
[INDIA-READINESS.md](INDIA-READINESS.md), which also lists the gaps honestly: retention is not
enforced, there is no export path, and there is no subject-erasure workflow.

**"Can I see it scale?"**
Not honestly. The indexes are designed for these queries and the infrastructure scales on HTTP
concurrency to 20 replicas, but nothing has been load tested. Say that.

**"When can we have change management?"**
Do not answer with a date. Say the foundation — tenancy, identity, permissions, audit, SLA,
notifications, workflow-ready record relations — is built and shared, and that Incident
Management is the first module on it. See [STATUS.md](STATUS.md) for what is next.

---

## 10. What is not built — say this out loud

Nothing in the navigation is marked "Later" any more; every entry works. An earlier version of
this page told you to say that service requests, problems, changes, knowledge, the CMDB, assets,
the workflow engine, reporting and the virtual agent were absent. They are all built. Do not read
that list out.

What genuinely is not there:

- **Mobile applications.** The web UI is responsive; there is no native client.
- **Outbound integrations** — Teams, Slack, monitoring tools. Inbound email works; outbound
  webhooks and provider connectors do not.
- **No user interface for the integration surface.** Inbound email and machine credentials are
  configured through the API only.
- **No create form for problems or changes.** Both have full list and record pages, and every
  other operation is available in the browser; raising one goes through the API. If the demo
  needs a new problem or change, create it beforehand.
- **Only incidents are seeded.** Requests, problems, changes, assets, the CMDB, knowledge and
  workflows all work but start empty in a fresh environment. Populate anything you intend to
  show before the room is watching.

Also not built, and less obvious from the UI:

- **Attachment upload works, but files are not virus-scanned.** Do not demo attachments to a
  security-conscious audience without saying so.
- **Email notifications are generated but only dispatched when Azure Communication Services is
  configured.** Locally they queue and report as not sent.
- **Entra ID sign-in is wired but the first-sign-in user provisioning path is not built.** Demo
  local sign-in.
- **No data export.**
- **English only.**

---

## 11. If something goes wrong

**Sign-in fails after several attempts** — the lockout is 5 attempts, and the sign-in rate limit
is 10 per 5 minutes per IP. Both are real security controls. Wait, or reseed.

**Figures look wrong or the queue is all red** — you seeded a while ago. Reseed.

**The API will not rebuild: file locked** — `NexaOps.Api.exe` is running. `reseed-demo.ps1` stops
it for you; that is why it exists.

**Migration error 2714, "There is already an object named…"** — the schema exists from a previous
run. Reseed; it drops the database first.
