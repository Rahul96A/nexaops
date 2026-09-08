# NexaOps — Testing

**258 tests.** 220 .NET, 38 front end. All passing on the current tree.

The strategy is deliberately not "aim for a coverage percentage". It is: **test the things that
would be catastrophic or expensive to get wrong, at the cheapest layer that can prove them.**

---

## 1. The four suites

| Suite | Tests | Runs against | Takes |
|---|---|---|---|
| `NexaOps.Domain.Tests` | 113 | Nothing. Pure objects | < 1 s |
| `NexaOps.Application.Tests` | 31 | Fakes | < 1 s |
| `NexaOps.Api.IntegrationTests` | 76 | Real HTTP, real SQL Server | ~40 s |
| `NexaOps.Web` (Vitest) | 38 | jsdom | ~3 s |

### Why the shape is this way

The domain suite is the largest because the domain is where the rules that would be most
expensive to get wrong live: SLA arithmetic across business hours, holidays and time zones, and
the incident state machine. Those are pure functions of their inputs, so they can be tested
exhaustively for the price of nothing.

The integration suite is the second largest because **tenant isolation and authorization cannot be
proved with a mock.** A test that mocks the DbContext proves the mock was configured correctly.
The isolation tests run real HTTP requests, as a real authenticated user of a real neighbouring
tenant, against real rows in real SQL Server. There is no other way to make the claim honestly.

The application suite is the smallest on purpose. Most of what it could test is either a domain
rule (tested cheaper below) or an integration concern (tested truthfully above). What is left is
orchestration that is genuinely its own logic — SLA attach/pause/re-target sequencing — and the
permission catalogue.

---

## 2. Running them

```bash
dotnet test NexaOps.slnx
```

> Use `NexaOps.slnx`, not `NexaOps.sln`. This solution uses the .NET 10 XML solution format and
> there is no `.sln` file.

```bash
dotnet test tests/NexaOps.Domain.Tests
```

```bash
dotnet test tests/NexaOps.Api.IntegrationTests --filter "FullyQualifiedName~TenantIsolation"
```

```bash
cd src/NexaOps.Web && npm run test
```

The full quality gate, exactly as CI runs it:

```bash
dotnet build NexaOps.slnx && dotnet test NexaOps.slnx && cd src/NexaOps.Web && npm run typecheck && npm run lint && npm run test && npm run build
```

---

## 3. Domain tests — 113

| File | Tests | Covers |
|---|---|---|
| `Sla/BusinessScheduleTests` | 18 | Business-hours arithmetic |
| `ServiceDesk/IncidentTests` | 19 | Aggregate invariants and the state machine |
| `Sla/SlaInstanceTests` | 14 | Clock lifecycle: pause, resume, breach, re-target |
| `Localisation/IndiaReferenceTests` | 12 (+30 inline cases) | GSTIN, PAN, phone, states, holidays |
| `ServiceDesk/PriorityCalculatorTests` | 6 (+7 inline cases) | The impact × urgency matrix |

The business-schedule tests are the ones worth reading. They assert to the minute:

- A ticket raised at 17:30 on Friday with a 4-hour target is due at **12:30 on Monday**, not
  21:30 on Friday.
- Time spent outside the working window does not count.
- A holiday inside the window is skipped, and so is a holiday that lands on the deadline itself.
- Crossing an IST/UTC day boundary does not shift by 5½ hours.
- A malformed window (end before start) is skipped rather than throwing or producing a negative
  duration.
- A pathological calendar — say, one working minute a week — terminates via the
  `MaxDaysToWalk = 3660` guard rather than looping.

The last two are the ones that matter for a product that accepts customer-configured calendars.
A customer can and will configure something nonsensical, and "the SLA engine hung" is not an
acceptable outcome.

---

## 4. Application tests — 31

| File | Tests | Covers |
|---|---|---|
| `Sla/SlaServiceTests` | 17 | Attach, pause on Pending, resume with credit, re-target on priority change, first-response idempotence |
| `Security/PermissionCatalogueTests` | 14 | Every seeded role against the permission catalogue |

The permission tests are structural and are cheap insurance against a whole class of mistake:

- No role references a permission outside `Permissions.Catalogue`. A typo'd code in a role grants
  nothing silently; this makes it a build failure.
- No customer role holds a platform permission.
- `role.manage`, `user.reset_password` and `audit.read` are held only by administrators.
- An agent can work the queue but cannot reconfigure the service desk.
- A requester's permission set cannot see other people's incidents or internal work notes.

---

## 5. Integration tests — 76

Real Kestrel, real SQL Server, real JWTs. Two tenants are provisioned once and shared.

**Two tenants, not one, is the entire point.** Isolation cannot be demonstrated against an empty
neighbour: a test that finds nothing might be finding nothing because nothing exists. Northwind
has real incidents, real users, real groups and real categories, and Acme's most privileged user
still cannot reach any of it.

