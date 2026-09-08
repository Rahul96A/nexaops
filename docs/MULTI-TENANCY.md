# NexaOps — Multi-tenancy

Tenant isolation is the single most important guarantee a multi-tenant SaaS product makes. This
document describes how NexaOps implements it, what it deliberately does not rely on, and how the
guarantee is tested rather than asserted.

---

## 1. The hierarchy

```
Platform                 The NexaOps operator. Not a customer.
  └── Tenant             One paying customer. The isolation boundary.
        └── Organization A legal entity or business unit. Indian groups often have several.
              └── Department
                    └── User
        └── Group        Assignment, approval, notification and security groups.
```

Every business record carries a `TenantId`. There is no shared business data between tenants and
no "global" record type that spans them.

---

## 2. Model: shared database, shared schema

NexaOps uses a **shared database with a tenant discriminator column** rather than a database or
schema per tenant.

**Why.** With a few hundred tenants, database-per-tenant multiplies operational cost by the
tenant count: every migration runs N times, every backup and restore is N operations, and
connection pooling collapses. A discriminator column keeps one migration, one backup policy and
one connection pool, at the cost of making isolation a code property rather than an
infrastructure one.

**What that costs, honestly.** The isolation boundary is now something the software must get
right on every single query and every single write. That is a real risk, and it is why isolation
is enforced at three independent layers and covered by dedicated tests, rather than left to
developer discipline.

**When to change.** A customer with a regulatory requirement for physical separation, or one
large enough to warrant dedicated capacity, is served by a dedicated deployment of the same
software with its own database. The application needs no change for that; only the deployment
does.

---

## 3. Where the tenant comes from

The ambient tenant is resolved **exclusively** from the `nexaops:tid` claim on a
signature-validated access token.

```
Sign-in  →  server looks up the user  →  issues a token carrying nexaops:tid
                                              ↓
Request  →  JWT validated (signature, issuer, audience, lifetime, security stamp)
                                              ↓
TenantResolutionMiddleware reads nexaops:tid  →  opens the tenant scope
                                              ↓
Everything downstream is scoped
```

A `tenantId` in a request body, query string, route value, or header is **ignored entirely**.
There is no code path that reads one. To change tenant, an attacker would have to forge an
HMAC-SHA256 signature.

An authenticated token with no usable tenant claim is rejected with 401 rather than being
allowed through with no scope — failing closed rather than silently returning empty results.

---

## 4. Three independent layers

Isolation is enforced three times over, so that a bug in any one layer is not by itself
sufficient to leak data.

### Layer 1 — Query filters (reads)

Every entity implementing `ITenantOwned` is given an EF Core global query filter automatically,
by reflection, in `OnModelCreating`:

```csharp
e => IgnoreTenantFilter || e.TenantId == CurrentTenantId
```

This is applied **by convention, not per entity**. A developer adding a new tenant-owned entity
gets isolation for free and cannot forget it. `CurrentTenantId` is a context property, which EF
translates into a query parameter evaluated at execution time — so a background worker that
switches tenant mid-scope gets correctly re-filtered queries rather than a value captured when
the context was constructed.

When no tenant is established, `CurrentTenantId` is `Guid.Empty`, which matches no row.

> **Tested by:** `Every_tenant_owned_entity_has_a_tenant_query_filter` fails the build if the
> convention ever stops covering an entity, and
> `A_query_with_no_tenant_scope_returns_nothing_rather_than_everything`.

### Layer 2 — The write guard

`AuditAndTenantInterceptor` inspects every tracked change before `SaveChanges` and refuses any
write whose tenant does not match the ambient tenant. It catches three distinct failures:

1. An insert carrying a foreign tenant id.
2. An update to a row loaded outside a tenant scope.
3. An attempt to **move** a row between tenants by editing `TenantId`.

The third is checked against the entry's *original* value, not the current one — comparing only
the current value would let a rewrite that sets a correct-looking tenant slip through.

A violation raises `TenantIsolationViolationException`, which is logged at **Critical**, written
to the audit trail as a security event, and returned to the caller as a bare 403 with no detail.

> **Tested by:** `The_write_guard_refuses_to_persist_a_record_belonging_to_another_tenant` and
> `The_write_guard_refuses_to_move_a_record_between_tenants`, both of which bypass every
> controller and application service and write directly through the DbContext.

### Layer 3 — Explicit checks in application services

Where one aggregate references another — an incident assigned to a group, classified with a
category, or given an assignee — the application service verifies the referenced record exists
*through a tenant-filtered query* before accepting it. A reference to another tenant's group
therefore reads as a non-existent group and returns 404.

