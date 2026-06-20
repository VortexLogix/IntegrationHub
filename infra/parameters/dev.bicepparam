// Integration Hub — Development environment parameters
// Deploys to: d-az1-ih-integration-rg
using '../main.bicep'

param envCode       = 'd'
param location      = 'centralus'
param locationCode  = 'az1'
param projectCode   = 'ih'
param tags          = {
  project: 'IntegrationHub'
  environment: 'development'
  managedBy: 'Bicep'
  owner: 'vivek-karthikeyan'
}
param enrichmentFunctionUrl = 'https://d-az1-ih-enrichment-func.azurewebsites.net/api/events/enrich'
param notificationWebhookUrl = 'https://d-az1-ih-enrichment-func.azurewebsites.net/api/notifications/failure'
param apimBackendUrl = 'https://d-az1-ih-enrichment-func.azurewebsites.net'
