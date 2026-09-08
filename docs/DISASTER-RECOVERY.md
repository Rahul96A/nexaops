# NexaOps — Disaster recovery

An honest document. It states what the current infrastructure actually gives you, what it does
not, and which runbooks have been rehearsed versus written down and never run.

---

## 1. Objectives

These are the objectives the **production** configuration supports. Lower environments are not
covered by any objective; their loss is an inconvenience.

| Scenario | RPO | RTO | Basis |
|---|---|---|---|
| Bad deployment | 0 | **< 5 min** | Container Apps revision rollback, automated in the pipeline |
| Accidental data change by a user | 0 | Case by case | Audit trail identifies it; correction is manual |
| Database corruption or bad migration | **≤ 10 min** | **< 1 hour** | Point-in-time restore, 35-day window |
| Loss of a single availability zone | 0 | **< 5 min** | Zone-redundant SQL and Container Apps; automatic |
| Loss of the Central India region | **≤ 1 hour** | **8–12 hours** | Geo-redundant backup restore into a new region |
| Accidental resource deletion | 0 | 1–2 hours | Redeploy from Bicep, restore data |
| Ransomware / malicious operator | ≤ 10 min | 4–8 hours | Immutable backups, PITR to before the event |

**The regional RTO is 8–12 hours and that is the honest number.** It is not minutes, because
there is no standby region. Achieving a low-hours regional RTO requires an active geo-replica,
which is a real cost decision a customer should make deliberately rather than assume.

---

## 2. What protects the data

### Azure SQL

| Control | Production | Lower environments |
|---|---|---|
| Automated backups | Full weekly, differential ~12-hourly, transaction log every 5–10 min | Same |
| Point-in-time restore | **35 days** | 7 days |
| Backup storage redundancy | **Geo** (paired region, Chennai) | Local |
| Long-term retention | Weekly 4 weeks, monthly 12 months, **yearly 7 years** | Off |
| Zone redundancy | Yes (Business Critical) | Off below staging |

The transaction-log cadence is what sets the ≤ 10-minute RPO. Nothing in the application affects
it.

Seven-year yearly retention is chosen because Indian corporate record-keeping obligations
commonly run to eight years under the Companies Act. This is a **configurable technical control
that helps a customer meet their own obligations** — it is not a compliance claim, and a customer
with a specific retention requirement should set the policy their counsel specifies.

### Blob storage

Soft delete is enabled on blobs and containers. Versioning retains prior versions of an
overwritten blob. Attachments are immutable in practice — nothing in the application overwrites
one — so the realistic loss mode is deletion, which soft delete covers.

**Cross-region replication is not configured.** In a regional loss, attachment *metadata* is
recoverable from the geo-restored database but the *bytes* are not. This is the largest gap in
the current DR posture and is stated plainly rather than buried.

### Key Vault

Soft delete with a 90-day retention and **purge protection enabled**, so a deleted vault cannot
be permanently destroyed inside the window even by an owner. This is what makes the "malicious
operator" row above achievable.

### Container images

Retained in the registry. Every image is tagged with its commit SHA, so any prior revision can be
redeployed by digest without rebuilding.

### Configuration

All infrastructure is in Bicep, in the repository. A resource group can be rebuilt from source.
The only thing not in source is the JWT signing key, which lives in Key Vault — and if it is
lost, the consequence is that every issued token becomes invalid and every user signs in again.
Inconvenient, not destructive.

---

## 3. Runbooks

### 3.1 Bad deployment — rehearsed

The pipeline does this automatically when a smoke test fails. Manually:

```bash
az containerapp revision list --name ca-nexaops-prod-api --resource-group rg-nexaops-prod --output table
```

```bash
az containerapp ingress traffic set --name ca-nexaops-prod-api --resource-group rg-nexaops-prod --revision-weight <previous-revision>=100
```

Traffic moves in seconds.

**The caveat that matters:** this reverts *code*, not *schema*. If the bad deployment included a
migration, rolling back the image leaves the new schema in place. That is why every migration
must be backward compatible with the previously running revision — see
[AZURE-DEPLOYMENT.md §5](AZURE-DEPLOYMENT.md). A migration that breaks the old revision cannot be
rolled back this way and needs a forward fix.

### 3.2 Point-in-time restore — rehearsed against a test database

