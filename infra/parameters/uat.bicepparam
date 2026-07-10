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
