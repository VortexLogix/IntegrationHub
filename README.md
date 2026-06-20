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
        │  APIM rate-limit: 10 req / 60s sliding window
        │  APIM injects: x-correlation-id (GUID)
        ▼
┌─────────────────────────────────────────────────┐
│            Azure API Management                  │
│  subscription key · rate-limit · correlation-id  │
│  logs every request → Application Insights       │
│  On 429 → Retry-After header + 429 response      │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│              Azure Logic App                     │
│  1. Schema-validate (eventId, eventType)         │
│  2. Retry policy (3x exponential backoff)        │
│  3. Call enrichment Function via HTTP            │
│  4. Route enriched message → Service Bus         │
│  5. On failure → webhook alert → 502/503         │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│         .NET 10 Azure Function                   │
│         (EnrichOrderFunction)                    │
│  1. Check idempotency store (Blob)               │
│     → BlobIdempotencyService                     │
│       → exists → return 200 (duplicate)          │
│       → missing → continue                       │
│  2. Validate payload fields                      │
│     → eventId, eventType, sourceSystem required  │
│  3. Enrich with product data + business rules    │
│     → EnrichmentService                          │
│       → product lookup, pricing, priority        │
│  4. Apply claim-check if payload > 64 KB         │
│     → BlobClaimCheckStore (IsClaimCheck=true)    │
│  5. Return EnrichedOrder to Logic App            │
│     → 200 OK with EnrichedOrder body             │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│            Azure Service Bus                     │
│       orders queue · erp-delivery queue          │
│   Dead-letter queue — auto-managed               │
│   MaxDeliveryCount = 3 → then DLQ                │
└──────────┬──────────────────────────────────────┘
           │  ServiceBusTrigger
           ▼
┌──────────────────────────────────────────────────────┐
│  OrderProcessingFunction (Service Bus-triggered)      │
│  ┌─────────────────────────────────────────────────┐ │
│  │  1. Deserialize enriched order body             │ │
│  │     → JSON (fast path)                          │ │
│  │     → on JSON fail → Base64 decode → JSON       │ │
│  │     → FormatException caught + logged            │ │
│  │     → null → throw (→ retry → DLQ after 3)      │ │
│  │                                                 │ │
│  │  2. IOrderDeliveryService.DeliverToErpAsync()   │ │
│  │     ├── Check delivery idempotency marker       │ │
│  │     │   → exists → recover "Completed" → return │ │
│  │     ├── SetStatus("Processing") → TableStorage  │ │
│  │     ├── Check ErpEndpointUrl config             │ │
│  │     │   └── empty → mark delivered + "Completed"│ │
│  │     │           → return NotConfigured          │ │
│  │     ├── HTTP POST to ERP endpoint               │ │
│  │     │   (using IHttpClientFactory)              │ │
│  │     ├── 2xx → mark delivered + "Completed"      │ │
│  │     │      → return Accepted                    │ │
│  │     ├── 4xx/5xx → setStatus("Failed")           │ │
│  │     │          → return Rejected                │ │
│  │     └── network error → exception propagates    │ │
│  │                      → Service Bus retries      │ │
│  │                      → DLQ after MaxDeliveryCount│ │
│  └─────────────────────────────────────────────────┘ │
└──────────────────────┬───────────────────────────────┘
                       │                           ▲
                       │ HTTP POST                 │ GET /api/status/{id}
                       ▼                           │
              ┌─────────────────┐       ┌──────────────────────────┐
              │   Mock ERP API  │       │  StatusFunction          │
              │  (REST consumer)│       │  (or StatusController)   │
              │                 │       │  queries TableStorage    │
              │  returns 200    │       │  → OrderStatusEntity     │
              │  on success     │       │  → 200 OK / 404 NotFound │
              └────────┬────────┘       └──────────────────────────┘
                       │                         │
                       └──────────┬──────────────┘
                                  │
                                  ▼
