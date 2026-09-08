# NexaOps — Database

Azure SQL Database (SQL Server 2022 compatible). One database, one schema set, shared by every
tenant, with a discriminator column. The reasoning behind that choice is in
[MULTI-TENANCY.md](MULTI-TENANCY.md).

---

## 1. Schemas

Tables are grouped into schemas by bounded context, not by type. It keeps a 30-table database
navigable and makes a future module split obvious.

| Schema | Holds |
|---|---|
| `identity` | Tenants, organizations, departments, users, roles, permissions, groups, refresh tokens |
| `servicedesk` | Incidents, comments, tags, categories, the priority matrix, record relations |
| `sla` | Business calendars, SLA definitions, policies, and live clocks |
| `audit` | The append-only audit trail |
| `platform` | Attachments, notifications, number sequences, system settings |

---

## 2. Conventions applied to every table

These are applied in `OnModelCreating` by convention, not per entity, so a new table gets them
without anyone remembering to.

### Keys — UUID v7

`Guid.CreateVersion7()`. Globally unique like a v4 GUID, but time-ordered, so inserts land at the
end of the clustered index instead of scattering across it. Random v4 keys on SQL Server
fragment the clustered index badly at incident volumes; v7 does not, while keeping the property
that an id can be generated client-side without a round trip.

### Strings default to `nvarchar(512)`

An unbounded `nvarchar(max)` column cannot be indexed and invites unbounded input. Anything that
genuinely needs more — an incident description, an audit JSON snapshot — says so explicitly in
its own configuration.

### Decimals are `decimal(18,4)`

Never floating point. Nothing in this build stores money yet, but the convention is set before
the first column that does.

### Deletes never cascade

Every foreign key is `DeleteBehavior.Restrict`. Business records are archived, not deleted, and
a stray cascade crossing a tenant boundary would be both catastrophic and silent.

### Concurrency

Every business record carries `RowVersion` (`rowversion`). Concurrent edits are detected, not
merged — the second writer gets a 409 with code `concurrency.conflict` and is told to reload.

> This was silently broken for part of development: assigning a caller-supplied version to the
> entity property does nothing, because EF compares the version the change tracker *loaded*. The
> fix — `SetExpectedVersion` on the repository port, setting `OriginalValue` — is described in
> [SECURITY.md §7](SECURITY.md).

### Provenance

`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` are stamped by an interceptor, never by
application code. On update, `CreatedAt` and `CreatedBy` are **reverted to their original
values**, so a crafted payload bound onto a tracked entity cannot rewrite history.

### Timestamps

`DateTimeOffset`, stored as `datetimeoffset`, always UTC in the database. Conversion to IST
happens once, in the front end, from the tenant's configured time zone. Nothing in the domain
reasons about local time except `BusinessSchedule`, which is given an explicit zone.

### Archival, not deletion

`IsArchived` / `ArchivedAt` / `ArchivedBy`. There is no hard-delete path for a business record
anywhere in the application.

---

## 3. Core tables

### `identity`

| Table | Notes |
|---|---|
| `Tenants` | The isolation boundary. Unique `Code`. Carries time zone, locale, currency, retention days. Not itself tenant-owned. |
| `Organizations` | Legal entities inside a tenant. Indian customers commonly have several. Holds GSTIN, PAN, CIN, state code. |
| `Departments` | Unique per tenant by code; indexed by organization. |
| `Users` | Unique per tenant by email. Also indexed by bare `Email` (sign-in runs before a tenant is known) and by `ExternalObjectId` for Entra mapping. |
| `Roles` | Unique per tenant by code. `IsSystemRole` marks the 12 seeded ones. |
| `RolePermissions` | Permission **codes**, not an enum table. A permission is a string constant validated against `Permissions.Catalogue`. |
| `UserRoles` | Many-to-many, unique on the pair. |
| `Groups` | Assignment, approval, notification, security. Indexed by `(TenantId, Type)`. |
| `GroupMembers` | Unique on the pair; indexed by user for "my groups". |
| `RefreshTokens` | Stores a SHA-256 **hash**, never the token. Unique on hash; indexed by `(UserId, SessionId)` for chain revocation and by `ExpiresAt` for cleanup. |

Permission codes are strings rather than a lookup table on purpose: a permission is a fact about
the *code*, not about the data. A row in a table cannot make `incident.assign` exist if nothing
checks for it, and a code with no row would silently grant nothing. One source of truth,
`Permissions.Catalogue`, with a test asserting no role references a code outside it.

### `servicedesk`

`Incidents` is the busiest table and carries the most indexes:

| Index | Serves |
|---|---|
| `(TenantId, Number)` unique | Direct lookup by INC number |
| `(TenantId, Status, Priority, CreatedAt)` | The default queue ordering |
| `(TenantId, AssignedToUserId, Status)` | "My work" |
| `(TenantId, AssignmentGroupId, Status)` | Group queues |
| `(TenantId, RequesterId, Status)` | A requester's own tickets |
| `(TenantId, NextSlaDueAt)` | "Due soon" and the SLA monitor |
| `(TenantId, HasBreachedSla, Status)` | The breach counter |
| `(TenantId, CategoryId)` | Category reporting |
| `(TenantId, CreatedAt)` | Volume over time |

Every one leads with `TenantId`, because every query is tenant-filtered. An index that did not
would be unusable.

`NextSlaDueAt` and `HasBreachedSla` are **denormalised onto the incident** by the SLA service.
Without them, the queue page would join and aggregate `SlaInstances` on every row of every page,
which is the difference between a queue that loads instantly and one that does not.

