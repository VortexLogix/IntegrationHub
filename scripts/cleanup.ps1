# =============================================================================
# Integration Hub — Kill Switch
# Deletes the entire dev resource group and purges soft-deleted resources.
# After running this, re-run setup.ps1, then trigger the pipeline.
# =============================================================================

param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = 'd-az1-ih-integration-rg',

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

Write-Host "=== IntegrationHub Kill Switch ===" -ForegroundColor Red
Write-Host ""

if (-not $Force) {
    Write-Host "This will DELETE the resource group '$ResourceGroup' and all its resources." -ForegroundColor Red
    Write-Host "This action is IRREVERSIBLE." -ForegroundColor Red
    $confirm = Read-Host "Type the resource group name to confirm (or press Enter to cancel)"
    if ($confirm -ne $ResourceGroup) {
        Write-Host "Cancelled." -ForegroundColor Yellow
        exit 0
    }
}

# ── 1. Delete resource group ──────────────────────────────────────────────
Write-Host "[1/3] Deleting resource group '$ResourceGroup'..." -ForegroundColor Yellow
az group delete --name $ResourceGroup --yes --no-wait
Write-Host "  Resource group deletion started (async)" -ForegroundColor Green

# ── 2. Purge soft-deleted APIM ───────────────────────────────────────────
Write-Host "[2/3] Purging soft-deleted APIM resources..." -ForegroundColor Yellow
$deleted = az apim deletedservice list -o json 2>$null | ConvertFrom-Json
foreach ($svc in $deleted) {
    if ($svc.name -like '*ih*') {
        Write-Host "  Purging $($svc.name) in $($svc.location)..."
        az apim deletedservice purge --service-name $svc.name --location $svc.location 2>$null
    }
}
Write-Host "  Purge complete" -ForegroundColor Green

# ── 3. Verify resource group is gone ─────────────────────────────────────
Write-Host "[3/3] Waiting for resource group deletion..." -ForegroundColor Yellow
do {
    Start-Sleep -Seconds 10
    $exists = az group exists --name $ResourceGroup -o tsv
} while ($exists -eq 'true')
Write-Host "  Resource group confirmed deleted" -ForegroundColor Green

Write-Host ""
Write-Host "=== Kill switch complete ===" -ForegroundColor Cyan
Write-Host "Run scripts/setup.ps1, then trigger the pipeline from GitHub Actions."