> **Tested by:** `An_incident_cannot_be_assigned_to_a_group_from_another_tenant`,
> `An_incident_cannot_be_assigned_to_a_user_from_another_tenant`,
> `An_incident_cannot_be_classified_with_another_tenants_category`.

---

## 5. Deliberate escapes from the filter

Three pieces of infrastructure genuinely need to cross tenants. Each opens an explicit,
scoped suppression rather than having the filter quietly not apply.

| Where | Why | How it stays safe |
|---|---|---|
| **Sign-in** | Runs before any tenant scope exists; the user's tenant is not yet known. | Constrained to an exact email match. Nothing is returned to the caller until a password has been verified. |
| **Tenant provisioning and demo seeding** | Creates the tenants themselves. | Only reachable from platform code, never from an HTTP request. |
| **SLA monitor** | One background pass must see every customer's clocks. | Everything it writes is stamped with the tenant of the record it derives from. |

The suppression is a **disposable scope** (`SuppressTenantFilter()`) that restores the previous
value on dispose, not a settable flag.

> That design came from a real bug found during development: the flag was a plain setter, a
> nested call in the provisioning service reset it to `false` on the way out, and the outer
> seeding scope was silently un-suppressed. The scope object makes that mistake impossible
> to write.

---

## 6. Caching

`ApplicationCache` prefixes **every** key with the ambient tenant id:

```
nexaops:{tenantId:N}:{key}
```

Callers never supply the prefix, so a cross-tenant cache collision is structurally impossible
rather than merely unlikely.

---

## 7. Blob storage

Every storage path is prefixed with the tenant id, generated server-side. A caller-supplied file
name is stripped of traversal sequences and can never escape its prefix. Reads and deletes
verify the path belongs to the ambient tenant before touching storage — the path comes from our
own database, but checking it again means a compromised or mistaken row still cannot reach
across a boundary.

---

## 8. Record visibility inside a tenant

Isolation between tenants is absolute. **Within** a tenant, visibility is a permission question:

- `incident.read.all` — sees every incident in the tenant. Agent level and above.
- `incident.read` alone — sees only incidents they raised, are affected by, are assigned, or
  that sit in one of their assignment groups.

This is applied as a **database predicate**, not a filter over already-loaded rows, so counts,
paging and aggregates are all correct and an invisible incident is indistinguishable from one
that does not exist.

Internal work notes are excluded **at the query level** for callers without
`incident.worknote.read`. The comment count is filtered too — a count that included hidden notes
would leak their existence.

---

## 9. What is tested

`tests/NexaOps.Api.IntegrationTests/TenantIsolationTests.cs` — 18 tests, all running as a fully
authenticated, highly privileged user of a *neighbouring* tenant against real records:

| Test | Proves |
|---|---|
| Cannot read another tenant's incident by id | 404, not 403 |
| Cannot read another tenant's incident by number | 404 |
| Search never returns another tenant's incidents | Zero results, correct total |
| Dashboard counters are tenant-scoped | Counts unchanged by a neighbour's activity |
| Comments not readable across tenants | 404 |
| Activity timeline not readable across tenants | 404 |
| Reference data is tenant-scoped | Disjoint group id sets |
| User picker cannot enumerate another directory | Neighbour's users absent |
| Cannot modify another tenant's incident | 404, and the record is verified untouched |
| Cannot comment on another tenant's incident | 404 |
| Cannot resolve another tenant's incident | 404 |
| Cannot assign to a foreign group or user | 404 |
| Cannot classify with a foreign category | 404 |
| Every tenant-owned entity has a query filter | Reflection over the EF model |
| Write guard refuses a foreign insert | Exception, with both tenant ids reported |
| Write guard refuses re-tenanting | Exception |
| Unscoped query returns nothing | Empty, not full |

Run them:

```bash
dotnet test tests/NexaOps.Api.IntegrationTests --filter "FullyQualifiedName~TenantIsolation"
```

---

## 10. Not-found versus forbidden

A record in another tenant is reported as **404 Not Found**, never 403 Forbidden.

The existence of a record is itself information. A 403 confirms "this id is real, you just
cannot see it", which lets an attacker enumerate another tenant's record ids. A 404 is
indistinguishable from an id that was never issued.

---

## 11. Known limitations

- **No row-level security in SQL.** Isolation is enforced in the application. A direct database
  connection with sufficient privilege bypasses it. SQL RLS as a fourth layer, and private
  endpoints so no public network path to the database exists, are both worth adding for a
  customer whose threat model includes a compromised operator credential.
- **Platform impersonation is designed but not yet implemented.** `ITenantContext` carries an
  `IsPlatformImpersonation` flag and the audit action exists, but the endpoint that lets a
  platform administrator act inside a customer tenant is not built.
- **Cross-tenant reporting for the platform operator** is not built.
