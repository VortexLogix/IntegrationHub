// Integration Hub — Integration environment parameters
// Deploys to: i-az1-ih-integration-rg
using '../main.bicep'

param envCode       = 'i'
param location      = 'eastus'
param locationCode  = 'az1'
param projectCode   = 'ih'
param tags          = {
  project: 'IntegrationHub'
  environment: 'integration'
  managedBy: 'Bicep'
  owner: 'vivek-karthikeyan'
}
param enrichmentFunctionUrl = 'https://i-az1-ih-enrichment-func.azurewebsites.net/api/events/enrich'
param notificationWebhookUrl = 'https://i-az1-ih-enrichment-func.azurewebsites.net/api/notifications/failure'
param apimBackendUrl = 'https://i-az1-ih-enrichment-func.azurewebsites.net'