┌──────────────────────────────────────────────────┐
│    Application Insights + Log Analytics           │
│    + Azure Table Storage (OrderStatus table)      │
│  end-to-end trace · KQL queries · alerts          │
│  DeadLetterHandlerFunction reads DLQ every 5 min  │
│  → logs + sends webhook alert for failed messages │
└──────────────────────────────────────────────────┘
```

---

## Features

| Feature | Implementation |
|---|---|---|
| Claim-check pattern | Payloads > 64 KB stored in Blob; only reference on queue |
| Enrichment idempotency | Zero-byte marker blob per `eventId` — duplicates silently discarded (200 Accepted) |
| Delivery idempotency | `IIdempotencyService` with `delivery:{eventId}` key — prevents duplicate ERP POST on retry recovery |
| Dead-letter handling | Timer function reads DLQ every 5 min, logs + sends webhook alert |
| Correlation ID | `eventId` propagated through every log entry as `correlationId` |
| Managed Identity | No connection strings — all Azure services use DefaultAzureCredential |
| Async request-reply | APIM returns HTTP 202; processing is fully asynchronous |
| Status tracking | `OrderDeliveryService` writes Processing/Completed/Failed to Table Storage |
| Status query | `GET /api/status/{correlationId}?sourceSystem=` via `StatusFunction` (Functions) or `StatusController` (API) |
| Partition-aware query | `GetStatusAsync` includes `PartitionKey eq {sourceSystem}` when sourceSystem is provided |
| Container init | `CreateIfNotExistsAsync` called once in constructor — not per-write |
| RBAC role assignments | Function App and Logic App Managed Identities granted least-privilege roles |
| Double-deserialization | Handles both raw JSON and Base64-encoded JSON; FormatException caught + logged |
| Retry → DLQ pipeline | Service Bus MaxDeliveryCount=3; exceptions propagate for retry, corrupt data goes to DLQ |

---

## Data Flow Verification

### Step-by-step trace of every component in the pipeline

| Step | Component | Input → Action → Output | Error Handling |
|---|---|---|---|
| 1 | **External caller** | HTTP POST `/api/events` with `{eventId, eventType, sourceSystem, payload}` | — |
| 2 | **APIM** (rate-limit 10/60s) | Validates subscription key → injects `x-correlation-id` (GUID) → forwards to Logic App | 429 with `Retry-After` header if rate exceeded |
| 3 | **Logic App** (schema-validate) | Validates `eventId` and `eventType` exist → retry policy 3x exponential backoff → calls `EnrichOrderFunction` | 502/503 with webhook alert on failure |
| 4 | **EnrichOrderFunction** | Receives raw `OrderEvent` → checks `BlobIdempotencyService` → validates fields → calls `EnrichmentService` → applies claim-check if >64KB → returns `EnrichedOrder` | 409 if duplicate (idempotency hit); 400 if validation fails |
| 5 | **BlobIdempotencyService** | Checks for zero-byte marker blob at `idempotency/{eventId}` → exists = duplicate (200 OK) → missing = create marker + continue | Blob storage exception → enrichment fails → Logic App retries |
| 6 | **BlobClaimCheckStore** | If `EnrichedOrder` serialized size > 64 KB → stores body in `claim-check/{eventId}.json` → sets `IsClaimCheck=true` + `ClaimCheckBlobPath` | Blob storage exception → enrichment fails → Logic App retries |
| 7 | **EnrichmentService** | Product lookup → pricing calculation → priority assignment (standard/express) based on business rules | Product not found → enrichment fails → 400 response |
| 8 | **Logic App** (route to queue) | Receives `EnrichedOrder` → sends to Service Bus `orders` queue | Send failure → retry 3x → webhook alert |
| 9 | **Service Bus** (`orders` queue) | Stores message → triggers `OrderProcessingFunction` (MaxDeliveryCount=3) | After 3 failed deliveries → moves to DLQ automatically |
| 10 | **OrderProcessingFunction** | Receives `ServiceBusReceivedMessage` → deserializes body (raw JSON or Base64) → `FormatException` caught + logged → calls `IOrderDeliveryService.DeliverToErpAsync()` | `FormatException` on Base64 → logged with correlationId → rethrows → retry 3x → DLQ |
| 11 | **IOrderDeliveryService** (OrderDeliveryService) | Checks delivery idempotency marker (`delivery:{eventId}`) → if exists, recovers "Completed" status → skips ERP POST; else `SetStatus("Processing")` → checks `ErpEndpointUrl` → HTTP POST to ERP → marks delivered + `SetStatus("Completed" or "Failed")` | Network exception → propagates → retry; HTTP non-success → "Failed" status → message completes |
| 12 | **Table Storage** (`OrderStatus`) | `PartitionKey=SourceSystem`, `RowKey=CorrelationId` → stores status, message, event type, timestamp. Table created once at startup via `CreateIfNotExistsAsync` | Storage exception → function exception → retry → DLQ |
| 13 | **ERP endpoint** (MockEndpoints) | Receives delivery request `{correlationId, productCode, quantity, totalAmount, deliveryPriority}` → returns 200 OK | 500 error → "Failed" status, message completes |
| 14 | **StatusFunction** (or StatusController) | `GET /api/status/{correlationId}?sourceSystem=` → queries `OrderStatus` table (partition-aware when sourceSystem provided) → returns `{correlationId, status, message, lastUpdatedUtc}` | No entity → 404 NotFound |
| 15 | **DeadLetterHandlerFunction** | Timer trigger every 5 min → reads DLQ of `orders` queue → logs each message → sends webhook alert via `HttpNotificationService` | DLQ read failure → retries on next tick |

### Status state machine

```
null ──first message──▶ Processing ──ERP success──▶ Completed
                             │                 │
                             │                 └──(on retry)──▶ idempotency hit
                             │                                    → Completed (recovered)
                             ├──ERP HTTP error────▶ Failed
                             │
                             └──network exception──▶ (abandoned, retries ×3, then DLQ)
