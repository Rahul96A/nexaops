# NexaOps — Security

What is implemented, how it is enforced, and what is deliberately not claimed.

---

## 1. Authentication

Two modes, selected by `Auth:Mode`. Both converge on an identical `ClaimsPrincipal`, so every
authorization decision in the codebase is written once and works under either.

### Local (first-party JWT)

Used for demos, pilots, and customers without Entra ID. Fully implemented.

| Control | Implementation |
|---|---|
| Password storage | PBKDF2, HMAC-SHA512, 210,000 iterations (ASP.NET Core v3 format) |
| Password minimum | 12 characters, configurable |
| Lockout | 5 failed attempts, 15-minute lockout, both configurable |
| Access token | HMAC-SHA256 JWT, 30-minute lifetime, 30-second clock skew |
| Refresh token | 256 bits of cryptographic randomness, stored only as a SHA-256 hash |
| Rotation | Single-use. Every refresh issues a new token and revokes the old one |
| Theft detection | Presenting an already-rotated token revokes the **entire session chain** |
| Signing key | Never defaulted. The API refuses to start without one |

The signing key is deliberately not defaulted, not generated at startup, and not committed. A
defaulted key would let anyone who reads the repository mint a valid token for any tenant.

### Entra ID

Designed and wired; validated against Entra's own metadata. MFA and Conditional Access are
enforced by Entra and deliberately not re-implemented. The `oid` and `tid` claims are mapped to
a NexaOps user and tenant at first sign-in.

> **Not yet built:** the just-in-time user provisioning path for a first Entra sign-in, and SCIM.
> The token validation path is complete; the user-mapping path is not.

### Account enumeration

Every sign-in failure — unknown email, wrong password, disabled account, locked account,
federated-only account — returns **the same 401 with the same message**. The specific reason is
recorded server-side in the audit trail and the log.

### The security stamp

Every user carries a `SecurityStamp`, rotated whenever credentials or role assignments change —
a password change, a role granted or revoked, a sign-in address changed, an account disabled.
The stamp is embedded in each access token and compared on every request.

Without it, a token minted before a role was revoked would keep working for its full 30-minute
lifetime. With it, revocation takes effect on the caller's very next request.

The verified stamp is cached for a minute to avoid a database read per request, and **every
rotation site evicts that entry** — which is what keeps "immediately" true rather than "within a
minute". That cache is deliberately separate from the general application cache: the general one
prefixes keys by tenant, and stamp validation runs before a tenant scope exists, so an eviction
written the obvious way would have looked correct and evicted a different key. See
[STATUS.md §6](STATUS.md) — this was found by a test, not by review.

### Machine credentials

Integrations authenticate with a key rather than a person's token. The key is an authentication
scheme, not a bypass: it produces the same shape of principal, so the tenant middleware, the
permission attributes, `ICurrentUser`, the audit interceptor and the query filters all apply
without knowing a machine is calling.

| Property | Decision |
|---|---|
| Permissions | None of its own. It names a service account and holds exactly that account's roles, read fresh per call — so revoking a role, or disabling the account, cuts the key off at once |
| Scope | Checked separately from permissions. A key issued for inbound email cannot be pointed elsewhere even if its account could go there |
| Storage | SHA-256 of a 256-bit random value. Not recoverable; a lost key is replaced. A plain hash rather than a slow KDF is deliberate — there is no dictionary to run against 256 bits of randomness, and this verifies on every call |
| Presentation | A header, never a query string. Query strings reach access logs, proxy logs and browser history |
| Recognisability | An `nxk_` prefix, so secret scanners can spot a leaked key in a public repository |
| Failure | An unknown key and a revoked key fail identically. Distinguishing them tells whoever is probing which of their guesses was once real |
| Deletion | Revoked, never deleted. The identifier appears in the audit entries for everything the key did |

---

## 2. Authorization

**Permission-based, not role-based, at the point of enforcement.**

No code path anywhere in NexaOps branches on a role name. Every protected operation asks "does
this principal hold `incident.assign`?" This makes a custom role a configuration change rather
than a code change.

### Enforced twice

1. **At the endpoint** — `[RequiresPermission(Permissions.IncidentAssign)]`, resolved by a
   policy provider that generates a policy per permission on demand. Without that provider, each
   of the ~50 permissions would need a hand-written `AddPolicy` call, and a new permission would
   silently fail open until someone remembered to add one.

2. **Inside the application service** — the same check again. The service is also reachable from
   the workflow engine and from confirmed AI actions, so the endpoint attribute alone would not
   cover every caller. This is what makes "the AI cannot bypass authorization" a structural
   property rather than a promise.

