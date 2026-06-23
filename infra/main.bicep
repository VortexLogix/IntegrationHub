// =============================================================================
// Integration Hub — Infrastructure Entry Point
// File        : infra/main.bicep
// Description : Orchestrates all Bicep modules. This is the only file the
//               pipeline deploys directly. Each module is self-contained and
//               accepts only what it needs via explicit parameters.
//
// Naming      : {env}-az1-ih-{scope}-{suffix}   (all lowercase)
// =============================================================================

// ── Target scope ─────────────────────────────────────────────────────────────
targetScope = 'resourceGroup'

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Environment code. d=dev | i=int | t=test | u=uat | p=prod')
@allowed(['d', 'i', 't', 'u', 'p'])
param envCode string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Azure region short code used in resource names.')
param locationCode string = 'az1'

@description('Project short code.')
param projectCode string = 'ih'

@description('Tags applied to every resource.')
param tags object = {
  project: 'IntegrationHub'
  environment: envCode
  managedBy: 'Bicep'
}

@description('HTTP endpoint for the enrichment function consumed by Logic App.')
param enrichmentFunctionUrl string = 'https://example.invalid/api/events/enrich'

@description('Webhook endpoint used by Logic App for failure notifications.')
param notificationWebhookUrl string = 'https://example.invalid/webhook'

@description('Backend base URL used by APIM policy forwarding.')
param apimBackendUrl string = 'https://example.invalid'

// ── Shared name prefix ────────────────────────────────────────────────────────
// Every module derives resource names from this prefix so naming stays consistent.
var namePrefix = '${envCode}-${locationCode}-${projectCode}'

// ── Module: Monitoring (deploy first — others need the workspace resource ID) ─
module monitoring 'modules/monitoring/monitoring.bicep' = {
  name: 'deploy-monitoring'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

// ── Module: Storage (Function App requires a backing storage account) ─────────
module storage 'modules/storage/stg.bicep' = {
  name: 'deploy-storage'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

// ── Module: Key Vault ─────────────────────────────────────────────────────────
module keyVault 'modules/key-vault/kv.bicep' = {
  name: 'deploy-keyvault'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

// ── Module: Service Bus ───────────────────────────────────────────────────────
module serviceBus 'modules/service-bus/sb.bicep' = {
  name: 'deploy-servicebus'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

// ── Module: Function App ──────────────────────────────────────────────────────
module functionApp 'modules/function-app/func.bicep' = {
  name: 'deploy-functionapp'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    storageAccountName: storage.outputs.storageAccountName
    keyVaultName: keyVault.outputs.keyVaultName
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
  }
}

// ── Module: Logic App ─────────────────────────────────────────────────────────
module logicApp 'modules/logic-app/la.bicep' = {
  name: 'deploy-logicapp'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
    appInsightsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    enrichmentFunctionUrl: enrichmentFunctionUrl
    notificationWebhookUrl: notificationWebhookUrl
  }
}

// ── Module: API Management ────────────────────────────────────────────────────
module apiManagement 'modules/api-management/apim.bicep' = {
  name: 'deploy-apim'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
    appInsightsResourceId: monitoring.outputs.appInsightsResourceId
    appInsightsInstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
    backendUrl: apimBackendUrl
  }
}

// ── RBAC Role Assignments ─────────────────────────────────────────────────────
// Managed Identity for Function App and Logic App — no stored credentials.
// Built-in role IDs are stable GUIDs documented at:
// https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles

var kvSecretsUserRoleId        = '4633458b-17de-408a-b874-0445c86b69e6'  // Key Vault Secrets User
var sbDataReceiverRoleId       = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'  // Azure Service Bus Data Receiver
var sbDataSenderRoleId         = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'  // Azure Service Bus Data Sender
var storageBlobContribRoleId   = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'  // Storage Blob Data Contributor
var storageQueueContribRoleId  = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'  // Storage Queue Data Contributor
var storageTableContribRoleId  = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'  // Storage Table Data Contributor

// Function App — Key Vault Secrets User
resource funcKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namePrefix, 'func-kv', kvSecretsUserRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Function App — Storage Blob Data Contributor (claim-check + idempotency blobs)
resource funcStorageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namePrefix, 'func-stg', storageBlobContribRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobContribRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Function App — Storage Queue Data Contributor
resource funcStorageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namePrefix, 'func-stg-queue', storageQueueContribRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueContribRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Function App — Storage Table Data Contributor
resource funcStorageTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namePrefix, 'func-stg-table', storageTableContribRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableContribRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Function App — Azure Service Bus Data Receiver (reads from DLQ)
resource funcSbReceiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namePrefix, 'func-sb-recv', sbDataReceiverRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sbDataReceiverRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Logic App — Azure Service Bus Data Sender (sends enriched orders to queue)
resource logicAppSbSenderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namePrefix, 'la-sb-send', sbDataSenderRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sbDataSenderRoleId)
    principalId: logicApp.outputs.logicAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Logic App — Key Vault Secrets User (reads webhook URL from Key Vault)
resource logicAppKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namePrefix, 'la-kv', kvSecretsUserRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: logicApp.outputs.logicAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs (referenced by pipelines and other tools) ────────────────────────
output functionAppName string = functionApp.outputs.functionAppName
output logicAppName string = logicApp.outputs.logicAppName
output apimGatewayUrl string = apiManagement.outputs.gatewayUrl
output serviceBusNamespaceName string = serviceBus.outputs.namespaceName
output keyVaultName string = keyVault.outputs.keyVaultName
