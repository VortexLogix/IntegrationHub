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