```

### Expected behavior per scenario

| Scenario | Status written | Message disposition | Alert |
|---|---|---|---|---|
| Normal flow (ERP 200) | Processing → Completed | Completed (removed) | None |
| Idempotency hit (retry recovery) | Completed (recovered) | Completed (removed) | None |
| ERP returns 4xx/5xx | Processing → Failed | Completed (removed) | None (logged as Warning) |
| ERP unreachable (timeout) | Processing | Abandoned → retry ×3 → DLQ | DeadLetterHandler sends webhook |
| Corrupt message body (Bad JSON + Bad Base64) | None | Abandoned → retry ×3 → DLQ | DeadLetterHandler sends webhook |
| Missing ErpEndpointUrl | Processing → Completed (no ERP) | Completed (removed) | Logged as Warning |

---

## Integration Patterns

| Pattern | Where |
|---|---|---|
| **Claim-Check** | `EnrichmentService` → `BlobClaimCheckStore` |
| **Enrichment Idempotency** | `EnrichmentService` → `BlobIdempotencyService` (`{eventId}` key) |
| **Delivery Idempotency** | `OrderDeliveryService` → `IIdempotencyService` (`delivery:{eventId}` key) |
| **Dead-Letter Processing** | `DeadLetterHandlerFunction` (timer) |
| **Status Tracking** | `OrderProcessingFunction` → `IOrderDeliveryService` → `TableStatusStore` (Processing/Completed/Failed) |
| **Status Query** | `StatusFunction` (HTTP GET) or `StatusController` (API) → `TableStatusStore` (partition-aware) |
| **Failure Alerting** | `HttpNotificationService` → webhook |
| **Correlation ID propagation** | APIM inbound policy → Logic App header → Function |
| **Async Request-Reply** | Logic App returns 202 immediately → status check later |
| **Retry → DLQ** | Service Bus MaxDeliveryCount=3; exceptions from function trigger retry; DLQ monitored by timer |
| **Delivery Service** | `OrderDeliveryService` (extracted from function) — testable via `IOrderDeliveryService` interface |
| **Container Init** | `CreateIfNotExistsAsync` called once per service constructor, not per-write |

---

## Project Structure

```
src/
├── IntegrationHub.Functions/         ← .NET 10 isolated Azure Function App
│   ├── Functions/
│   │   ├── EnrichOrderFunction.cs       ← HTTP-triggered enrichment
│   │   ├── DeadLetterHandlerFunction.cs ← Timer-triggered DLQ processor
│   │   ├── OrderProcessingFunction.cs   ← Service Bus-triggered ERP delivery (thin)
│   │   └── StatusFunction.cs            ← HTTP-triggered status lookup
│   ├── Models/
│   │   ├── OrderEvent.cs
│   │   └── EnrichedOrder.cs
│   ├── Services/
│   │   ├── IEnrichmentService.cs / EnrichmentService.cs
│   │   ├── IIdempotencyService.cs / BlobIdempotencyService.cs
│   │   ├── IClaimCheckStore.cs / BlobClaimCheckStore.cs
│   │   ├── INotificationService.cs / HttpNotificationService.cs
│   │   ├── IStatusStore.cs / TableStatusStore.cs
│   │   ├── IOrderDeliveryService.cs / OrderDeliveryService.cs  ← ERP delivery logic
│   ├── Program.cs
│   ├── local.settings.sample.json    ← Copy to local.settings.json for dev
│   └── host.json
│
├── IntegrationHub.Api/               ← Status query API (reads Table Storage)
├── IntegrationHub.MockEndpoints/     ← Mock CRM + ERP endpoints (for local testing)
└── IntegrationHub.Tests/             ← xUnit unit tests (coverage gate ≥ 60%)
    ├── EnrichmentServiceTests.cs        ← 11 tests: enrichment, validation, idempotency, claim-check, notification
    ├── OrderDeliveryServiceTests.cs     ← 11 tests: Accepted, Rejected, NotConfigured, Processing,
    │                                       idempotent skip, network failure, cancellation, empty EventId,
    │                                       non-JSON error body, status sequence
    ├── TableStatusStoreTests.cs         ← 6 tests: contract, round-trip, not-found, overwrite,
    │                                       partition-aware GetStatus, partition not-found
    ├── InMemoryStatusStore.cs           ← In-memory IStatusStore for testing
    ├── FakeHttpClientFactory.cs         ← Fake IHttpClientFactory (supports ThrowOnCall, Delay, CallCount)
    └── FakeIdempotencyService.cs        ← In-memory IIdempotencyService (tracks processed keys in HashSet)

