# =============================================================================
# Integration Hub — Demo Smoke Test
# Sends a sample order event and queries its status.
#
# Usage:
#   1. Get your function key from:
#      Azure Portal → d-az1-ih-enrichment-func → App keys → default
#   2. Run: .\scripts\demo.ps1 -FunctionKey "YOUR_KEY_HERE"
# =============================================================================

param(
    [Parameter(Mandatory = $true, HelpMessage = "Function host key from Azure Portal → Function App → App keys → default")]
    [string]$FunctionKey,

    [Parameter(Mandatory = $false)]
    [string]$FunctionAppName = 'd-az1-ih-enrichment-func',

    [Parameter(Mandatory = $false)]
    [string]$CorrelationId = "demo-$(Get-Random -Maximum 99999)"
)

$ErrorActionPreference = 'Stop'

$baseUrl = "https://$FunctionAppName.azurewebsites.net"

Write-Host "=== IntegrationHub Demo ===" -ForegroundColor Cyan
Write-Host "Function App : $FunctionAppName"
Write-Host "Correlation  : $CorrelationId"
Write-Host ""

# ── 1. Post an order event ───────────────────────────────────────────────
Write-Host "[1/3] Posting order event..." -ForegroundColor Yellow
$body = @{
    eventId     = $CorrelationId
    eventType   = 'OrderCreated'
    sourceSystem = 'Demo'
    payload     = @{
        productCode = 'SKU-DEMO-001'
        quantity    = 2
        customerId  = 'C-DEMO-001'
    }
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/events/enrich" `
        -Method Post `
        -ContentType 'application/json' `
        -Headers @{
            'x-correlation-id' = $CorrelationId
            'x-functions-key'  = $FunctionKey
        } `
        -Body $body
    Write-Host "  Status: 200" -ForegroundColor Green
    Write-Host "  Body: $($response | ConvertTo-Json -Compress)" -ForegroundColor Gray
    Write-Host "  (Event submitted successfully)" -ForegroundColor Green
} catch {
    $sc = $_.Exception.Response.StatusCode.value__
    Write-Host "  HTTP $sc" -ForegroundColor Green
    $reader = $null
    try { $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream()) } catch {}
    if ($reader -ne $null) {
        Write-Host "  Response: $($reader.ReadToEnd())" -ForegroundColor Gray
        $reader.Close()
    }
    if ($sc -eq 202) {
        Write-Host "  (Duplicate - idempotency working)" -ForegroundColor Yellow
    } elseif ($sc -ne 200) {
        Write-Host "  Unexpected status" -ForegroundColor Red
        Write-Host ""
        Write-Host "Hint: Get the function key from:" -ForegroundColor Yellow
        Write-Host "  Azure Portal → $FunctionAppName → App keys → default" -ForegroundColor Yellow
        exit 1
    }
}

# ── 2. Wait for processing ───────────────────────────────────────────────
Write-Host "[2/3] Waiting 5 seconds for processing..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

# ── 3. Check status ──────────────────────────────────────────────────────
Write-Host "[3/3] Querying order status..." -ForegroundColor Yellow
try {
    $status = Invoke-RestMethod -Uri "$baseUrl/api/status/$CorrelationId" `
        -Headers @{ 'x-functions-key' = $FunctionKey }
    Write-Host "  Status : $($status.status)" -ForegroundColor Green
    Write-Host "  Message: $($status.message)"
    Write-Host "  Updated: $($status.lastUpdatedUtc)"
} catch {
    Write-Host "  Status query failed: $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Demo complete ===" -ForegroundColor Cyan
Write-Host "Correlation ID: $CorrelationId"
