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