### Closed by default

The host sets a **fallback policy requiring authentication**, so a newly added controller cannot
be accidentally public. Opting out requires an explicit `[AllowAnonymous]`.

> **Tested by:** `Endpoints_reject_anonymous_callers` across seven endpoints.

### Separation of duties

Permissions that amount to privilege escalation are held only by administrators, and a test
enforces it:

| Permission | Held by |
|---|---|
| `role.manage` | Platform and tenant administrators only |
| `user.reset_password` | Platform and tenant administrators only |
| `incident.priority.override` | Managers, not agents |
| `incident.declare_major` | Managers, not agents |
| `audit.read` | Not implied by any other permission |

> **Tested by:** `Role_administration_and_audit_reading_are_not_granted_casually`,
> `An_agent_can_work_the_queue_but_not_reconfigure_the_service_desk`,
> `No_customer_role_holds_a_platform_permission`.

### The UI is not a security boundary

The front end hides actions a user cannot take, using the permission list in their profile.
That is presentation only. A user who forces a hidden button into view gets a 403 from the API.

---

## 3. Audit

An EF Core `SaveChangesInterceptor` captures before/after snapshots of every tracked entity
change, plus an explicit `IAuditService` for events that are not entity mutations: sign-in,
sign-in failure, permission denial, AI tool execution, export, impersonation.

Each record carries tenant, actor id, actor display name and email, UTC timestamp, action,
entity type and id, human-readable label, JSON before and after states, changed field list,
source (`Api` / `Ai` / `Workflow` / `System` / `Integration`), outcome, correlation id, IP
address and user agent.

### Append-only by construction

No update or delete path is exposed anywhere in the application — not in the API, not in a
service, not in a repository. The trail is append-only because there is no code that could
modify it, not because of a convention.

Reading requires `audit.read`, which no role holds implicitly.

### Redaction

`PasswordHash`, `SecurityStamp`, `TokenHash` and `ReplacedByTokenHash` are replaced with
`[redacted]` in every snapshot. Without that, a read-only audit permission would become a
credential disclosure.

### Immediate versus transactional

Ordinary changes are audited in the caller's unit of work, so the audit row and the change it
describes commit together. Security events — a denied permission, a failed sign-in, a tenant
isolation violation — are written **immediately in their own transaction**, because they must
survive the failed operation that triggered them.

### Bulk provisioning is not audited

Tenant provisioning and demo seeding suppress audit capture. Nobody performed those writes, and
a JSON snapshot per row across several hundred permission grants is noise in the trail.
Deliberate administrative changes made afterwards are audited normally.

---

## 4. Threat coverage

| Threat | Mitigation |
|---|---|
| **SQL injection** | EF Core parameterises every query. No string concatenation into SQL anywhere. Sort fields are allow-listed and never interpolated — an unrecognised field is a 400. SQL Advanced Threat Protection is enabled as defence in depth. |
| **Cross-site scripting** | React escapes by default; no `dangerouslySetInnerHTML` in the codebase. Notification emails HTML-encode every interpolated value, because bodies contain user-written incident titles. Blob downloads are served `Content-Disposition: attachment`, so an uploaded SVG or HTML file cannot execute on the storage origin. |
| **CSRF** | The API is stateless and bearer-token authenticated. No cookie carries authority, so there is nothing for a cross-site request to ride on. |
| **IDOR** | Every record lookup is tenant-filtered and visibility-scoped as a database predicate. An id the caller may not see returns 404, indistinguishable from one that was never issued. |
| **Broken access control** | Fallback authentication policy; permission checks at both the endpoint and the service; tested as a lower-privileged user calling endpoints directly. |
| **Privilege escalation** | `role.manage` restricted to administrators; platform permissions unreachable from any customer role; security stamp invalidates tokens on privilege change. |
| **Tenant isolation failure** | Three independent layers, 18 dedicated tests. See [MULTI-TENANCY.md](MULTI-TENANCY.md). |
| **File upload attacks** | Extension allow-list (not a block-list, which is always one extension behind); 25 MB cap; server-detected content type, not the client-declared one; SHA-256 integrity hash; tenant-prefixed server-generated paths; scan status tracked and a non-clean file never served. |
| **Secret leakage** | No secret in source or appsettings. Managed Identity everywhere it is supported. SQL uses Entra-only authentication — there is no password. Storage shared keys and Service Bus SAS are disabled outright. Azure OpenAI local auth is disabled. Gitleaks runs over full history in CI. |
| **Session attacks** | Rotating single-use refresh tokens, hashed at rest; replay revokes the whole chain; password change revokes every session. |
| **API abuse** | Configurable rate limits, partitioned per authenticated user and per source IP for anonymous traffic, so one noisy tenant cannot exhaust another's budget. Sign-in is limited far more tightly. Front Door adds a second, network-level limit ahead of the application. |
| **Mass assignment** | `CreatedAt` and `CreatedBy` are reverted to their original values on every update, so a crafted payload bound onto a tracked entity cannot rewrite provenance. |
| **Information disclosure in errors** | RFC 9457 Problem Details with a stable machine-readable code. Stack traces, SQL text and provider messages never leave the server outside Development. Every response carries a correlation id so support can find the request without the client being told anything sensitive. |