Restore to a **new** database. Never restore over the live one; you lose the ability to compare.

```bash
az sql db restore --dest-name sqldb-nexaops-restored --name sqldb-nexaops --resource-group rg-nexaops-prod --server sql-nexaops-prod-xxxxxx --time "2026-09-07T14:30:00Z"
```

Then:

1. Grant the SQL admin group access to the restored database and verify the data is what you
   expect — check row counts and spot-check the affected records.
2. Decide: swap wholesale, or extract specific rows.
3. To swap: stop traffic (scale the Container App to zero), rename the live database aside,
   rename the restored database into place, restart, re-grant the managed identity's database
   roles (they do not follow a rename).
4. **Re-grant the managed identity.** This step is easy to forget and the application will fail
   to connect until it is done.

Expect 20–40 minutes for a database of this size, plus verification.

### 3.3 Zone failure — automatic, not rehearsed

Business Critical SQL and the Container Apps environment are zone-redundant in staging and
production. Failover is automatic and requires no action. Confirm afterwards that replica count
recovered.

### 3.4 Regional loss — **written, never rehearsed**

This is the runbook to be honest about. It has not been executed end to end.

1. Create a resource group in the paired region (South India).
2. Deploy the Bicep with `location` overridden. Roughly 20–30 minutes.
3. **Geo-restore the database.** This is the long pole — hours for a database of production size.
   ```bash
   az sql db restore --dest-name sqldb-nexaops --resource-group rg-nexaops-dr --server sql-nexaops-dr-xxxxxx --geo-backup-source-id <source-db-resource-id>
   ```
4. Re-create the SQL admin group assignment and the managed identity's database roles.
5. Push the current image to the new registry, or point the Container App at the original
   registry if it is still reachable.
6. Restore the JWT signing key into the new Key Vault. **If it is not the same key, every user is
   signed out** — acceptable in a regional disaster, but tell them.
7. Update DNS. With Front Door, change the origin; otherwise repoint the record and wait out TTL.
8. Accept that attachment *bytes* are unavailable. Metadata will be present and downloads will
   fail. Communicate this before users find it.

Realistic total: **8–12 hours**, dominated by the geo-restore.

### 3.5 Accidental data change

The audit trail records who changed what, when, and both before and after states. Find it:

```
GET /api/v1/audit?entityType=Incident&entityId=<id>
```

There is **no automated revert**. The before-state is JSON in the audit row; correcting the
record is a manual edit through the normal API, which is itself audited. That is deliberate — an
automated revert would be a second write path around the domain's invariants.

### 3.6 Tenant-level restore — **not supported**

Restoring one tenant's data without disturbing others is not possible with the current design.
A point-in-time restore is database-wide. Extracting a single tenant's rows from a restored copy
and merging them back is technically possible but has no tooling and no tested procedure.

This is the honest cost of the shared-database model. A customer for whom per-tenant restore is a
hard requirement should be on a dedicated deployment — the application supports that without
change; only the deployment differs.

---

## 4. Backup verification

**This is not automated and it should be.**

Restore-testing is currently a manual quarterly exercise: geo-restore into a scratch resource
group, run the integration suite against it, confirm row counts, tear it down.

An untested backup is a hypothesis. Automating this — a scheduled pipeline that restores the
most recent geo-backup, asserts against it, and deletes it — is the single highest-value
improvement to this document, and it is listed in §6 rather than described as if it exists.

---

## 5. Communication

There is no status page and no automated customer notification. In an incident, communication is
manual and out of band.

Internally: the Application Insights availability test and the health-endpoint alerts fire into
the configured action group. See [OBSERVABILITY.md](OBSERVABILITY.md).

---

## 6. Gaps, in priority order

1. **Automated restore verification.** Quarterly manual testing is better than nothing and worse
   than a pipeline.
2. **Blob cross-region replication.** Without it, a regional disaster loses every attachment.
3. **The regional runbook has never been executed.** A written runbook that has not been run is a
   plan, not a capability.
4. **No per-tenant restore.** Inherent to the shared-database model; needs tooling or a dedicated
   deployment.
5. **No standby region.** The 8–12 hour RTO is a direct consequence. An active geo-replica would
   bring it to under an hour at roughly double the SQL cost.
6. **No status page or automated customer communication.**
7. **No documented data-export path for a departing customer.** Contractually likely to be
   required; not built.
