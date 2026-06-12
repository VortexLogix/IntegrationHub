// Integration Hub — UAT environment parameters
// Deploys to: u-az1-ih-integration-rg
using '../main.bicep'

param envCode       = 'u'
param location      = 'eastus'
param locationCode  = 'az1'
param projectCode   = 'ih'
param tags          = {
  project: 'IntegrationHub'
  environment: 'uat'
  managedBy: 'Bicep'
  owner: 'vivek-karthikeyan'
}
param enrichmentFunctionUrl = 'https://u-az1-ih-enrichment-func.azurewebsites.net/api/events/enrich'
param notificationWebhookUrl = 'https://u-az1-ih-enrichment-func.azurewebsites.net/api/notifications/failure'
param apimBackendUrl = 'https://u-az1-ih-enrichment-func.azurewebsites.net'
