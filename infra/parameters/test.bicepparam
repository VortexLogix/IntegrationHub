// Integration Hub — Test environment parameters
// Deploys to: t-az1-ih-integration-rg
using '../main.bicep'

param envCode       = 't'
param location      = 'eastus'
param locationCode  = 'az1'
param projectCode   = 'ih'
param tags          = {
  project: 'IntegrationHub'
  environment: 'test'
  managedBy: 'Bicep'
  owner: 'vivek-karthikeyan'
}
param enrichmentFunctionUrl = 'https://t-az1-ih-enrichment-func.azurewebsites.net/api/events/enrich'
param notificationWebhookUrl = 'https://t-az1-ih-enrichment-func.azurewebsites.net/api/notifications/failure'
param apimBackendUrl = 'https://t-az1-ih-enrichment-func.azurewebsites.net'
