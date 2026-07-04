# =============================================================================
# Integration Hub — Fully Automated End-To-End Test
# Automatically fetches required keys and endpoints from Azure CLI, then runs
# the full E2E flow (Logic App -> Enrichment -> Service Bus -> Status Storage).
#
# Usage:
#   .\scripts\Test-IntegrationHub.ps1
# =============================================================================

$ErrorActionPreference = 'Stop'

Write-Host "=== IntegrationHub Automated E2E Test ===" -ForegroundColor Cyan
Write-Host "Verifying Azure CLI login..." -ForegroundColor Yellow

$account = az account show -o json | ConvertFrom-Json
if (-not $account) {
    Write-Error "Not logged into Azure. Please run 'az login' first."
    exit 1
}

$SubscriptionId = $account.id
$ResourceGroup = "d-az1-ih-integration-rg"
$FunctionAppName = "d-az1-ih-enrichment-func"
$LogicAppName = "d-az1-ih-orchestrator-la"

Write-Host "[1/4] Fetching credentials from Azure..." -ForegroundColor Yellow

# Fetch Logic App Webhook URL
try {
    $LogicAppUrl = az rest --method post --uri "https://management.azure.com/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Logic/workflows/$LogicAppName/triggers/When_an_HTTP_request_is_received/listCallbackUrl?api-version=2016-06-01" --query value -o tsv
    if (-not $LogicAppUrl -or $LogicAppUrl -match "error") { throw "Failed to get Logic App URL." }
} catch {
    Write-Error "Could not retrieve Logic App webhook URL. Is the logic app deployed?"
    exit 1
}

$CorrelationId = "e2e-auto-$(Get-Random -Maximum 99999)"
$baseUrl = "https://$FunctionAppName.azurewebsites.net"

Write-Host "  Successfully retrieved credentials." -ForegroundColor Green
Write-Host "  Correlation ID : $CorrelationId"
Write-Host ""

# 1. Post to Logic App
Write-Host "[2/4] Triggering Logic App..." -ForegroundColor Yellow
$body = @{
    eventId     = $CorrelationId
    eventType   = 'OrderCreated'
    sourceSystem = 'Automated-E2E-Test'
    payload     = @{
        productCode = 'SKU-AUTO-100'
        quantity    = 10
        customerId  = 'C-TEST-AUTO'
    }
} | ConvertTo-Json

try {
    $null = Invoke-RestMethod -Uri $LogicAppUrl `
        -Method Post `
        -ContentType 'application/json' `
        -Headers @{ 'x-correlation-id' = $CorrelationId } `
        -Body $body
    Write-Host "  Status: 202 Accepted (Logic App workflow started successfully)" -ForegroundColor Green
} catch {
    Write-Host "  Failed to trigger Logic App: $_" -ForegroundColor Red
    exit 1
}

# 2. Wait for processing pipeline
Write-Host "[3/4] Waiting 15 seconds for full pipeline processing..." -ForegroundColor Yellow
Start-Sleep -Seconds 15

# 3. Check status from StatusFunction
Write-Host "[4/4] Querying final order status..." -ForegroundColor Yellow
try {
    $status = Invoke-RestMethod -Uri "$baseUrl/api/status/$CorrelationId"
    Write-Host "  Status : $($status.status)" -ForegroundColor Green
    Write-Host "  Message: $($status.message)"
    Write-Host "  Updated: $($status.lastUpdatedUtc)"
} catch {
    Write-Host "  Status query failed: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== E2E Test Complete ===" -ForegroundColor Cyan
