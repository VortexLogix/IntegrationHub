# IntegrationHub
Production-grade Azure Integration Hub using Functions, Logic Apps, APIM, Service Bus, and Bicep

> **Portfolio-grade, production-realistic enterprise integration platform on Azure.**
> Built to demonstrate the exact skills enterprise clients and consulting firms hire for.

---

## Architecture

```
External caller (CRM / Portal / any HTTP client)
        │
        │  HTTPS POST /api/events
        ▼
┌─────────────────────────────────────────────────┐
│            Azure API Management                  │
│  subscription key · rate-limit · correlation-id  │
│  logs every request → Application Insights       │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│              Azure Logic App                     │
│  1. Schema-validate (eventId, eventType)         │
│  2. Call enrichment Function via HTTP            │
│  3. Route enriched message → Service Bus         │
│  4. On failure → webhook alert → 502/503         │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│         .NET 10 Azure Function                   │
│         (EnrichOrderFunction)                    │
│  1. Check idempotency store (Blob)               │
│  2. Validate payload fields                      │
│  3. Enrich with product data + business rules    │
│  4. Apply claim-check if payload > 64 KB         │
│  5. Return EnrichedOrder to Logic App            │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│            Azure Service Bus                     │
│       orders queue · erp-delivery queue          │
│   Dead-letter queue — auto-managed               │
└──────────┬──────────────────────────┬───────────┘
           │                          │
           ▼                          ▼
  ┌─────────────────┐       ┌─────────────────────┐
  │   Mock ERP API  │       │  Notification        │
  │  (REST consumer)│       │  Webhook             │
  │                 │       │  (Slack/Teams/custom)│
  └─────────────────┘       └─────────────────────┘
           │                          │
           └──────────┬───────────────┘
                      │ all telemetry
                      ▼
┌─────────────────────────────────────────────────┐
│    Application Insights + Log Analytics          │
│  end-to-end trace · KQL queries · alerts         │
└─────────────────────────────────────────────────┘
```

---

## Features

| Feature | Implementation |
|---|---|
| Claim-check pattern | Payloads > 64 KB stored in Blob; only reference on queue |
| Idempotency | Zero-byte marker blob per `eventId` — duplicates silently discarded |
| Dead-letter handling | Timer function reads DLQ every 5 min, logs + sends webhook alert |
| Correlation ID | `eventId` propagated through every log entry as `correlationId` |
| Managed Identity | No connection strings — all Azure services use DefaultAzureCredential |
| Async request-reply | APIM returns HTTP 202; processing is fully asynchronous |
| RBAC role assignments | Function App and Logic App Managed Identities granted least-privilege roles |

---

## Integration Patterns

| Pattern | Where |
|---|---|
| **Claim-Check** | `EnrichmentService` → `BlobClaimCheckStore` |
| **Idempotency / Exactly-Once** | `EnrichmentService` → `BlobIdempotencyService` |
| **Dead-Letter Processing** | `DeadLetterHandlerFunction` (timer) |
| **Failure Alerting** | `HttpNotificationService` → webhook |
| **Correlation ID propagation** | APIM inbound policy → Logic App header → Function |
| **Async Request-Reply** | Logic App returns 202 immediately |

---

## Project Structure

```
src/
├── IntegrationHub.Functions/         ← .NET 10 isolated Azure Function App
│   ├── Functions/
│   │   ├── EnrichOrderFunction.cs    ← HTTP-triggered enrichment
│   │   └── DeadLetterHandlerFunction.cs ← Timer-triggered DLQ processor
│   ├── Models/
│   │   ├── OrderEvent.cs
│   │   └── EnrichedOrder.cs
│   ├── Services/
│   │   ├── IEnrichmentService.cs / EnrichmentService.cs
│   │   ├── IIdempotencyService.cs / BlobIdempotencyService.cs
│   │   ├── IClaimCheckStore.cs / BlobClaimCheckStore.cs
│   │   └── INotificationService.cs / HttpNotificationService.cs
│   ├── Program.cs
│   ├── local.settings.sample.json    ← Copy to local.settings.json for dev
│   └── host.json
│
├── IntegrationHub.Api/               ← Status / query API
├── IntegrationHub.MockEndpoints/     ← Mock CRM + ERP endpoints
└── IntegrationHub.Tests/             ← xUnit unit tests

infra/
├── main.bicep                        ← Orchestrates all modules + RBAC
└── modules/
    ├── monitoring/                   ← Log Analytics + App Insights
    ├── storage/                      ← Storage Account + Blob containers
    ├── key-vault/                    ← Key Vault (RBAC model)
    ├── service-bus/                  ← Namespace + orders + erp-delivery queues
    ├── function-app/                 ← Consumption plan + Function App
    ├── logic-app/                    ← Logic App + workflow definition
    └── api-management/               ← APIM (Consumption tier)

.github/workflows/
├── IntegrationHub.Dev.yml            ← develop branch → dev environment
├── IntegrationHub.CI.yml             ← main branch → integration + PR what-if
├── IntegrationHub.CD.yml             ← manual/auto → test → uat → production
├── build-steps-template.yml          ← Reusable build/test/publish template
└── deploy-steps-template.yml         ← Reusable infra/function deploy template
```

