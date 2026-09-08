# NexaOps — Observability

Three signals — logs, traces, metrics — plus the audit trail, which is a fourth thing that people
often confuse with logging and shouldn't.

---

## 1. The four, and what each is for

| Signal | Question it answers | Where it goes | Retention |
|---|---|---|---|
| **Logs** (Serilog) | What happened, in words | Application Insights | 30–365 days by environment |
| **Traces** (OpenTelemetry) | Where did the time go | Application Insights | Sampled |
| **Metrics** (OpenTelemetry) | How much, how often, how fast | Application Insights | 90 days |
| **Audit** | Who changed what, and to what | The `audit` schema in SQL | Indefinite (see below) |

The audit trail is a **product feature**, not telemetry. It is queryable by customers who hold
`audit.read`, it is legally meaningful, and it must survive independently of any observability
vendor. That is why it is in the transactional database and not in a log sink.

Telemetry is the opposite: operational, ours, disposable, sampled.

---

## 2. Correlation

Every request gets a correlation id, assigned by `CorrelationIdMiddleware`:

- An incoming `X-Correlation-Id` is honoured, so a caller's trace continues through us.
- A client-supplied value is **sanitised and capped at 64 characters** — accepted for trace
  continuity, never trusted for anything security-relevant, and never allowed to bloat a log line.
- Absent one, the current `Activity.TraceId` is used, falling back to a UUID v7.
- It is pushed into the Serilog `LogContext`, echoed on the response header, and written into
  every audit row and every Problem Details response.

The result is one identifier a customer can quote that ties together the log line, the
distributed trace, and the audit record. That is the whole point of it.

---

## 3. Logs

Serilog, enriched with environment name and `Application=NexaOps.Api`, plus the correlation id
from the log context.

Request logging uses a level selector rather than a flat level:

| Condition | Level |
|---|---|
| `/health/*` | `Verbose` |
| Unhandled exception, or status ≥ 500 | `Error` |
| Everything else | `Information` |

Health probes at `Information` would be most of the log volume and most of the bill, and would
bury real traffic. They are not silenced — they are demoted, so they are there when a probe
problem is what you are chasing.

### What is never logged

- Passwords, password hashes, security stamps, refresh tokens or their hashes.
- SQL statement text (see §4).
- Bearer tokens.
- Stack traces in responses. They are logged server-side; the client gets a correlation id.

### Security events

Authentication failures, permission denials and tenant isolation violations are logged **and**
audited. A tenant isolation violation logs at `Critical` — it should never happen, and if it does
it is either a bug in the isolation layers or an active attack. Either way it warrants waking
someone.

---

## 4. Traces

OpenTelemetry, exported to Azure Monitor when `ApplicationInsights:ConnectionString` is set.
Instrumented: ASP.NET Core, `HttpClient`, `SqlClient`.

Two deliberate configuration choices:

**Health probes are filtered out of tracing.** They would otherwise dominate trace volume and
cost, for no diagnostic value.

**SQL statement text is not captured.** `AddSqlClientInstrumentation()` is left at its default of
*not* recording statements. Query text can contain customer data — an incident title in a search
predicate, an email address in a filter — and a trace backend is not the right place for that.
You still get the connection, the duration, and the failure; you do not get the text. Turning
this on to debug a slow query is a decision with a data-handling consequence, not a free switch.

When the connection string is absent, OpenTelemetry still runs and simply exports nowhere. Local
development is not a different code path.

---

## 5. Metrics

Runtime and ASP.NET Core instrumentation give the usual infrastructure metrics. On top of those,
`NexaOpsMetrics` publishes **product** metrics under the `NexaOps` meter — the questions a service
desk manager asks, not the questions an SRE asks:

| Metric | Type | Tags |
|---|---|---|
| `nexaops.incidents.created` | Counter | `priority` |
| `nexaops.incidents.resolved` | Counter | `priority` |
| `nexaops.incidents.resolution_minutes` | Histogram | `priority` |
| `nexaops.sla.breached` | Counter | `target` |
| `nexaops.sla.warned` | Counter | `target` |
| `nexaops.ai.tool.invoked` | Counter | `tool`, `succeeded` |
| `nexaops.auth.failed` | Counter | `reason` |

### No tenant id as a metric tag

Deliberate. Tagging by tenant multiplies cardinality by the customer count, and metric backends
bill and degrade on cardinality. Per-tenant figures come from SQL, where they belong and where
they are already tenant-filtered.

