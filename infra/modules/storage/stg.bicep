// =============================================================================
// Integration Hub — Storage Module
// File        : infra/modules/storage/stg.bicep
// Provisions  : Azure Storage Account (backs the Function App runtime)
//
// Storage Account names CANNOT contain hyphens.
// Strip hyphens from namePrefix before appending the suffix.
//
// Name produced: {namePrefix stripped}-funcdatastg
// Example      : daz1ihfuncdatastg
// =============================================================================

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Shared name prefix: {env}-az1-ih')
@minLength(1)
param namePrefix string

@description('Azure region for this resource.')
param location string

@description('Tags applied to the resource.')
param tags object

// ── Variables ─────────────────────────────────────────────────────────────────

// Storage Account names: 3–24 chars, lowercase alphanumeric only — no hyphens.
// take() omitted because namePrefix is always ≤7 chars (e.g. d-az1-ih),
// so the combined name never exceeds the 24-char limit.
var storageAccountName = replace('${namePrefix}funcdatastg', '-', '')

// ── Resources ─────────────────────────────────────────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'  // Locally redundant; cheapest option, fine for demo
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true   // Enforce HTTPS — never allow plain HTTP
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false     // No anonymous blob access
    networkAcls: {
      defaultAction: 'Allow'         // Open for demo; lock down in prod
    }
  }
}

// Blob service — needed explicitly to create containers below
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7   // Soft-delete for 7 days — recoverable accidental deletions
    }
  }
}

// Claim-check pattern container:
// Large payloads are stored here; only a reference is sent on the queue.
resource claimCheckContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'claim-check-payloads'
  properties: {
    publicAccess: 'None'
  }
}

// Idempotency store container:
// Function checks this before processing to skip duplicate messages.
resource idempotencyContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'idempotency-store'
  properties: {
    publicAccess: 'None'
  }
}

// Table service — needed to create status tracking table
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

// Order status table: tracks enrichment and ERP delivery status per correlationId
resource statusTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: 'OrderStatus'
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Name of the Storage Account (used by Function App settings).')
output storageAccountName string = storageAccount.name

@description('Resource ID of the Storage Account.')
output storageAccountId string = storageAccount.id
