# NexaOps

**AI-native enterprise IT service management, built for the Indian market.**

NexaOps is an original, independently designed ITSM and workflow automation platform. It is
**not** derived from, and contains no source code, schema, branding, trademarks, or copyrighted
material from, any commercial ITSM vendor.

---

## What is built

This repository contains **Phase 1 (platform foundation) plus a complete Incident Management
module and a demo environment.** Everything listed below is implemented, tested, and runs.

| Capability | State |
|---|---|
| Multi-tenancy with three-layer isolation | Built, 18 integration tests |
| Authentication (first-party JWT + Entra ID design) | Built |
| Permission-based authorization, 12 seeded roles | Built, 14 tests |
| Append-only audit trail | Built |
| Incident management, full lifecycle | Built, 23 integration tests |
| SLA engine with Indian business calendars | Built, 32 tests |
| Notifications (in-app + email dispatch) | Built |
| Service desk dashboard, incident queue, record pages | Built |
| AI abstraction, tool registry, grounded assistant | Built, 9 security tests |
| Azure infrastructure as Bicep | Built, compiles clean |
| CI/CD pipelines | Built |
| Demo environment, 420 incidents across 2 tenants | Built |

Modules that ship in later phases — requests, problems, changes, knowledge, service catalog,
CMDB, assets, the workflow engine and reporting — appear in the navigation marked **"Later"**
and are deliberately not clickable. Nothing in this product pretends to work.

**[See the full status report, including known limitations →](docs/STATUS.md)**

---

## Running it locally

### Prerequisites

- .NET 10 SDK
- Node.js 20.19 or later
- SQL Server LocalDB (ships with Visual Studio) or a SQL Server container

No Azure subscription is needed. Every Azure adapter reports itself unconfigured and the
product runs without it — attachments go to the local filesystem, email is not sent (and says
so), and the AI assistant reports that no provider is configured rather than inventing answers.

### 1. Set the token signing key

The API refuses to start without one. There is no default, because a defaulted signing key
would let anyone who reads this repository mint a valid token for any tenant.

```bash
dotnet user-secrets set "Auth:SigningKey" "$(openssl rand -base64 48)" --project src/NexaOps.Api
```

### 2. Start the API

It applies migrations and seeds the demo environment on first run in Development.

```bash
dotnet run --project src/NexaOps.Api
```

The API listens on `http://localhost:5266`. API documentation is at `/docs`.

### 3. Start the front end

```bash
cd src/NexaOps.Web
npm install
npm run dev
```

Open `http://localhost:5173`.

### Rebuilding the demo from scratch

```bash
pwsh ./infra/scripts/reseed-demo.ps1
```

This drops the database, rebuilds, reseeds, and prints the resulting queue shape so you can see
what a demo will show before you show it.

---

## Demo accounts

Tenant **Acme Technologies India** (`acme-in`). Password for all accounts:
`NexaOps#Demo2026`

| Account | Role | What they can do |
|---|---|---|
| `priya.raghavan@acmetech.example.in` | Tenant Administrator, IT Manager | Everything in the tenant |
| `arun.mehta@acmetech.example.in` | Service Desk Manager | Queue, priority overrides, major incidents, SLA config, audit |
| `kavya.nair@acmetech.example.in` | Service Desk Agent | Work the queue; no priority override, no audit |
| `aditya.menon@acmetech.example.in` | Requester | Own tickets only; never sees internal work notes |
| `vikram.iyer@acmetech.example.in` | Asset Manager | Read access, reporting |
| `neha.gupta@acmetech.example.in` | CMDB Administrator | Read access, reporting |

A second tenant, **Northwind Logistics India** (`deepak.varma@northwind.example.in`), exists so
tenant isolation can be demonstrated live rather than asserted. Sign in as an Acme agent and no
Northwind record is reachable — by search, by direct URL, or through the AI assistant.

All demo data is synthetic. No real person's data is used.

**[Demo scripts for each scenario →](docs/DEMO.md)**

---

## Repository layout

```
src/
  NexaOps.Domain/          Entities, invariants, SLA arithmetic. No framework dependencies.
  NexaOps.Application/     Use cases, DTOs, validators, ports, AI tool registry.
  NexaOps.Infrastructure/  EF Core, persistence, identity, Azure adapters, migrations.
  NexaOps.Api/             HTTP surface, auth wiring, middleware, workers, seeding.
  NexaOps.Web/             React 19 + TypeScript + Vite + MUI front end.
tests/
  NexaOps.Domain.Tests/         113 tests. Pure domain rules.
  NexaOps.Application.Tests/    31 tests. Use-case orchestration and the permission catalogue.
  NexaOps.Api.IntegrationTests/ 76 tests. Real HTTP against real SQL Server.
infra/
  bicep/                   Azure infrastructure, four environments.
  scripts/                 Local development helpers.
docs/                      Architecture, security, operations, demo scripts.
```

---

## Documentation

| Document | Covers |
|---|---|
| [PHASE-1-REPORT.md](docs/PHASE-1-REPORT.md) | The full Phase 1 delivery report, 22 sections |
| [STATUS.md](docs/STATUS.md) | What is built, test results, known limitations, next phase |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layering, dependency rules, cross-cutting concerns |
| [MULTI-TENANCY.md](docs/MULTI-TENANCY.md) | Isolation design and threat model |
| [SECURITY.md](docs/SECURITY.md) | Authentication, authorization, audit, threat coverage |
| [DATABASE.md](docs/DATABASE.md) | Schema, indexes, conventions |
| [AI-ARCHITECTURE.md](docs/AI-ARCHITECTURE.md) | Tool registry, grounding, prompt-injection posture |
| [AZURE-DEPLOYMENT.md](docs/AZURE-DEPLOYMENT.md) | Provisioning and deploying to Azure |
| [DISASTER-RECOVERY.md](docs/DISASTER-RECOVERY.md) | Backups, RPO/RTO, recovery runbooks |
| [OBSERVABILITY.md](docs/OBSERVABILITY.md) | Logging, tracing, metrics, dashboards, alerts |
| [INDIA-READINESS.md](docs/INDIA-READINESS.md) | Localisation, DPDP-supporting controls, CERT-In readiness |
| [TESTING.md](docs/TESTING.md) | Test strategy and how to run each suite |
| [DEMO.md](docs/DEMO.md) | Demo scripts, with the parts that are not built called out |

---

## Quality gates

Every one of these passes on the current tree:

```bash
dotnet build NexaOps.slnx                # 0 warnings, 0 errors
dotnet test NexaOps.slnx                 # 220 tests
cd src/NexaOps.Web && npm run typecheck   # clean
cd src/NexaOps.Web && npm run lint        # clean
cd src/NexaOps.Web && npm run test        # 38 tests
cd src/NexaOps.Web && npm run build       # 264 KB gzipped
az bicep build --file infra/bicep/main.bicep   # 0 warnings
```

---

## Licence and attribution

Copyright (c) NexaOps. All rights reserved.

NexaOps is an independent product. Microsoft, Azure, and Entra ID are trademarks of Microsoft
Corporation and are referenced only to describe the platform this software runs on.
