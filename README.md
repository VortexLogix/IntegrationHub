# IntegrationHub

> A production-realistic, event-driven order integration platform on Azure — built to show how modern enterprise integration is done right.

[![Build](https://github.com/your-org/IntegrationHub/actions/workflows/IntegrationHub.CI.yml/badge.svg)](https://github.com/your-org/IntegrationHub/actions)

---

## What problem does it solve?

Most enterprise systems need to connect orders from CRM, portals, and internal tools to a downstream ERP. The naive approach — a direct HTTP call — breaks under real-world conditions: duplicate submissions, payload size limits, no retry logic, zero visibility when something fails, and tight coupling that makes every system change risky.

IntegrationHub replaces that with a **reliable, asynchronous pipeline** built from proven Azure services:

- Every order gets a correlation ID and is traceable end-to-end
- Duplicate submissions are silently discarded (idempotency)
- Large payloads are handled transparently (claim-check pattern)
- Failures trigger automatic retries then dead-letter alerting — nothing is lost
- Processing status is queryable at any time via a REST API and a live dashboard

---

## Architecture

```
External caller (CRM / Portal / any HTTP client)
        │
        │  HTTPS POST /api/events   ← subscription key required
        ▼
┌───────────────────────────────────────────────┐
│           Azure API Management                 │
│  • subscription key auth                       │
│  • rate-limit: 10 req / 60 s                  │
│  • injects x-correlation-id (GUID)            │
│  • streams every request → Application Insights│
└──────────────────┬────────────────────────────┘
                   │
                   ▼
┌───────────────────────────────────────────────┐
│            Azure Logic App                     │
│  1. Schema-validate (eventId, eventType)       │
│  2. Call EnrichOrderFunction (retry ×3, 10 s) │
│  3. Route enriched message → Service Bus       │
│  4. On failure → webhook alert → 502/503       │
└──────────────────┬────────────────────────────┘
                   │
                   ▼
┌───────────────────────────────────────────────┐
│         EnrichOrderFunction (.NET 10)          │
│  1. Idempotency check (Blob marker)            │
│  2. Validate payload fields                    │
│  3. Enrich: product lookup, pricing, priority  │
│  4. Claim-check if payload > 64 KB            │
│  5. Return EnrichedOrder → Logic App           │
└──────────────────┬────────────────────────────┘
                   │  Logic App sends to queue
                   ▼
┌───────────────────────────────────────────────┐
│         Azure Service Bus (orders queue)       │
│  MaxDeliveryCount = 3 → then Dead Letter Queue │
└──────────┬────────────────────────────────────┘
           │  ServiceBusTrigger
           ▼
┌───────────────────────────────────────────────┐
│       OrderProcessingFunction (.NET 10)        │
│  1. Deserialize (JSON or Base64 fallback)      │
│  2. Delivery idempotency check                 │
│  3. SetStatus("Processing") → Table Storage    │
│  4. HTTP POST → ERP                            │
│  5. SetStatus("Completed" | "Failed")          │
└──────┬───────────────────────┬────────────────┘
       │ ERP delivery          │ Status updates
       ▼                       ▼
 External ERP          Azure Table Storage
                       (OrderStatus table)
                               │
                   ┌───────────┴────────────┐
                   │ GET /api/status/{id}   │
                   │ StatusFunction         │
                   └────────────────────────┘

Dead Letter Queue → DeadLetterHandlerFunction (timer, 5 min)
                  → logs + sends webhook alert
```

---

## Features

| Feature | Implementation |
|---|---|
| **Idempotent enrichment** | Zero-byte blob marker per `eventId` — duplicate events silently discarded |
| **Idempotent delivery** | `delivery:{eventId}` blob marker — prevents duplicate ERP POST on retry recovery |
| **Claim-check pattern** | Payloads > 64 KB offloaded to Blob Storage; only a reference rides the queue |
| **Async request-reply** | APIM returns 202 immediately; processing is fully asynchronous |
| **Status tracking** | Processing → Completed / Failed written to Table Storage per order |
| **Status query** | `GET /api/status/{correlationId}` via APIM → StatusFunction |
| **Dead-letter handling** | Timer function reads DLQ every 5 min, logs + fires webhook alert |
| **Retry → DLQ pipeline** | Service Bus MaxDeliveryCount = 3; unhandled exceptions trigger retry |
| **Correlation ID propagation** | APIM injects `x-correlation-id` → Logic App → Function App → every log |
| **Managed Identity** | No stored credentials — all Azure service auth via DefaultAzureCredential |
| **Business flow logging** | `Business Flow:` prefixed logs across APIM, Function App, and Logic App |
| **Rate limiting** | APIM policy: 10 calls / 60 s per subscription key |
| **RBAC** | Least-privilege role assignments for Function App and Logic App managed identities |

---

## Project structure

```
src/
├── IntegrationHub.Functions/         ← .NET 10 isolated Azure Function App
│   ├── Functions/
│   │   ├── EnrichOrderFunction.cs       ← HTTP-triggered enrichment endpoint
│   │   ├── OrderProcessingFunction.cs   ← Service Bus-triggered ERP delivery
│   │   ├── StatusFunction.cs            ← HTTP GET status lookup
│   │   ├── DeadLetterHandlerFunction.cs ← Timer-triggered DLQ processor
│   │   └── DashboardFunction.cs         ← Serves the HTML dashboard
│   ├── Services/
│   │   ├── EnrichmentService.cs
│   │   ├── OrderDeliveryService.cs
│   │   ├── BlobIdempotencyService.cs
│   │   ├── BlobClaimCheckStore.cs
│   │   ├── TableStatusStore.cs
│   │   └── HttpNotificationService.cs
│   ├── Models/
│   │   ├── OrderEvent.cs
│   │   └── EnrichedOrder.cs
│   ├── wwwroot/
│   │   └── index.html                   ← Live order dashboard
│   └── host.json

└── IntegrationHub.Tests/             ← xUnit unit tests (coverage gate ≥ 60%)
    ├── EnrichmentServiceTests.cs        ← 11 tests
    ├── OrderDeliveryServiceTests.cs     ← 11 tests
    └── TableStatusStoreTests.cs         ←  6 tests

infra/
├── main.bicep
└── modules/
    ├── monitoring/   ← Log Analytics + App Insights
    ├── storage/      ← Storage Account + Blob containers + OrderStatus table
    ├── key-vault/    ← Key Vault (RBAC model)
    ├── service-bus/  ← Namespace + orders queue
    ├── function-app/ ← Consumption plan + Function App
    ├── logic-app/    ← Logic App + workflow definition
    └── api-management/ ← APIM (Consumption tier) + policies

.github/workflows/
├── IntegrationHub.Dev.yml   ← Push to develop → dev
├── IntegrationHub.CI.yml    ← Push/PR to main → integration
├── IntegrationHub.CD.yml    ← Manual → test → uat → production
├── build-steps-template.yml
└── deploy-steps-template.yml
```

---

## Quick start (fresh deployment)

**Prerequisites:** Azure subscription, GitHub repo, `az` CLI, PowerShell

```bash
# 1. Create App Registration in Microsoft Entra ID
#    Copy Client ID + Tenant ID
#    Add Federated Credentials for each GitHub environment

# 2. Add GitHub secrets: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID

# 3. Bootstrap (one-time)
az login
./scripts/setup.ps1 -ClientId "YOUR_CLIENT_ID" -SubscriptionId "YOUR_SUBSCRIPTION_ID"

# 4. Trigger Dev pipeline (push to develop, or run manually in GitHub Actions)

# 5. Run integration test
./scripts/Test-IntegrationHub.ps1
```

**Dashboard:** `https://{namePrefix}-gw-apim.azure-api.net/api/dashboard`

---

## CI/CD

| Pipeline | Trigger | Environments |
|---|---|---|
| `IntegrationHub.Dev.yml` | Push to `develop` / `feature/*` | development |
| `IntegrationHub.CI.yml` | Push / PR to `main` | integration + Bicep what-if |
| `IntegrationHub.CD.yml` | Manual or after CI | test → uat → production (approval gate) |

Code coverage gate: ≥ 60% line coverage. 28 unit tests using in-memory fakes (no mocking frameworks).

### GitHub secrets required

| Secret | Description |
|---|---|
| `AZURE_CLIENT_ID` | App Registration Client ID |
| `AZURE_TENANT_ID` | Azure Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID |

### GitHub environments required

| Environment | Protection |
|---|---|
| `development` | None |
| `integration` | None |
| `test` | Optional reviewer |
| `uat` | Optional reviewer |
| `production` | **Required reviewers — mandatory** |

---

## Security

| Concern | Approach |
|---|---|
| No stored credentials | Managed Identity (DefaultAzureCredential) for all Azure service auth |
| Secret management | Key Vault with RBAC (`Key Vault Secrets User`) |
| API authentication | APIM subscription key on every inbound request |
| HTTPS only | `httpsOnly: true` on Function App; APIM HTTPS-only |
| No public blob access | `allowBlobPublicAccess: false` |
| Soft-delete | Key Vault (7 days) + Blob Storage (7 days) |

---

## Observability

All services stream to Application Insights + Log Analytics via diagnostic settings.  
Every log entry carries a `Business Flow:` prefix and `CorrelationId` for end-to-end tracing.

### End-to-end order trace (KQL)

```kql
let cid = "paste-your-correlation-id";
AppTraces
| where Message startswith "Business Flow:"
| where tostring(Properties.CorrelationId) == cid or Message contains cid
| project TimeGenerated, Source = "Function/APIM", Message
| union (
    LogicAppWorkflowRuntime
    | where trackedProperties_CorrelationId_s == cid
    | project TimeGenerated, Source = "Logic App",
              Message = trackedProperties_BusinessFlowMessage_s
)
| order by TimeGenerated asc
```

### Dead-letter alert query (KQL)

```kql
traces
| where timestamp > ago(24h)
| where message has "DeadLetter"
| project timestamp,
    correlationId = tostring(customDimensions.correlationId),
    reason        = tostring(customDimensions.deadLetterReason),
    sourceSystem  = tostring(customDimensions.sourceSystem)
| order by timestamp desc
```

---

## Cost (Azure free tier)

| Service | Tier | Free limit |
|---|---|---|
| Azure Functions | Consumption | 1 M executions / month |
| Logic Apps | Consumption | 4,000 actions / month |
| Service Bus | Basic | 10 M ops / first 12 months |
| API Management | Consumption | 1 M calls / month |
| Application Insights | Free | 5 GB / month |
| Blob + Table Storage | LRS | 5 GB |

---

## Prototype → Production gaps

This project demonstrates the patterns production systems use. These gaps should be addressed before going live:

| Gap | Production approach |
|---|---|
| No OAuth / JWT on APIM | Add Entra ID JWT validation policy |
| Rate limit is global | Per-caller limits + quotas by product |
| Single region | Geo-redundancy + cross-region failover |
| No message schema registry | Azure Schema Registry (Avro / JSON governance) |
| Webhook for alerts | Azure Monitor alerts + Action Groups |
| Status table has no TTL | Table Storage lifecycle policy to expire old rows |
| Dashboard has no auth | Add Entra ID Easy Auth on the Function App |
| ERP endpoint is config | Proper service registry or API contract management |
| No load / chaos testing | Validate retry behaviour and DLQ under real traffic |

---

*Built by [Vivek Kajavadra](https://github.com/vivekkajavadra) and [Karthikeyan T C](https://github.com/karthikeyan-tc)*
