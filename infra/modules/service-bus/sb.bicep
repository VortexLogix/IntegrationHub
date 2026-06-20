// =============================================================================
// Integration Hub — Service Bus Module
// File        : infra/modules/service-bus/sb.bicep
// Provisions  : Service Bus Namespace  +  orders queue  +  dead-letter queue
//               (dead-letter is automatic on every Service Bus queue)
//
// Tier choice : Basic — free tier compatible (10 M ops first 12 months).
//               Upgrade to Standard if Topics/Subscriptions are needed later.
//
// Name produced: {namePrefix}-order-events-ns
// =============================================================================

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Shared name prefix: {env}-az1-ih')
param namePrefix string

@description('Azure region for this resource.')
param location string

@description('Tags applied to the resource.')
param tags object

// ── Resources ─────────────────────────────────────────────────────────────────

// Service Bus Namespace
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${namePrefix}-order-events-ns'
  location: location
  tags: tags
  sku: {
    name: 'Basic'    // Free-tier compatible. Change to 'Standard' for topics.
    tier: 'Basic'
  }
  properties: {
    minimumTlsVersion: '1.2'
    disableLocalAuth: false   // Keep enabled; Managed Identity preferred but
                              // local auth needed for Logic App connections.
  }
}

// Orders queue — primary intake for enriched order events
resource ordersQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'orders'
  properties: {
    maxDeliveryCount: 3           // Retry 3 times before moving to dead-letter
    lockDuration: 'PT1M'          // 1-minute lock; enough for enrichment processing
    defaultMessageTimeToLive: 'P1D' // Messages expire after 1 day if unprocessed
    deadLetteringOnMessageExpiration: true
    enablePartitioning: false     // Not supported on Basic tier
  }
}

// ERP delivery queue — messages destined for the downstream ERP system
resource erpDeliveryQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'erp-delivery'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT2M'
    defaultMessageTimeToLive: 'P1D'
    deadLetteringOnMessageExpiration: true
    enablePartitioning: false
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Service Bus namespace name — used in connection strings and Key Vault secrets.')
output namespaceName string = serviceBusNamespace.name

@description('Resource ID of the Service Bus namespace.')
output namespaceId string = serviceBusNamespace.id

@description('Name of the primary orders intake queue.')
output ordersQueueName string = ordersQueue.name

@description('Name of the ERP delivery queue.')
output erpDeliveryQueueName string = erpDeliveryQueue.name