---

## Local Development Setup

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download), [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local), [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite)

```bash
# 1. Clone
git clone https://github.com/<your-org>/IntegrationHub.git
cd IntegrationHub

# 2. Configure local settings
cp src/IntegrationHub.Functions/local.settings.sample.json \
   src/IntegrationHub.Functions/local.settings.json
# Edit local.settings.json and fill in your values

# 3. Start Azurite (Storage emulator)
azurite --location .azurite

# 4. Run the Function App
cd src/IntegrationHub.Functions
func start

# 5. Test the enrichment endpoint
curl -X POST http://localhost:7071/api/events/enrich \
  -H "Content-Type: application/json" \
  -d '{"eventId":"test-001","eventType":"OrderCreated","sourceSystem":"CRM","payload":{"productCode":"SKU-9001","quantity":2}}'
```

---

## CI/CD Pipeline Summary

| Pipeline | Trigger | Environments |
|---|---|---|
| `IntegrationHub.Dev.yml` | Push to `develop` | development |
| `IntegrationHub.CI.yml` | Push/PR to `main` | integration (+ Bicep what-if on PR) |
| `IntegrationHub.CD.yml` | Manual or after CI | test → uat → **production (approval gate)** |

**Code coverage gate:** ≥ 60% line coverage required in CI/CD build.

---

## GitHub Setup

### Secrets required

| Secret | Description |
|---|---|
| `AZURE_CREDENTIALS` | Output of `az ad sp create-for-rbac --sdk-auth` |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID |
| `APIM_SUBSCRIPTION_KEY_TEST` | APIM key for test environment |
| `APIM_SUBSCRIPTION_KEY_UAT` | APIM key for UAT environment |
| `APIM_SUBSCRIPTION_KEY_PROD` | APIM key for production environment |

### Environments required

Create in GitHub → Settings → Environments:

| Environment | Protection |
|---|---|
| `development` | None |
| `integration` | None |
| `test` | Optional reviewer |
| `uat` | Optional reviewer |
| `production` | **Required reviewers — mandatory** |

---

## Security Architecture

| Concern | Approach |
|---|---|
| No stored credentials | Managed Identity (DefaultAzureCredential) for all Azure service access |
| Secret management | Key Vault with RBAC (`Key Vault Secrets User`) |
| API authentication | APIM subscription key on every inbound request |
| HTTPS only | `httpsOnly: true` on Function App; APIM HTTPS-only |
| No public blob access | `allowBlobPublicAccess: false` |
| Soft-delete | Enabled on Key Vault (7 days) and Blob Storage (7 days) |

---

## Key KQL Query — Failed messages in 24 hours

```kql
traces
| where timestamp > ago(24h)
| where message has "DeadLetter"
| project
    timestamp,
    correlationId = tostring(customDimensions.correlationId),
    reason        = tostring(customDimensions.deadLetterReason),
    sourceSystem  = tostring(customDimensions.sourceSystem)
| order by timestamp desc
```

---

## Zero-Cost Demo

Every service tier stays within Azure free limits:

| Service | Tier | Free limit |
|---|---|---|
| Azure Functions | Consumption | 1 M executions/month (permanent) |
| Logic Apps | Consumption | 4,000 actions/month |
| Service Bus | Basic | 10 M ops/first 12 months |
| API Management | Consumption | 1 M calls/month |
| Application Insights | Free | 5 GB/month |
| Blob Storage | LRS | 5 GB (permanent) |

---

*Built by [Vivek Kajavadra](https://github.com/vivekkajavadra) and [Karthikeyan T C](https://github.com/karthikeyan-tc)*
