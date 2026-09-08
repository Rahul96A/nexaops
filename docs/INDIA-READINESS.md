# NexaOps — India readiness

NexaOps is built for Indian SMB, mid-market and enterprise customers first, not localised into
that market afterwards. This document says what that actually means in the code, and — separately
and carefully — what it means for a customer's regulatory obligations.

**Read §5 before quoting anything here to a customer.** NexaOps makes no compliance claim.

---

## 1. Locale and formatting

| Concern | Value |
|---|---|
| Default time zone | `India Standard Time` / `Asia/Kolkata` |
| Default locale | `en-IN` |
| Currency | INR, symbol ₹ |
| Date display | `dd/MM/yyyy` |
| Number grouping | Indian — lakhs and crores (`12,34,567`, not `1,234,567`) |

All four are **per-tenant settings**, not constants. A tenant in another zone changes one column.
The defaults are Indian; the design is not hard-wired to India.

### The Windows/IANA problem

.NET on Windows uses `India Standard Time`. Browsers only understand IANA identifiers
(`Asia/Kolkata`). The server stores the Windows form; `configureFormatting` maps it in the front
end, and `BusinessSchedule.ResolveTimeZone` accepts either form on the server, falling back to IST
rather than throwing.

This is a small thing that breaks a product completely when it is wrong — an SLA computed in UTC
and displayed in IST is off by five and a half hours, which for a four-hour commitment is the
difference between met and breached.

### Container globalisation

