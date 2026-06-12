// Integration Hub — Production environment parameters
// Deploys to: p-az1-ih-integration-rg
using '../main.bicep'

param envCode       = 'p'
param location      = 'eastus'
param locationCode  = 'az1'
param projectCode   = 'ih'
param tags          = {
  project: 'IntegrationHub'
  environment: 'production'
  managedBy: 'Bicep'
  owner: 'vivek-karthikeyan'
}
param enrichmentFunctionUrl = 'https://p-az1-ih-enrichment-func.azurewebsites.net/api/events/enrich'
param notificationWebhookUrl = 'https://p-az1-ih-enrichment-func.azurewebsites.net/api/notifications/failure'
param apimBackendUrl = 'https://p-az1-ih-enrichment-func.azurewebsites.net'
