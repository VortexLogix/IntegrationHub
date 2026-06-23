// =============================================================================
// Integration Hub — Function App Module
// File        : infra/modules/function-app/func.bicep
// Provisions  : App Service Plan (Consumption)  +  Function App (.NET 8 isolated)
//
// Security    : System-assigned Managed Identity — no connection strings in
//               plain text. All secrets referenced from Key Vault.
//
// Names produced
//   App Service Plan : {namePrefix}-enrichment-asp
//   Function App     : {namePrefix}-enrichment-func
// =============================================================================

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Shared name prefix: {env}-az1-ih')
param namePrefix string

@description('Azure region for this resource.')
param location string

@description('Tags applied to the resource.')
param tags object

@description('Application Insights connection string (from monitoring module).')
param appInsightsConnectionString string

@description('Storage Account name backing the Function App runtime.')
param storageAccountName string

@description('Key Vault name — used to build Key Vault reference URIs in app settings.')
param keyVaultName string

@description('Service Bus namespace name — connection string stored in Key Vault.')
param serviceBusNamespaceName string

// ── Variables ─────────────────────────────────────────────────────────────────

// Key Vault reference helper — resolves a secret named {secretName} at runtime.
// The Function App Managed Identity must hold the Key Vault Secrets User role.
var kvRef = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName='

// ── Resources ─────────────────────────────────────────────────────────────────

// Consumption plan — Windows; scales to zero; 1M executions/month free
resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: '${namePrefix}-enrichment-asp'
  location: location
  tags: tags
  sku: {
    name: 'Y1'       // Y1 = Consumption plan
    tier: 'Dynamic'
  }
  properties: {
    reserved: false  // false = Windows
  }
}

// Function App — .NET 10 isolated worker on Windows Consumption
resource functionApp 'Microsoft.Web/sites@2022-09-01' = {
  name: '${namePrefix}-enrichment-func'
  location: location
  tags: tags
  kind: 'functionapp'   // Windows; 'functionapp,linux' is Linux

  // Enable system-assigned Managed Identity so it can read from Key Vault
  // and connect to Service Bus without storing credentials.
  identity: {
    type: 'SystemAssigned'
  }

  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true    // Force HTTPS — never allow plain HTTP

    siteConfig: {
      // .NET 10 isolated worker on Windows
      netFrameworkVersion: 'v10.0'

      // App settings — all sensitive values come from Key Vault references.
      appSettings: [
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          // Runtime identifier for isolated worker model.
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          // Functions host version.
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          // Storage account name for the runtime (triggers, bindings state).
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          // App Insights — plain connection string, not a secret.
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          // Service Bus connection via Key Vault reference.
          // Secret name in KV: ServiceBusConnectionString
          name: 'ServiceBusConnection__fullyQualifiedNamespace'
          value: '${serviceBusNamespaceName}.servicebus.windows.net'
        }
        {
          // Name of the orders intake queue.
          name: 'ServiceBusOrdersQueueName'
          value: 'orders'
        }
        {
          // Name of the ERP delivery queue.
          name: 'ServiceBusErpDeliveryQueueName'
          value: 'erp-delivery'
        }
        {
          // Table name for order status tracking.
          name: 'StatusTableName'
          value: 'OrderStatus'
        }
        {
          // Downstream ERP endpoint — pulled from Key Vault at runtime.
          name: 'ErpEndpointUrl'
          value: '${kvRef}ErpEndpointUrl)'
        }
        {
          // Notification webhook URL — pulled from Key Vault at runtime.
          name: 'NotificationWebhookUrl'
          value: '${kvRef}NotificationWebhookUrl)'
        }
        {
          // Blob container for claim-check large payloads.
          name: 'ClaimCheckContainerName'
          value: 'claim-check-payloads'
        }
        {
          // Blob container for idempotency deduplication keys.
          name: 'IdempotencyContainerName'
          value: 'idempotency-store'
        }
      ]

      // CORS — restrict to known origins in UAT/prod.
      cors: {
        allowedOrigins: ['*']   // Tighten per environment via parameters
      }
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Function App name — used by pipeline deploy step.')
output functionAppName string = functionApp.name

@description('Principal ID of the Function App Managed Identity.')
output functionAppPrincipalId string = functionApp.identity.principalId

@description('Resource ID of the Function App.')
output functionAppId string = functionApp.id
