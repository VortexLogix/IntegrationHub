// =============================================================================
// Integration Hub — Logic App Module
// File        : infra/modules/logic-app/la.bicep
// Provisions  : Logic App (Consumption) — the workflow orchestrator
//
// The actual workflow definition lives alongside this module at:
//   infra/modules/logic-app/workflows/orchestrator-definition.json
//
// Tier   : Consumption — free tier (4,000 actions/month)
// Name   : {namePrefix}-orchestrator-la
// =============================================================================

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Shared name prefix: {env}-az1-ih')
param namePrefix string

@description('Azure region for this resource.')
param location string

@description('Tags applied to the resource.')
param tags object

@description('Log Analytics Workspace resource ID (for diagnostic settings).')
param appInsightsWorkspaceId string

@description('Service Bus namespace name — used in API connection.')
param serviceBusNamespaceName string

@description('HTTP endpoint of the enrichment function.')
param enrichmentFunctionUrl string = 'https://example.invalid/api/events/enrich'

@description('Webhook URL used for failure notifications.')
param notificationWebhookUrl string = 'https://example.invalid/webhook'

// ── Variables ─────────────────────────────────────────────────────────────────

// Load the workflow definition from the adjacent JSON file.
// Bicep's loadJsonContent() reads and inlines the file at compile time.
var workflowDefinition = loadJsonContent('workflows/orchestrator-definition.json')
var serviceBusConnectionString = 'Endpoint=sb://${serviceBusNamespaceName}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=${serviceBusRootKey.listKeys().primaryKey}'

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

resource serviceBusRootKey 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2022-10-01-preview' existing = {
  parent: serviceBusNamespace
  name: 'RootManageSharedAccessKey'
}

// ── Resources ─────────────────────────────────────────────────────────────────

// Logic App — Consumption tier (serverless, per-action billing)
resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: '${namePrefix}-orchestrator-la'
  location: location
  tags: tags

  // System-assigned Managed Identity — used to authenticate to Service Bus
  // and Key Vault without stored credentials.
  identity: {
    type: 'SystemAssigned'
  }

  properties: {
    state: 'Enabled'

    // Inline the workflow definition from the JSON file.
    definition: workflowDefinition.definition

    // API connections are wired up at deploy time via parameters.
    // The Logic App designer stores these as resource IDs.
    parameters: {
      '$connections': {
        value: {
          serviceBus: {
            connectionId: serviceBusConnection.id
            connectionName: 'servicebus'
            id: subscriptionResourceId(
              'Microsoft.Web/locations/managedApis',
              location,
              'servicebus'
            )
          }
        }
      }
      enrichmentFunctionUrl: {
        value: enrichmentFunctionUrl
      }
      notificationWebhookUrl: {
        value: notificationWebhookUrl
      }
    }
  }
}

// Service Bus API connection
// This managed connector lets the Logic App designer send and receive
// messages on Service Bus without manual SDK code.
resource serviceBusConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: '${namePrefix}-sb-connection'
  location: location
  tags: tags
  properties: {
    displayName: '${namePrefix}-sb-connection'
    api: {
      id: subscriptionResourceId(
        'Microsoft.Web/locations/managedApis',
        location,
        'servicebus'
      )
    }
    parameterValues: {
      connectionString: serviceBusConnectionString
    }
  }
}

// Diagnostic settings — stream Logic App run logs to Log Analytics
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${namePrefix}-la-diag'
  scope: logicApp
  properties: {
    workspaceId: appInsightsWorkspaceId
    logs: [
      {
        category: 'WorkflowRuntime'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
    ]
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Logic App name.')
output logicAppName string = logicApp.name

@description('Resource ID of the Logic App.')
output logicAppId string = logicApp.id

@description('Principal ID of the Logic App Managed Identity.')
output logicAppPrincipalId string = logicApp.identity.principalId

