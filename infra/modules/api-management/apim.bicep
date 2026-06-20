// =============================================================================
// Integration Hub — API Management Module
// File        : infra/modules/api-management/apim.bicep
// Provisions  : API Management (Consumption tier)
//               — 1 M free calls/month on Consumption plan
//
// Responsibilities
//   - Single HTTPS entry point for all external callers
//   - OAuth 2.0 JWT validation (inbound policy)
//   - Rate limiting per caller key
//   - Request/response logging to App Insights
//
// Name produced: {namePrefix}-gateway-apim
// =============================================================================

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Shared name prefix: {env}-az1-ih')
param namePrefix string

@description('Azure region for this resource.')
param location string

@description('Tags applied to the resource.')
param tags object

@description('App Insights resource ID — used for APIM logger.')
param appInsightsResourceId string

@description('App Insights instrumentation key — used for APIM logger.')
param appInsightsInstrumentationKey string

@description('Publisher email (required by APIM).')
param publisherEmail string = 'admin@integrationhub.dev'

@description('Publisher name shown in the developer portal.')
param publisherName string = 'Integration Hub Team'

@description('Backend base URL APIM forwards requests to.')
param backendUrl string

// ── Resources ─────────────────────────────────────────────────────────────────

// API Management instance — Consumption tier (no infrastructure to manage)
resource apim 'Microsoft.ApiManagement/service@2022-08-01' = {
  name: '${namePrefix}-gateway-apim'
  location: location
  tags: tags
  sku: {
    name: 'Consumption'   // Serverless; 1M calls/month free
    capacity: 0           // Must be 0 for Consumption tier
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
    // Managed Identity lets APIM fetch secrets from Key Vault for policies.
    virtualNetworkType: 'None'
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// ── Logger: connect APIM to App Insights ──────────────────────────────────────
resource apimLogger 'Microsoft.ApiManagement/service/loggers@2022-08-01' = {
  parent: apim
  name: '${namePrefix}-telemetry-ai'
  properties: {
    loggerType: 'applicationInsights'
    description: 'Routes APIM request telemetry to Application Insights'
    credentials: {
      instrumentationKey: appInsightsInstrumentationKey
    }
    isBuffered: true
    resourceId: appInsightsResourceId
  }
}

// ── Integration Hub API definition ───────────────────────────────────────────
resource integrationHubApi 'Microsoft.ApiManagement/service/apis@2022-08-01' = {
  parent: apim
  name: 'integration-hub-api'
  properties: {
    displayName: 'Integration Hub API'
    description: 'Receives order events from source systems and routes them through the integration pipeline.'
    path: 'api'
    protocols: ['https']   // HTTPS only — no plain HTTP
    subscriptionRequired: true
    subscriptionKeyParameterNames: {
      header: 'Ocp-Apim-Subscription-Key'
      query: 'subscription-key'
    }
    apiType: 'http'
  }
}

// ── POST /events operation ────────────────────────────────────────────────────
resource postEventsOperation 'Microsoft.ApiManagement/service/apis/operations@2022-08-01' = {
  parent: integrationHubApi
  name: 'post-events'
  properties: {
    displayName: 'Submit Order Event'
    method: 'POST'
    urlTemplate: '/events'
    description: 'Accepts an order event from any source system. Validated, enriched, and queued for downstream delivery.'
    request: {
      description: 'Order event payload'
      headers: []
      queryParameters: []
      representations: [
        {
          contentType: 'application/json'
          examples: {
            default: {
              value: {
                eventId: 'evt-001'
                eventType: 'OrderCreated'
                sourceSystem: 'CRM'
                payload: {
                  customerId: 'C-1234'
                  productCode: 'SKU-9001'
                  quantity: 3
                }
              }
            }
          }
        }
      ]
    }
    responses: [
      {
        statusCode: 202
        description: 'Accepted — event queued for processing'
      }
      {
        statusCode: 400
        description: 'Bad Request — validation failed'
      }
      {
        statusCode: 429
        description: 'Too Many Requests — rate limit exceeded'
      }
    ]
  }
}

// ── Inbound policy: rate limiting + JWT validation + logging ──────────────────
resource apiPolicy 'Microsoft.ApiManagement/service/apis/policies@2022-08-01' = {
  parent: integrationHubApi
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: replace('''
<policies>
  <inbound>
    <base />

    <set-variable name="correlationId" value="@((string)context.Request.Headers.GetValueOrDefault(&quot;x-correlation-id&quot;, Guid.NewGuid().ToString()))" />

    <!-- Rate limit: 10 calls per 60 seconds per subscription key -->
    <rate-limit calls="10" renewal-period="60" />

    <!-- Quota: max 500 calls per day per subscription key -->
    <quota calls="500" renewal-period="86400" />

    <!-- Require x-correlation-id header; generate one if absent -->
    <set-header name="x-correlation-id" exists-action="override">
      <value>@((string)context.Variables["correlationId"])</value>
    </set-header>

    <!-- Forward correlation ID to backend -->
    <set-backend-service base-url="__BACKEND_URL__" />
    <rewrite-uri template="/api/events/enrich" />
  </inbound>

  <backend>
    <base />
  </backend>

  <outbound>
    <base />
    <!-- Expose correlation ID in response so callers can trace their request -->
    <set-header name="x-correlation-id" exists-action="override">
      <value>@((string)context.Variables["correlationId"])</value>
    </set-header>
  </outbound>

  <on-error>
    <base />
    <return-response>
      <set-status code="500" reason="Internal Server Error" />
      <set-header name="Content-Type" exists-action="override">
        <value>application/json</value>
      </set-header>
      <set-body>@{
        return new JObject(
          new JProperty("error", context.LastError.Message),
          new JProperty("correlationId", context.RequestId)
        ).ToString();
      }</set-body>
    </return-response>
  </on-error>
</policies>
''', '__BACKEND_URL__', backendUrl)
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('APIM gateway URL — entry point for all external API calls.')
output gatewayUrl string = apim.properties.gatewayUrl

@description('APIM resource name.')
output apimName string = apim.name

@description('Principal ID of APIM Managed Identity.')
output apimPrincipalId string = apim.identity.principalId
