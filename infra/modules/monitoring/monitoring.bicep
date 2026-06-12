// =============================================================================
// Integration Hub — Monitoring Module
// File        : infra/modules/monitoring/monitoring.bicep
// Provisions  : Log Analytics Workspace  +  Application Insights
//
// Deployed first because every other module references the workspace ID
// and the App Insights connection string.
//
// Names produced
//   Log Analytics : {namePrefix}-workspace-law
//   App Insights  : {namePrefix}-telemetry-ai
// =============================================================================

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Shared name prefix: {env}-az1-ih')
param namePrefix string

@description('Azure region for all resources in this module.')
param location string

@description('Tags applied to every resource.')
param tags object

// ── Resources ─────────────────────────────────────────────────────────────────

// Log Analytics Workspace
// All Application Insights telemetry is routed here for centralised querying.
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-workspace-law'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018' // Pay-per-GB; free tier covers 5 GB/month
    }
    retentionInDays: 30  // Minimum; increase to 90 for UAT/prod if needed
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Application Insights
// Connected to the workspace above so all traces and metrics land in one place.
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-telemetry-ai'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    RetentionInDays: 30
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Resource ID of the Log Analytics Workspace.')
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id

@description('Resource ID of the Application Insights instance.')
output appInsightsResourceId string = appInsights.id

@description('Instrumentation key (used by APIM logger).')
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey

@description('Connection string used by Function App and Logic App.')
output appInsightsConnectionString string = appInsights.properties.ConnectionString