| File | Tests | Covers |
|---|---|---|
| `IncidentLifecycleTests` | 23 | Create → assign → respond → resolve → close, SLA attachment, validation, concurrency |
| `AuthorizationTests` | 19 (+7 inline) | Anonymous rejection, permission enforcement, visibility scoping, work-note filtering |
| `TenantIsolationTests` | 18 | Enumerated in [MULTI-TENANCY.md §9](MULTI-TENANCY.md) |
| `AiSecurityTests` | 10 | Tool gating, permission ordering, mutation refusal, unconfigured behaviour |

### What these tests actually prove

- A user of tenant A gets **404, not 403**, for tenant B's records — by id, by number, by search,
  through reference data, and through the AI assistant.
- The write guard refuses a foreign insert and refuses to re-tenant a row, tested by writing
  **directly through the DbContext**, bypassing every controller and service. That is the point:
  it proves the guard, not the callers.
- Every `ITenantOwned` entity has a query filter, by reflecting over the EF model. A new entity
  that escapes the convention fails the build.
- Seven endpoints reject anonymous callers.
- A stale row version is refused with 409 rather than overwriting a colleague's edit.
- A model asking for a tool the caller cannot use is denied and audited, and permission is checked
  **before arguments are parsed**.
- With no AI provider configured, `/ai/ask` returns 503 `ai_not_configured` — never a fabricated
  answer.

### Two things worth knowing before you run them

**They need SQL Server.** LocalDB locally, a service container in CI. The database is created,
migrated, and dropped per run.

**Rate limiting is raised in the test host.** Production allows 10 sign-in attempts per 5 minutes
per IP. The suite signs in many times from one address and was being throttled by its own
security control — a real finding, since it is exactly what a legitimate burst of activity would
hit. The fix was to extract `RateLimitOptions`, document the production defaults in
`appsettings.json`, raise them in the test host, and cache authenticated clients (`ClientForAsync`)
so the suite signs in once per user rather than once per test.

---

## 6. Front-end tests — 38

Vitest with Testing Library, querying by role and text, not by CSS class or test id — the tests
find elements the way a user or a screen reader does.

| File | Tests | Covers |
|---|---|---|
| `utils/format.test.ts` | 16 | IST conversion, Windows→IANA mapping, durations, Indian number grouping |
| `components/StatusChips.test.tsx` | 13 | Priority, status, SLA meter, user chip |
| `api/client.test.ts` | 9 | `ApiError` classification |

Three assertions that encode product decisions rather than implementation:

- An overrun renders **"2h 15m over"**, never "-135 minutes". A negative duration is not a thing a
  person reads.
- The SLA progress bar clamps at 100% while the label still reports the true overrun — the bar is
  bounded, the truth is not.
- `ApiError` classification is asserted on the **stable `code`**, never the message. Messages are
  written for people and get reworded; a screen that branches on prose breaks the next time
  someone improves the wording. `ai_not_configured` is specifically distinguished from a
  configured-but-failing provider, because they need different UI.

---

## 7. CI

`.github/workflows/ci.yml`, four parallel jobs:

| Job | Does |
|---|---|
| Backend | Restore, build with warnings as errors, run all 220 tests against a SQL Server service container |
| Frontend | `npm ci`, typecheck, lint, test, build |
| Infrastructure | `az bicep build` — **0 warnings required** |
| Security | NuGet audit, `npm audit`, gitleaks over full history |

`NuGetAudit` is on with `NuGetAuditMode=all` and `NuGetAuditLevel=moderate` in
`Directory.Build.props`, so a moderate-or-worse advisory in any package — direct or transitive —
**fails the build**, not just CI.

---

## 8. What is not tested

Stated plainly.

- **No E2E browser suite.** No Playwright, no Cypress. The user journeys were verified manually
  through a browser — sign-in, dashboard, queue filtering, record page, state transitions — but
  that verification is not automated and does not run in CI. This is the largest testing gap.
- **No load or performance testing.** The indexes are designed for the queries the product runs;
  nothing has measured them under load.
- **No mutation testing.** Coverage of the domain rules is high by inspection, not by measurement.
- **No accessibility test automation.** Components are built with roles and labels, and the tests
  query by them, but no axe run gates the build.
- **No contract tests.** There is one client and one server, in one repository, sharing generated
  types.
- **Coverage is not measured or gated.** A percentage would be easy to add and would mostly
  measure how much of `Program.cs` a test happened to execute.

---

## 9. Tests that found real bugs

The suite has earned its keep. Full detail in [SECURITY.md §7](SECURITY.md); in short:

| Found | Was |
|---|---|
| Optimistic concurrency did nothing | Silent lost updates between two agents |
| A paused SLA clock could still breach | Overstated breach rates; customers charged for time the desk was blocked |
| `PauseWhenPending` read through an unloaded navigation | A commitment configured to run continuously silently paused |
| Assigning a no-tracking entity to a navigation | PK violation on `sla.SlaDefinitions` |
| Grouping by a key through a navigation | 500 on the dashboard workload panel |
| Invalid sort field returned 409 | Wrong status; now a 400 with field errors |

One test in the AI suite was itself wrong: it asserted that malformed arguments were rejected
before the permission check. The implementation had the ordering right — permission first, then
parse — so the test was corrected and renamed
`Permission_is_checked_before_tool_arguments_are_even_parsed`. Changing the code to satisfy a
wrong test would have made the product worse.