`nexaops.auth.failed` is tagged by *reason* — a low-cardinality enum — even though the API
returns an identical 401 for every reason. The distinction stays server-side, which is exactly
the point: operators can tell "one user forgot their password" from "someone is spraying" while
an attacker learns nothing from the response.

---

## 6. Health endpoints

| Endpoint | Checks | Anonymous |
|---|---|---|
| `/health/live` | Nothing — the process answered | Yes |
| `/health/ready` | Everything tagged `ready`, currently the database | Yes |

Liveness deliberately checks nothing. If it checked the database, a transient SQL blip would fail
liveness, Container Apps would kill the container, and a five-second dependency hiccup would
become a restart storm. Liveness answers "is this process wedged"; readiness answers "can it
serve"; those are different questions with different remedies.

Both are anonymous because a platform probe has no credentials, and both return only status
names — no versions, no connection strings, no dependency detail.

---

## 7. Dashboards

Not built as code. There is no dashboard ARM template in this repository, and saying there is
would be untrue. The queries below are what a first dashboard should be built from.

**Request health**
```kusto
requests
| where timestamp > ago(24h) and not(url endswith "/health/live" or url endswith "/health/ready")
| summarize total = count(), failed = countif(success == false),
            p50 = percentile(duration, 50), p95 = percentile(duration, 95)
          by bin(timestamp, 5m)
| extend errorRate = todouble(failed) / total
```

**Slowest endpoints**
```kusto
requests
| where timestamp > ago(1h)
| summarize count(), p95 = percentile(duration, 95) by name
| order by p95 desc
| take 20
```

**SLA breaches by target**
```kusto
customMetrics
| where name == "nexaops.sla.breached" and timestamp > ago(7d)
| summarize sum(value) by tostring(customDimensions.target), bin(timestamp, 1h)
```

**Authentication failures — the spray detector**
```kusto
customMetrics
| where name == "nexaops.auth.failed" and timestamp > ago(1h)
| summarize sum(value) by tostring(customDimensions.reason), bin(timestamp, 5m)
```

**Tenant isolation violations — should always be empty**
```kusto
traces
| where severityLevel >= 4 and message has "TenantIsolationViolation"
| project timestamp, message, customDimensions.CorrelationId
```

**Follow one request end to end**
```kusto
union requests, traces, dependencies, exceptions
| where customDimensions.CorrelationId == "<id the customer quoted>"
| order by timestamp asc
```

---

## 8. Alerts

The Bicep provisions Application Insights and Log Analytics but **does not provision alert rules
or an action group**. These are the rules to create, with the reasoning, rather than a claim that
they exist.

| Alert | Condition | Severity | Why this threshold |
|---|---|---|---|
| Availability | The availability test fails from 2+ locations | 1 | One location failing is usually the location |
| Error rate | 5xx > 5% over 5 min | 1 | Below that is noise at low volume |
| Latency | p95 > 2 s over 10 min | 2 | Agents notice a slow queue before they notice an error |
| Readiness failing | `/health/ready` unhealthy 3 times in 5 min | 1 | A dependency is down |
| **Tenant isolation violation** | **Any occurrence** | **1** | Should be structurally impossible. One is an incident |
| Authentication failure spike | > 100 in 5 min | 2 | Credential spraying |
| SLA breach spike | Breaches > 3× the 7-day hourly mean | 3 | Usually an ops problem, not a platform one |
| SQL DTU / CPU | > 80% for 15 min | 2 | Ahead of user-visible impact |
| AI tool denials | Sustained non-zero | 3 | Either misconfigured roles or something probing |
| Deployment failure | Pipeline failed | 2 | |

The tenant isolation alert is the one that matters most and is the cheapest to get wrong by
setting a threshold on it. It should be "any", not "more than N".

---

## 9. Gaps

- **No alert rules or action group in Bicep.** The table above is a specification, not a
  deployment.
- **No dashboards as code.**
- **No availability test provisioned.** §8 assumes one; the Bicep does not create it.
- **No log-based per-tenant usage reporting**, by design (see §5) — but nothing yet reports it
  from SQL either.
- **No SLO definitions or error budgets.** The alert thresholds above are reasonable starting
  points, not commitments derived from a stated objective.
- **Sampling is at the Azure Monitor default.** Not tuned, and it should be before production
  volume.
