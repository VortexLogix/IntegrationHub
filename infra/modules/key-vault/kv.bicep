// =============================================================================
// Integration Hub — Key Vault Module
// File        : infra/modules/key-vault/kv.bicep
// Provisions  : Azure Key Vault
//
// Design principles applied here:
//   - No connection strings stored in app settings directly.
//   - Function App and Logic App use Managed Identity to pull secrets.
//   - Key Vault references in app settings: @Microsoft.KeyVault(...)
//
// Name produced: {namePrefix}-app-secrets-kv
// =============================================================================

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Shared name prefix: {env}-az1-ih')
param namePrefix string

@description('Azure region for this resource.')
param location string

@description('Tags applied to the resource.')
param tags object

// ── Resources ─────────────────────────────────────────────────────────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${namePrefix}-app-secrets-kv'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'   // Standard tier is free-tier compatible
    }
    tenantId: tenant().tenantId

    // RBAC-based access model — no legacy access policies.
    // Assign roles (Key Vault Secrets User) to Managed Identities via roleAssignments.
    enableRbacAuthorization: true

    // Soft-delete protects against accidental deletion.
    enableSoftDelete: true
    softDeleteRetentionInDays: 7    // Minimum; use 90 in prod

    publicNetworkAccess: 'Enabled'  // Restrict to VNet in prod hardening

    networkAcls: {
      defaultAction: 'Allow'        // Open for demo environments
      bypass: 'AzureServices'
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Name of the Key Vault.')
output keyVaultName string = keyVault.name

@description('Resource ID of the Key Vault.')
output keyVaultId string = keyVault.id

@description('URI used in Key Vault references: @Microsoft.KeyVault(VaultName=...)')
output keyVaultUri string = keyVault.properties.vaultUri
