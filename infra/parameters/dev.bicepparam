// Integration Hub — Development environment parameters
// Deploys to: d-az1-ih-integration-rg
using '../main.bicep'

param envCode       = 'd'
param location      = 'eastus'
param locationCode  = 'az1'
param projectCode   = 'ih'
param tags          = {
  project: 'IntegrationHub'
  environment: 'development'
  managedBy: 'Bicep'
  owner: 'vivek-karthikeyan'
}
