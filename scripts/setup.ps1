# =============================================================================
# Integration Hub - One-time Subscription Bootstrap
# Run this ONCE per subscription before deploying via pipeline.
# =============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$ClientId,

    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId
)

$ErrorActionPreference = 'Stop'

Write-Host "=== IntegrationHub Bootstrap ===" -ForegroundColor Cyan
Write-Host ""

# -- 0. Validate Azure login --------------------------------------------------
Write-Host "[1/6] Checking Azure login..." -ForegroundColor Yellow
$account = az account show 2>$null
if (-not $account) {
    Write-Error "Not logged into Azure. Run 'az login' first."
    exit 1
}
Write-Host "  Logged in as $(($account | ConvertFrom-Json).user.name)" -ForegroundColor Green

# Set the correct subscription context so all subsequent commands operate on
# the right subscription, not other subscriptions
Write-Host "  Setting active subscription to $SubscriptionId..."
az account set --subscription $SubscriptionId | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to set subscription '$SubscriptionId'. Check that the ID is correct and you have access."
    exit 1
}
Write-Host "  Subscription set" -ForegroundColor Green

# -- 1. Register resource providers ------------------------------------------
Write-Host "[2/6] Registering resource providers..." -ForegroundColor Yellow
$rps = @(
    'Microsoft.AlertsManagement',
    'Microsoft.Storage',
    'Microsoft.ApiManagement',
    'Microsoft.Web',
    'Microsoft.Insights',
    'Microsoft.OperationalInsights',
    'Microsoft.ServiceBus',
    'Microsoft.KeyVault',
    'Microsoft.Logic'
)

foreach ($rp in $rps) {
    $state = az provider show --namespace $rp --query "registrationState" -o tsv 2>$null

    if ($state -eq 'Registered') {
        Write-Host "  $rp already registered" -ForegroundColor Green
    }
    elseif ($state -eq 'Registering') {
        Write-Host "  $rp is already registering (async - may take a few minutes)" -ForegroundColor Yellow
    }
    else {
        Write-Host "  Registering $rp..."
        az provider register --namespace $rp | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "  Failed to register provider '$rp'. Ensure the account has 'Microsoft.Support/register/action' permission."
            exit 1
        }
        Write-Host "  $rp registration initiated" -ForegroundColor Green
    }
}

# -- 2. Create service principal from app registration -----------------------
Write-Host "[3/6] Creating service principal for $ClientId..." -ForegroundColor Yellow
$spCheck = az ad sp show --id $ClientId 2>$null
if ($spCheck) {
    Write-Host "  Service principal already exists - skipping creation" -ForegroundColor Green
}
else {
    az ad sp create --id $ClientId | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Service principal created" -ForegroundColor Green
    }
    else {
        Write-Error "  Failed to create service principal for $ClientId"
        exit 1
    }
}

# -- 3. Assign Contributor ---------------------------------------------------
Write-Host "[4/6] Assigning Contributor role..." -ForegroundColor Yellow
$existing = az role assignment list --assignee $ClientId --role "Contributor" --scope "/subscriptions/$SubscriptionId" -o tsv 2>$null
if (-not $existing) {
    az role assignment create --assignee $ClientId --role "Contributor" --scope "/subscriptions/$SubscriptionId" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "  Failed to assign Contributor role to $ClientId"
        exit 1
    }
    Write-Host "  Contributor role assigned" -ForegroundColor Green
}
else {
    Write-Host "  Contributor role already assigned" -ForegroundColor Green
}

# -- 4. Assign User Access Administrator ------------------------------------
Write-Host "[5/6] Assigning User Access Administrator role..." -ForegroundColor Yellow
$existing = az role assignment list --assignee $ClientId --role "User Access Administrator" --scope "/subscriptions/$SubscriptionId" -o tsv 2>$null
if (-not $existing) {
    az role assignment create --assignee $ClientId --role "User Access Administrator" --scope "/subscriptions/$SubscriptionId" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "  Failed to assign User Access Administrator role to $ClientId"
        exit 1
    }
    Write-Host "  User Access Administrator role assigned" -ForegroundColor Green
}
else {
    Write-Host "  User Access Administrator role already assigned" -ForegroundColor Green
}

# -- 5. Purge soft-deleted APIM ---------------------------------------------
Write-Host "[6/6] Purging soft-deleted APIM resources..." -ForegroundColor Yellow
$deletedJson = az apim deletedservice list -o json 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Could not list soft-deleted APIM services (may require Contributor). Skipping." -ForegroundColor Yellow
}
else {
    $deleted = if ($deletedJson) { $deletedJson | ConvertFrom-Json } else { @() }

    $purged = 0
    foreach ($svc in $deleted) {
        if ($svc.name -like '*ih*') {
            Write-Host "  Purging $($svc.name) in $($svc.location)..."
            az apim deletedservice purge --service-name $svc.name --location $svc.location
            if ($LASTEXITCODE -eq 0) {
                Write-Host "    Purged" -ForegroundColor Green
            }
            else {
                Write-Host "    Skip (not found or already purged)" -ForegroundColor Yellow
            }
            $purged++
        }
    }

    if ($purged -eq 0) {
        Write-Host "  No soft-deleted APIM resources matching '*ih*' found" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=== Bootstrap complete ===" -ForegroundColor Cyan
Write-Host "Next: Trigger the pipeline from GitHub Actions."