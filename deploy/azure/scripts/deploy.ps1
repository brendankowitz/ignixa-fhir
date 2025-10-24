# -------------------------------------------------------------------------------------------------
# Copyright (c) Ignixa Contributors. All rights reserved.
# Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
# -------------------------------------------------------------------------------------------------
#
# Azure Deployment Script for FHIR Server
#
# Description: Deploys the FHIR Server infrastructure to Azure using Bicep templates
#
# Prerequisites:
#   - Azure CLI (az command) installed and updated
#   - Azure subscription configured with 'az login'
#   - Appropriate permissions (Contributor or Owner role on subscription)
#   - Bicep CLI (included with Azure CLI 2.20.0+)
#
# Usage:
#   .\deploy.ps1 -Environment dev -ResourceGroup fhir-dev-rg -Subscription "My Subscription"
#   .\deploy.ps1 -Environment production -ResourceGroup fhir-prod-rg
#

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('dev', 'production')]
    [string]$Environment,

    [Parameter(Mandatory=$true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory=$false)]
    [string]$Subscription,

    [Parameter(Mandatory=$false)]
    [string]$Location = 'eastus',

    [Parameter(Mandatory=$false)]
    [string]$TemplateFile = '../main.bicep',

    [Parameter(Mandatory=$false)]
    [string]$ParameterFile = "../parameters/$Environment.bicepparam"
)

# Enable strict error handling
$ErrorActionPreference = 'Stop'
$WarningPreference = 'Continue'

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$TemplateDir = Join-Path $ScriptDir '..'

# Convert relative paths to absolute
$TemplateFile = Join-Path $TemplateDir $TemplateFile
$ParameterFile = Join-Path $TemplateDir $ParameterFile

Write-Host "╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║          FHIR Server Azure Deployment Script                  ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ========================================
# 1. Validate Prerequisites
# ========================================
Write-Host "[1/5] Validating prerequisites..." -ForegroundColor Yellow

# Check if Azure CLI is installed
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI not found. Please install Azure CLI: https://aka.ms/cli"
}

# Check if logged in to Azure
$Account = az account show --query user.name -o tsv 2>$null
if ($null -eq $Account) {
    Write-Error "Not logged in to Azure. Run 'az login' first."
}
Write-Host "✓ Azure CLI found, authenticated as: $Account" -ForegroundColor Green

# Check if template files exist
if (-not (Test-Path $TemplateFile)) {
    Write-Error "Template file not found: $TemplateFile"
}
Write-Host "✓ Template file found: $(Split-Path $TemplateFile -Leaf)" -ForegroundColor Green

if (-not (Test-Path $ParameterFile)) {
    Write-Error "Parameter file not found: $ParameterFile"
}
Write-Host "✓ Parameter file found: $(Split-Path $ParameterFile -Leaf)" -ForegroundColor Green

# ========================================
# 2. Select Subscription
# ========================================
Write-Host ""
Write-Host "[2/5] Setting subscription..." -ForegroundColor Yellow

if ($Subscription) {
    az account set --subscription $Subscription
    Write-Host "✓ Subscription set to: $Subscription" -ForegroundColor Green
} else {
    $CurrentSubscription = az account show --query name -o tsv
    Write-Host "✓ Using current subscription: $CurrentSubscription" -ForegroundColor Green
}

# ========================================
# 3. Create Resource Group
# ========================================
Write-Host ""
Write-Host "[3/5] Creating/verifying resource group..." -ForegroundColor Yellow

$RgExists = az group exists --name $ResourceGroup
if ($RgExists -eq 'false') {
    Write-Host "Creating resource group: $ResourceGroup in $Location"
    az group create --name $ResourceGroup --location $Location | Out-Null
}
Write-Host "✓ Resource group ready: $ResourceGroup" -ForegroundColor Green

# ========================================
# 4. Validate Bicep Template
# ========================================
Write-Host ""
Write-Host "[4/5] Validating Bicep template..." -ForegroundColor Yellow

# Build and validate Bicep template
$ValidationResult = az deployment group validate `
    --resource-group $ResourceGroup `
    --template-file $TemplateFile `
    --parameters $ParameterFile `
    --query "properties.validationErrors" `
    -o json

if ($ValidationResult -and $ValidationResult -ne '[]') {
    Write-Error "Template validation failed: $ValidationResult"
}
Write-Host "✓ Template validation successful" -ForegroundColor Green

# ========================================
# 5. Deploy Infrastructure
# ========================================
Write-Host ""
Write-Host "[5/5] Deploying infrastructure..." -ForegroundColor Yellow
Write-Host "This may take 10-20 minutes..." -ForegroundColor Cyan

$DeploymentName = "fhir-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

$Deployment = az deployment group create `
    --name $DeploymentName `
    --resource-group $ResourceGroup `
    --template-file $TemplateFile `
    --parameters $ParameterFile `
    --output json | ConvertFrom-Json

if ($Deployment.properties.provisioningState -ne 'Succeeded') {
    Write-Error "Deployment failed with state: $($Deployment.properties.provisioningState)"
}

Write-Host "✓ Deployment completed successfully" -ForegroundColor Green

# ========================================
# 6. Output Deployment Results
# ========================================
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Deployment Outputs:" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

$Outputs = $Deployment.properties.outputs
if ($Outputs) {
    $Outputs | Get-Member -MemberType NoteProperty | ForEach-Object {
        $OutputName = $_.Name
        $OutputValue = $Outputs.$OutputName.value
        Write-Host ""
        Write-Host "$OutputName" -ForegroundColor Yellow
        Write-Host "  $OutputValue" -ForegroundColor White
    }
}

# ========================================
# 7. Next Steps
# ========================================
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Set up SQL Database for Managed Identity:" -ForegroundColor Yellow
Write-Host "   - Connect to SQL Server using Azure AD authentication" -ForegroundColor Gray
Write-Host "   - Run: deploy/azure/scripts/setup-sql-mi.sql" -ForegroundColor Gray
Write-Host "   - Replace 'fhir-prod-yourorg' with your App Service name" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Deploy FHIR Server application:" -ForegroundColor Yellow
Write-Host "   - Build Docker image or publish .NET application" -ForegroundColor Gray
Write-Host "   - Deploy to the created App Service" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Configure application settings:" -ForegroundColor Yellow
Write-Host "   - Add connection strings and secrets to Key Vault" -ForegroundColor Gray
Write-Host "   - Update App Service configuration with Key Vault references" -ForegroundColor Gray
Write-Host ""
Write-Host "4. Test endpoints:" -ForegroundColor Yellow
Write-Host "   - https://$($Outputs.appServiceName.value).azurewebsites.net/metadata" -ForegroundColor Gray
Write-Host ""

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "✓ FHIR Server infrastructure deployed successfully!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Green