The Dockerfile sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` and installs ICU. The .NET
container images default to invariant globalisation, under which `en-IN` silently degrades to
invariant formatting — Indian digit grouping disappears and nobody notices until a customer
points at a number.

---

## 2. Business calendars

The SLA engine works in **business minutes**, against a per-tenant calendar with:

- Working windows per day of week — the seeded default is Mon–Fri 09:00–18:00 IST, which is what
  an Indian corporate service desk actually runs.
- Holidays as dates.
- A time zone.

### Fixed versus lunar holidays

`IndiaReference.FixedNationalHolidays` contains exactly three: Republic Day (26 Jan),
Independence Day (15 Aug), Gandhi Jayanti (2 Oct).

Diwali, Holi and Eid are **deliberately not in code**. They follow lunar calendars and move every
year; hard-coding next year's Diwali date would be wrong more often than right, and a wrong
holiday silently shifts every SLA deadline that crosses it. They are seeded per year onto a
tenant's calendar instead — a data problem with a data answer.

Regional holidays vary by state anyway. A Chennai office and a Gurugram office observe different
days, which is why the calendar is a per-tenant record and a tenant may have several.

---

## 3. Organizations and identifiers

An Indian group commonly operates several legal entities under one IT function. The hierarchy
supports that directly: **Tenant → Organization → Department → User**, with the tenant as the
isolation boundary and organizations as legal entities inside it.

Each organization can carry:

| Field | Validation |
|---|---|
| GSTIN | Structural: 2-digit state code checked against the real list, 10-character PAN, entity digit, literal `Z`, checksum character |
| PAN | Structural: `AAAAA9999A` with the fourth character constrained to the valid entity types |
| CIN | Stored, not validated |
| State | One of 36, each with its GST state code |

**These are format checks, not registry lookups.** A structurally valid GSTIN that was never
issued passes. Verification against the GST portal is not implemented and is not claimed.

Phone numbers are normalised to E.164 (`+91XXXXXXXXXX`), accepting the forms people actually type
— `+91 98765 43210`, `098765 43210`, `98765-43210`.

---

## 4. Data residency

The Bicep defaults `location` to **Central India**, and the paired region for geo-redundant
backups is South India. A deployment that never overrides that parameter keeps all customer data
— database, backups, blobs, logs, telemetry — inside India.

Two caveats stated rather than glossed:

- **Azure OpenAI capacity in Indian regions is limited.** A deployment that needs a model
  unavailable in Central India must either wait for capacity or accept that AI inference happens
  outside India. That is a customer decision with a residency consequence, and it should be made
  explicitly rather than discovered.
- **Application Insights** is deployed in the same region by default, but its data plane is not
  something this repository controls beyond that.

---

## 5. What NexaOps does *not* claim

This section is the important one.

**NexaOps makes no compliance claim of any kind.** It is not certified against ISO 27001,
SOC 2, or any Indian regulatory framework. It has not been independently assessed. No statement
in this repository should be read as a representation that using NexaOps makes a customer
compliant with anything.

What NexaOps provides is **configurable technical controls that a customer can use as part of
meeting their own obligations**, and evidence those controls operated. Whether that is sufficient
is a question for the customer's counsel and their auditor, not for this document.

### DPDP Act 2023 — supporting controls

The Digital Personal Data Protection Act places obligations on the customer as Data Fiduciary.
NexaOps is a processor in that relationship. The controls that help:

| Obligation area | What NexaOps provides | What it does not |
|---|---|---|
| Purpose limitation | Data is captured for service delivery only; no analytics, tracking or profiling of end users | |
| Data minimisation | Only name, work email, phone and organisational placement are stored about a person | |
| Access control | Permission-based authorization, enforced twice, with separation of duties | |
| Accountability | Append-only audit of every change, with actor, timestamp and before/after state | |
| Security safeguards | See [SECURITY.md](SECURITY.md) | No independent assessment |
| Storage limitation | `Tenant.DataRetentionDays` exists and is seeded | **Nothing enforces it. No purge job is implemented** |
| Erasure on request | | **Not implemented.** No subject-erasure workflow exists |
| Correction on request | Any record can be corrected through the API, and the correction is audited | No dedicated subject-request workflow |
| Data portability | | **Not implemented.** No export path for a departing customer or a data subject |
| Breach notification | Audit trail and telemetry support investigation and timeline reconstruction | No notification tooling; the customer's own process applies |
| Consent management | | Not applicable to workplace ITSM data, and not implemented |

The three gaps in bold — **retention enforcement, erasure, and export** — are the ones a customer
subject to DPDP will ask about, and the honest answer today is that the schema anticipates them
and the code does not implement them.

### CERT-In Directions (2022) — supporting controls

| Requirement | State |
|---|---|
| Logs retained 180 days | Production log retention is 365 days; the audit trail is retained indefinitely. **Retention within India** is satisfied by the Central India deployment |
| Clock synchronisation to NTP | Azure platform-managed. Not something the application configures |
| Incident reporting within 6 hours | A customer process. NexaOps provides the audit trail and telemetry to reconstruct a timeline; it does not report anything to anyone |
| KYC and subscriber records | Not applicable to this product category |

Again: these are controls that support a customer's obligations. NexaOps does not report incidents
to CERT-In and does not claim to satisfy the directions on a customer's behalf.

### RBI, IRDAI and sectoral guidelines

Not assessed. A regulated financial or insurance customer will have specific requirements —
commonly private network paths, customer-managed keys, physical data separation, and a right to
audit — of which NexaOps currently supports **none** out of the box. Private endpoints,
customer-managed keys and a dedicated single-tenant deployment are all achievable with the
existing architecture, and none is implemented today.

---

## 6. Demo data

The demo environment is built around two synthetic Indian organisations — Acme Technologies India
and Northwind Logistics India — with Indian names, offices in Bengaluru, Pune, Hyderabad,
Gurugram and Chennai, and 30 incident scenarios drawn from what an Indian enterprise service desk
actually sees: VPN failures at branch offices, SAP and Tally issues, biometric attendance
devices, GST portal access, leased-line outages.

Every person, organisation, email address and record in it is invented. `example.in` is a
reserved domain and cannot receive mail. No real person's data is used anywhere.

---

## 7. Gaps, in the order a customer will raise them

1. **Retention is not enforced.** The setting exists; the purge job does not.
2. **No data export.** A departing customer cannot extract their data through the product.
3. **No subject-erasure workflow.**
4. **No private endpoints or customer-managed keys** — the two things a regulated customer asks
   for first.
5. **GSTIN and PAN are format-validated only**, never verified against a registry.
6. **English only.** No Hindi or regional language UI. `en-IN` is a formatting locale, not a
   translation, and there is no i18n framework wired in.
7. **No SMS or WhatsApp notification channel**, both of which matter more in India than email for
   field and shop-floor staff.