`IncidentComments` is indexed `(IncidentId, Kind, CreatedAt)` — `Kind` is in the middle because
work notes are filtered out at the query level for callers without `incident.worknote.read`, so
the filter and the ordering are served by one index.

`RecordRelations` is a module-agnostic edge table (`SourceModule`, `SourceId`, `TargetModule`,
`TargetId`, `RelationType`), unique across the whole tuple. It exists now so that problems,
changes and requests can link to incidents in later phases without a schema migration per pair.

### `sla`

`BusinessCalendars` → `BusinessCalendarWindows` (day-of-week + start/end minute) and
`BusinessCalendarHolidays` (date). The domain reads these into a `BusinessSchedule` value object
and does all arithmetic in memory — no SLA calculation happens in SQL.

`SlaInstances` is the live clock. It **copies** `DurationMinutes`, `WarningThresholdPercent`,
`PauseWhenPending` and `BusinessCalendarId` from the definition at attach time rather than
reaching through a navigation. Two reasons, both learned the hard way:

1. A commitment made under yesterday's policy must not silently change when the policy is edited.
2. Reading through an unloaded navigation returns a default, not the configured value — that is
   how `PauseWhenPending` was silently wrong for a while.

Indexed `(State, DueAt)` for the monitor — deliberately **not** tenant-leading, because that one
background pass is the single query in the system that crosses tenants on purpose.

### `audit`

`AuditEvents` — append-only. Indexed by `(TenantId, OccurredAt)`, `(TenantId, EntityType,
EntityId)`, `(TenantId, ActorUserId, OccurredAt)`, and `CorrelationId`.

`BeforeState` and `AfterState` are JSON columns with redaction applied. `ChangedFields` is a
JSON array of property names, so a diff view does not have to parse both snapshots.

Nothing in the codebase updates or deletes a row here. That is the mechanism; there is no
trigger and no `DENY` grant, so a sufficiently privileged database connection can still modify
it. See [SECURITY.md §8](SECURITY.md).

### `platform`

| Table | Notes |
|---|---|
| `Attachments` | Metadata only. Bytes live in blob storage under a tenant-prefixed, server-generated path. Carries SHA-256 hash and scan status. |
| `Notifications` | Indexed `(TenantId, RecipientUserId, IsRead, CreatedAt)` for the bell, and `(EmailRequested, EmailSentAt)` for the dispatch worker's outbox scan. |
| `NumberSequences` | Per-tenant counters behind `INC0000042`. Allocated inside the caller's transaction under an update lock, so numbers are gapless and collision-free. Never reused. |
| `SystemSettings` | Per-tenant key/value configuration. |

---

## 4. Record numbers

`INC` + 7-digit zero-padded value, per tenant. Padding is what makes the number sort correctly as
a string, which matters because it is displayed and searched as text.

Allocation takes an update lock inside the caller's transaction rather than using a SQL
`SEQUENCE`, because a sequence is global and these must be per tenant, and because a rolled-back
incident creation must not consume a number.

---

## 5. Migrations

One migration, `InitialSchema`. Applied automatically on startup in Development only, gated by
`Database:MigrateOnStartup`. In deployed environments CI produces a **migration bundle** — a
self-contained executable — that runs as its own pipeline step before the new revision goes live.
The application never migrates a production database as a side effect of starting.

```bash
# add a migration
dotnet ef migrations add <Name> \
  --project src/NexaOps.Infrastructure \
  --startup-project src/NexaOps.Api \
  --output-dir Persistence/Migrations

# what would it do?
dotnet ef migrations script --idempotent \
  --project src/NexaOps.Infrastructure --startup-project src/NexaOps.Api
```

> If you regenerate the initial migration against a database that already has the schema, you
> will get `Error 2714: There is already an object named 'Attachments'`. Drop the database first:
> `pwsh ./infra/scripts/reseed-demo.ps1`.

---

## 6. Query filters

Every entity implementing `ITenantOwned` gets a global query filter by reflection:

```csharp
e => IgnoreTenantFilter || e.TenantId == CurrentTenantId
```

`CurrentTenantId` is a context *property*, so EF renders it as a parameter evaluated at execution
time. A background worker that switches tenant mid-scope gets correctly re-filtered queries
rather than a value frozen when the context was built.

Escaping the filter requires a disposable `SuppressTenantFilter()` scope. Three places do it,
all listed in [MULTI-TENANCY.md §5](MULTI-TENANCY.md).

---

## 7. Performance notes

- **`AsNoTracking` on every read path.** Query services return DTOs; only the write path tracks.
- **Never assign a no-tracking entity to a navigation property.** EF treats it as new and tries to
  insert it. That produced a primary-key violation on `sla.SlaDefinitions` during development,
  which is why the clock copies `BusinessCalendarId` instead of holding the definition.
- **Group by a key, then join for names.** `GroupBy` with an anonymous key that reaches through a
  navigation does not translate and throws at runtime. The workload query groups by
  `AssignedToUserId` and joins display names afterwards.
- **Paging is keyset-friendly but currently offset-based.** Sort fields are allow-listed; an
  unrecognised field is a 400, never interpolated into SQL.
- **Seeding writes in batches** of 20 incidents with a `ChangeTracker.Clear()` between them, with
  audit capture suppressed. Without that, seeding 420 incidents generated enough audit rows to hit
  the command timeout.

---

## 8. Retention

`Tenant.DataRetentionDays` exists and is seeded. **Nothing enforces it yet** — no purge job is
implemented. Audit retention in a deployed environment is currently whatever the database
retains, which is indefinite. This is called out as a gap rather than presented as a feature; a
customer with a defined retention obligation needs the purge worker built.