infra/
├── main.bicep                        ← Orchestrates all modules + RBAC
└── modules/
    ├── monitoring/                   ← Log Analytics + App Insights
    ├── storage/                      ← Storage Account + Blob containers + OrderStatus table
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

# 6. Check the status (after ERP processing)
curl http://localhost:7071/api/status/test-001
```

---

## CI/CD Pipeline Summary

| Pipeline | Trigger | Environments |
|---|---|---|
| `IntegrationHub.Dev.yml` | Push to `develop` | development |
| `IntegrationHub.CI.yml` | Push/PR to `main` | integration (+ Bicep what-if on PR) |
| `IntegrationHub.CD.yml` | Manual or after CI | test → uat → **production (approval gate)** |

**Code coverage gate:** ≥ 60% line coverage required in CI/CD build.
**35 unit tests** protect against regression across EnrichmentService, OrderDeliveryService, TableStatusStore (in-memory), and NotificationService.
**New code must be covered** — in-memory test doubles (no mocks) ensure fast, deterministic tests.

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

## Fresh Deployment Flow

From a brand-new subscription, you can be up and running in ~15 minutes:

### 1. Prerequisites (Azure Portal)

| Step | Where |
|------|-------|
| Create App Registration | Microsoft Entra ID → App registrations → New registration (name: `IntegrationHub-Deployment`) |
| Copy Client ID + Tenant ID | App Registration overview page |
| Add Federated Credentials | App Registration → Certificates & secrets → Federated credentials → one per GitHub environment (`development`, `integration`, `test`, `uat`, `production`) |
| Create GitHub secrets | Settings → Secrets and variables → Actions: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` |

### 2. Bootstrap (one-time per subscription)

```bash
# Login to Azure
az login

# Run the bootstrap script — registers RPs, creates SP, assigns RBAC, purges old APIM
./scripts/setup.ps1 -ClientId "YOUR_CLIENT_ID" -SubscriptionId "YOUR_SUBSCRIPTION_ID"
```

### 3. Deploy

Trigger the pipeline from GitHub Actions → Dev workflow → Run workflow.

Or push to `develop` / `feature/*` — the Dev pipeline auto-triggers.

### 4. Demo

```bash
# Sends a sample order event and checks processing status
./scripts/demo.ps1
```

### Kill Switch

```bash
# Deletes ALL resources. Run this to verify fresh-deployment works cleanly.
./scripts/cleanup.ps1 -ResourceGroup d-az1-ih-integration-rg
```

Then re-run steps 2 → 3 → 4 to verify end-to-end from scratch.

---

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