---

## 5. Transport and headers

Every API response carries:

| Header | Value | Why |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | A browser must not guess a content type |
| `X-Frame-Options` | `DENY` | An API has no reason to be framed |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` | The API returns JSON, never HTML, so nothing needs to load or execute |
| `Referrer-Policy` | `no-referrer` | |
| `Cross-Origin-Resource-Policy` | `same-site` | |
| `Permissions-Policy` | camera, microphone, geolocation, payment all denied | |
| `Cache-Control` | `no-store, no-cache, must-revalidate` | No proxy caches an authenticated response |

`Server` and `X-Powered-By` are removed. HSTS is enabled outside Development. TLS 1.2 is the
floor on SQL, Redis, Service Bus, Storage and API Management, set explicitly because several
default to allowing older versions.

CORS has **no wildcard fallback**. An unconfigured policy blocks the browser rather than opening
the API to every origin.

---

## 6. AI security

Covered in full in [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md). In summary:

- The browser never talks to an AI provider and never holds a key.
- The model can only affect the system through a **registered tool** with a declared permission.
  There is no path by which it can reach the database, run SQL, or execute a shell command.
- A tool the caller lacks permission for is **never described to the model**, so it cannot be
  named in a response or coaxed into being called.
- Permission is checked **before arguments are even parsed**.
- Every tool in this build is read-only, and a test enforces it.
- Tool output is treated as data. The system prompt states that content inside a tool result is
  written by users of the system and is never an instruction — though the prompt is a mitigation,
  not the boundary. The boundary is the registry.

---

## 7. Findings from building this

Three genuine defects were found by the test suite during development and fixed. They are listed
because "the tests pass" is only meaningful if the tests have ever caught anything.

| Finding | Impact | Fix |
|---|---|---|
| **Optimistic concurrency was not enforced.** `ApplyConcurrencyToken` assigned the caller's row version to the entity property. EF compares the version the change tracker *loaded*, so this silently did nothing and concurrent edits overwrote each other. | Lost updates. Two agents editing one incident, last write wins, no warning. | Added `SetExpectedVersion` to the persistence port, setting `OriginalValue`. Caught by `A_stale_concurrency_token_is_refused_rather_than_overwriting_a_colleague`. |
| **A paused SLA clock could breach.** `MarkBreachedIfOverdue` accepted `Paused` as well as `InProgress`. A clock stopped while waiting on the requester would still tip into breach. | SLA reporting overstated breaches, and customers would be charged for time the service desk was blocked. | Only `InProgress` can breach; the monitor no longer scans paused clocks. |
| **`PauseWhenPending` was read through an unloaded navigation.** It silently defaulted to `true`, so a definition configured not to pause still paused. | A commitment that was meant to run continuously stopped. | Copied onto the clock at attach time, like the duration already was. |

A fourth was introduced and caught in the same session: assigning an `AsNoTracking` SLA
definition to a navigation property made EF attempt to re-insert it, violating its primary key.
That is why the clock now carries its own `BusinessCalendarId` rather than reaching through a
navigation.

---

## 8. What is not claimed

- **No compliance certification.** NexaOps provides configurable technical controls that help a
  customer meet their own obligations. It does not carry ISO 27001, SOC 2, or any other
  certification, and this repository makes no such claim.
- **No penetration test.** The threat coverage above describes implemented controls, not the
  result of an independent assessment.
- **Malware scanning is not implemented.** `AttachmentScanStatus` exists and a non-clean file is
  never served, but nothing currently sets the status to `Clean`. Integrating Microsoft Defender
  for Storage is a prerequisite for accepting attachments from untrusted users in production.
- **No SQL row-level security.** Isolation is enforced in the application. A direct database
  connection with sufficient privilege bypasses it.
- **No private endpoints.** Azure SQL, Storage and Service Bus are reachable over the public
  network, restricted by firewall rules and Entra authentication. A customer requiring no public
  network path needs private endpoints added to the Bicep.
