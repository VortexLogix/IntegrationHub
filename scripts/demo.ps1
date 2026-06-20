# =============================================================================
# Integration Hub — Demo Smoke Test
# Sends a sample order event and queries its status.
# =============================================================================

param(
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
        -Headers @{ 'x-correlation-id' = $CorrelationId } `
        -Body $body `
        -SkipCertificateCheck

    Write-Host "  Status: $($response.statusCode)" -ForegroundColor Green
    Write-Host "  Body: $($response | ConvertTo-Json -Compress)" -ForegroundColor Gray
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    Write-Host "  HTTP $statusCode" -ForegroundColor Green
    try {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $errorBody = $reader.ReadToEnd()
        Write-Host "  Response: $errorBody" -ForegroundColor Gray
    } catch {
        Write-Host "  (no response body)" -ForegroundColor Gray
    }

    if ($statusCode -eq 202) {
        Write-Host "  (Duplicate — idempotency working)" -ForegroundColor Yellow
    } elseif ($statusCode -ne 200) {
        Write-Host "  Unexpected status" -ForegroundColor Red
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
        -SkipCertificateCheck

    Write-Host "  Status : $($status.status)" -ForegroundColor Green
    Write-Host "  Message: $($status.message)"
    Write-Host "  Updated: $($status.lastUpdatedUtc)"
} catch {
    Write-Host "  Status query failed: $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Demo complete ===" -ForegroundColor Cyan
Write-Host "Correlation ID: $CorrelationId"
