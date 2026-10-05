# FHIR Server Azure Deployment Guide

This directory contains Azure Infrastructure as Code (IaC) templates for deploying the Ignixa FHIR Server to Microsoft Azure using Bicep.

## Overview

The deployment uses **Bicep templates** or **ARM JSON templates** with **Managed Identity** for secure, passwordless authentication across all Azure services:

- **App Service (Linux)**: Runs the FHIR Server Docker container with System-Assigned Managed Identity
- **Azure SQL Server**: Shared SQL server for all tenants
- **Tenant Databases**: ARM creates the single `FhirDatabase` catalog; Bicep supports 1-50 tenant databases
- **Blob Storage (2 accounts)**: FHIR data storage + DurableTask orchestration backend
- **Application Insights**: Application monitoring and logging
- **Log Analytics**: Centralized logging workspace
- **Docker/GHCR Support**: Configured to pull Docker images from GitHub Container Registry (public, no credentials needed)

## Multi-Tenant Support

The **Bicep** deployment supports **1-50 tenants**, where each tenant gets its own isolated SQL database:

- **Tenant 0**: System partition (reserved for transaction IDs) - shares database with Tenant 1
- **Tenant 1-N**: Active tenants, each with their own database (`FhirTenant1`, `FhirTenant2`, etc.)
- **Configuration**: Automatically injected into App Service as environment variables
- **Authentication**: All tenants use Managed Identity for SQL access

The single-tenant **ARM** template instead creates `FhirDatabase` for tenant 1; tenant 0 inherits
that same catalog. Do not substitute Bicep's `FhirTenant1` name into an ARM deployment.

## Deployment Options

### Option 1: ARM Template (JSON) - Single Tenant ⚡

Use the consolidated `azuredeploy.json` template for **single-tenant** deployments:

```bash
az deployment group create \
  --resource-group fhir-dev-rg \
  --template-file azuredeploy.json \
  --parameters azuredeploy.parameters.json
```

**Best for**: Single tenant deployments, CI/CD pipelines, users familiar with ARM templates

**Note**: ARM JSON templates only support single-tenant deployments. For multi-tenant (2+ tenants), use Bicep (Option 2).

### Option 2: Bicep Modules (Modular IaC) 🔧 Multi-Tenant Support

Use the modular Bicep templates for multi-tenant deployments (1-50 tenants):

```bash
az deployment group create \
  --resource-group fhir-dev-rg \
  --template-file main.bicep \
  --parameters appName=ignixa-demo tenantCount=10
```

**Best for**: Multi-tenant deployments (2+ tenants), advanced scenarios, easier to maintain and customize

**Features**:
- Dynamic tenant configuration generation (1-50 tenants)
- Automatic app settings injection for all tenants
- Cleaner syntax and better readability

## Directory Structure

```
deploy/azure/
├── azuredeploy.json                    # ⚡ ARM template (consolidated, single-file)
├── azuredeploy.parameters.json         # ARM template parameters
├── main.bicep                          # Main Bicep orchestration template
├── modules/
│   ├── app-service.bicep              # App Service + Plan + MI
│   ├── sql-database.bicep             # Azure SQL Server + Database
│   ├── storage.bicep                  # Blob Storage + Containers
│   ├── key-vault.bicep                # Key Vault + RBAC roles
│   ├── monitoring.bicep               # Application Insights + Log Analytics
│   └── role-assignments.bicep         # Cross-service RBAC configuration
├── parameters/
│   ├── dev.bicepparam                 # Development environment parameters
│   └── production.bicepparam          # Production environment parameters
├── scripts/
│   ├── deploy.ps1                     # PowerShell deployment script
│   ├── setup-sql-mi.sql               # SQL Managed Identity configuration
│   └── deploy.sh                      # Bash deployment script (optional)
└── README.md                          # This file
```

## Prerequisites

### Tools Required

1. **Azure CLI** (v2.20.0 or later)
   - Download: https://aka.ms/cli
   - Includes Bicep CLI automatically

2. **PowerShell** (7.0+ recommended for cross-platform)
   - Or use Azure CLI directly for deployment

3. **Azure Account**
   - Active Azure subscription with appropriate permissions
   - Resource group creation permissions

### Azure Permissions Required

- **Minimum Role**: Contributor on subscription or resource group
- **Required Actions**:
  - Create/modify App Service
  - Create/modify SQL Database
  - Create/modify Storage Account
  - Create/modify Key Vault
  - Create RBAC role assignments

### Azure AD Configuration

- **SQL Server Admin**: Must be an Azure AD user or service principal
- Provide the object ID during deployment (optional but recommended)

## Quick Start (ARM Template)

### 1. Login to Azure

```powershell
# Login to Azure
az login

# Optionally specify subscription
az account set --subscription "My Subscription Name"
```

### 2. Update Parameter File

Edit `azuredeploy.parameters.json` with your values:

```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "appName": {
      "value": "ignixa-fhir-demo"  // Must be globally unique (3-24 chars)
    },
    "dockerRegistryUrl": {
      "value": "https://ghcr.io"  // GitHub Container Registry (public, no auth needed)
    },
    "dockerImage": {
      "value": "brendankowitz/ignixa-fhir"  // GitHub repo path
    },
    "dockerImageTag": {
      "value": "latest"  // Image tag (e.g., latest, v1.0.0, main)
    },
    "environment": {
      "value": "production"  // development, staging, or production
    },
    "fhirVersion": {
      "value": "4.3"  // 3.0.2 (STU3), 4.0 (R4), 4.3 (R4B), 5.0 (R5), 6.0 (R6)
    }
  }
}
```

**Get your Azure AD Object ID** (optional, for SQL admin):
```bash
az ad signed-in-user show --query objectId -o tsv
```

### 3. Create Resource Group

```bash
az group create \
  --name ignixa-fhir-rg \
  --location eastus
```

### 4. Deploy Infrastructure

**Single-Tenant Deployment** (using ARM Template):

```bash
az deployment group create \
  --resource-group ignixa-fhir-rg \
  --template-file azuredeploy.json \
  --parameters azuredeploy.parameters.json
```

**Multi-Tenant Deployment** (using Bicep - **required** for 2+ tenants):

Deploy with 10 tenants:

```bash
az deployment group create \
  --resource-group ignixa-fhir-rg \
  --template-file main.bicep \
  --parameters appName=ignixa-fhir-demo tenantCount=10 fhirVersion=4.0
```

Deploy with 50 tenants (maximum supported):

```bash
az deployment group create \
  --resource-group ignixa-fhir-rg \
  --template-file main.bicep \
  --parameters appName=ignixa-fhir-prod tenantCount=50 fhirVersion=4.0
```

**Using Parameters File**:

Edit `azuredeploy.parameters.json` to set `tenantCount`:

```json
{
  "parameters": {
    "appName": { "value": "ignixa-fhir-demo" },
    "tenantCount": { "value": 10 },
    "fhirVersion": { "value": "4.0" }
  }
}
```

Then deploy:

```bash
az deployment group create \
  --resource-group ignixa-fhir-rg \
  --template-file main.bicep \
  --parameters @azuredeploy.parameters.json
```

**OR using PowerShell script**:
```powershell
cd scripts
.\deploy.ps1 -Environment production `
    -ResourceGroup ignixa-fhir-rg `
    -Location eastus
```

Deployment takes approximately **5-10 minutes** for single tenant, **10-20 minutes** for 50 tenants.

### 5. Configure GHCR Authentication

The Docker image is hosted on **GitHub Container Registry (GHCR)** as a public image.

**No authentication required** - Leave `dockerRegistryUsername` and `dockerRegistryPassword` empty in the parameters file:

```json
"dockerRegistryUsername": {
  "value": ""
},
"dockerRegistryPassword": {
  "value": ""
}
```

The public image is automatically pulled without credentials.

### 6. Use Pre-Built Docker Image from GHCR

The Docker image is automatically pulled from GitHub Container Registry during deployment:

```bash
# The image is pulled from: ghcr.io/brendankowitz/ignixa-fhir:TAG
# No build/push steps needed for deployment!
```

The public image is built and pushed automatically by GitHub Actions on every commit. You can find the latest image at:
- **Repository**: https://github.com/brendankowitz/ignixa-fhir
- **Image**: `ghcr.io/brendankowitz/ignixa-fhir`
- **Available tags**: `latest`, `release`, version tags (e.g., `v1.0.0`)

To use a different image tag, update `dockerImageTag` in your parameters file.

### 7. Restart App Service (to pull new image)

After pushing a new Docker image, restart the App Service to pull and run it:

```bash
az webapp restart \
  --resource-group ignixa-fhir-rg \
  --name ignixa-fhir-demo
```

### 8. Database Schema Deployment

**Schema deployment is opt-in, not automatic.** `SchemaDeployer` only deploys or upgrades a tenant's
schema when `SqlServer:AutomaticSchemaDeploymentEnabled` is `true`; it defaults to `false`
(`SqlServerOptions.cs`) precisely so a deployment opts in rather than out, and this template does not
set it. That default is deliberate: the flag drives live DacFx deploys against a production database
with no environment guard of its own, so silently defaulting it on for every ARM/Bicep deployment
would be worse than requiring one explicit operator step. Leaving the flag unset means a freshly
deployed tenant database has no schema, and `SchemaDeployer` throws
`"Tenant {id}'s database is not initialized and ...AutomaticSchemaDeploymentEnabled is false"` during
initialization of that tenant (including application startup), directing you to the schema-upgrade CLI below.

The templates select a SQL UAMI through the connection string's client-ID `User ID` field. ARM uses
`Authentication=Active Directory Default`; Bicep uses `Active Directory Managed Identity`. Preserve
the deployed connection string rather than reconstructing its server, catalog, or authentication mode.
For the managed-identity deployment path below, run the CLI on compute carrying the selected UAMI;
`az login` on an arbitrary workstation does not attach that identity. The image contains the CLI alongside the server (see the Dockerfile), so the
lowest-friction option is a short-lived Azure Container Instance with that identity attached:

Before running it, provision the SQL database user and deployment permissions explicitly (see
[`scripts/setup-sql-mi.sql`](scripts/setup-sql-mi.sql)). `User ID` in the connection string is the
UAMI's **client ID**, not its resource name, principal/object ID, or a request to create a SQL user.
The runtime and CLI do not provision identities.

Use the **actual App Service settings** for the target configuration entry. The read commands below
capture values without displaying them; do not echo these variables or enable shell tracing/debug
output. Connection settings are sent to ACI as **secure** environment variables, not public values.

```powershell
$ErrorActionPreference = 'Stop'
$resourceGroup = 'fhir-dev-rg'
$appName = 'fhir-dev-yourorg'
$tenantIndex = 1 # ARM's tenant 1 entry; choose the actual entry index for a Bicep tenant.
$prefix = "Tenants__Configurations__${tenantIndex}__"

$settingsJson = az webapp config appsettings list --resource-group $resourceGroup --name $appName `
    --query "[?starts_with(name, '$prefix')]" --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not read the deployed tenant settings.' }
$settings = @{}
foreach ($setting in ($settingsJson | ConvertFrom-Json)) { $settings[$setting.name] = $setting.value }
$required = @('TenantId', 'DisplayName', 'FhirVersion', 'IsActive', 'Storage__Type', 'Storage__ConnectionString')
foreach ($key in $required) {
    if ([string]::IsNullOrWhiteSpace($settings["$prefix$key"])) { throw "Missing deployed setting: $prefix$key" }
}
$tenantId = [int]$settings["${prefix}TenantId"]
if ($tenantId -le 0 -or $settings["${prefix}IsActive"] -ne 'true') { throw 'Select an active, non-system tenant.' }
if ($settings["${prefix}Storage__Type"] -notin @('SqlServer', 'SqlEntityFramework')) { throw 'Select a SQL tenant.' }
$connectionString = $settings["${prefix}Storage__ConnectionString"]

# Find the attached identity selected by this connection string, not an assumed template output.
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection.set_ConnectionString($connectionString)
if (-not $connection.ContainsKey('User ID')) { throw 'This example requires a configured SQL UAMI client ID.' }
$identityJson = az webapp identity show --resource-group $resourceGroup --name $appName --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not read attached managed identities.' }
$identities = ($identityJson | ConvertFrom-Json).userAssignedIdentities
$matches = @($identities.PSObject.Properties | Where-Object { $_.Value.clientId -eq $connection['User ID'] })
if ($matches.Count -ne 1) { throw 'The SQL client ID must identify one UAMI attached to this App Service.' }
$uamiResourceId = $matches[0].Name
$uamiJson = az identity show --ids $uamiResourceId --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the selected UAMI resource.' }
$uami = $uamiJson | ConvertFrom-Json
if ($uami.clientId -ne $connection['User ID']) { throw 'The resolved UAMI does not match the SQL identity selector.' }
# Use $uami.name for the SQL CREATE USER placeholder in setup-sql-mi.sql, not its client ID.

$publicVariables = @(
    "${prefix}TenantId=$tenantId"
    "${prefix}DisplayName=$($settings["${prefix}DisplayName"])"
    "${prefix}FhirVersion=$($settings["${prefix}FhirVersion"])"
    "${prefix}IsActive=true"
    "${prefix}IsSystemPartition=false"
    "${prefix}Storage__Type=$($settings["${prefix}Storage__Type"])"
)
$secureVariables = @("${prefix}Storage__ConnectionString=$connectionString")
$containerName = 'ignixa-schema-upgrade'
az container create --resource-group $resourceGroup --name $containerName `
    --image ghcr.io/brendankowitz/ignixa-fhir:release --os-type Linux --cpu 1 --memory 2 `
    --assign-identity $uamiResourceId --restart-policy Never `
    --environment-variables @publicVariables --secure-environment-variables @secureVariables `
    --command-line "dotnet Ignixa.SchemaUpgrade.Cli.dll --tenant-id $tenantId --confirm" --output none
if ($LASTEXITCODE -ne 0) { throw 'Could not create the schema-upgrade container.' }

# Retain a failed or still-running container for diagnosis; do not terminate an in-progress upgrade.
for ($attempt = 0; $attempt -lt 180; $attempt++) {
    $state = az container show --resource-group $resourceGroup --name $containerName `
        --query 'containers[0].instanceView.currentState.state' --output tsv
    if ($LASTEXITCODE -ne 0) { throw 'Could not read schema-upgrade state.' }
    if ($state -eq 'Terminated') { break }
    Start-Sleep -Seconds 10
}
az container logs --resource-group $resourceGroup --name $containerName
if ($state -ne 'Terminated') { throw 'Schema CLI has not finished; inspect the retained container.' }
$exitCode = az container show --resource-group $resourceGroup --name $containerName `
    --query 'containers[0].instanceView.currentState.exitCode' --output tsv
if ($LASTEXITCODE -ne 0 -or $exitCode -ne '0') { throw "Schema CLI did not succeed (exit $exitCode)." }
az container delete --resource-group $resourceGroup --name $containerName --yes --output none
```

Repeat the lookup with the target `$tenantIndex` for each Bicep tenant; the catalog, FHIR version and
tenant ID come from deployed settings. ARM tenant 1 targets `FhirDatabase`, not `FhirTenant1`.
**Changing only
`--tenant-id` is insufficient:** the separate ACI does not inherit App Service settings and the image
only defines tenants 0/1. Supply every target tenant field above, or mount a complete tenant JSON
configuration into the container and pass `--config /config/appsettings.json`. Environment variables
and environment-specific JSON can override that file; verify the effective target connection before
deployment. Tenant 0 inherits tenant 1's database and does not need a second deployment. See
[`tools/Ignixa.SchemaUpgrade.Cli/README.md`](../../tools/Ignixa.SchemaUpgrade.Cli/README.md) for the
full option list, including `--allow-incompatible-platform` for non-Azure SQL targets and
`--allow-data-loss` for diffs `DeployReportClassifier` flags as unsafe.

**This step is not only for a first deployment.** Because this template leaves
`AutomaticSchemaDeploymentEnabled` unset, the app never applies a schema change on its own -- so any
release that raises `SchemaVersionConstants.CurrentVersion` needs the same CLI run again, once per
tenant, before that release serves traffic. Until then, initializing an out-of-date tenant
fails with `"Tenant {id}'s database is behind schema version {n} and
SqlServer:AutomaticSchemaDeploymentEnabled is false"`, naming the version the build expects.
`SELECT MAX(Version) FROM dbo.SchemaVersion` in a tenant's database reports where that tenant
currently stands; the changelog in `SchemaVersionConstants.cs` says what each version added.

**What Gets Configured (once the CLI has run):**
- ✅ **Tenant 0** (System Partition) - Shares database with Tenant 1, used for transaction IDs
- ✅ **Tenant 1-N** (Production Tenants) - Each configured with their own SQL database
- ✅ **Database Schema** - Deployed from the server's embedded dacpac (tables, indexes, stored procedures) per tenant database
- ✅ **DurableTask Backend** - Connected to Azure Storage with Managed Identity
- ✅ **Export/Import Storage** - Connected to Azure Blob Storage with Managed Identity

The schema-upgrade CLI does not create the database user for the SQL user-assigned Managed Identity --
run [`scripts/setup-sql-mi.sql`](scripts/setup-sql-mi.sql) against each tenant database (as an Azure
AD admin) beforehand, substituting the identity's name for the script's placeholder.

**Multi-Tenant Initialization (Bicep only):**

For a deployment with `tenantCount=10`, after running the CLI and `setup-sql-mi.sql` against each tenant:
1. All 10 tenant databases (`FhirTenant1` through `FhirTenant10`) have schema deployed
2. Managed identity users exist in each database
3. Tenant routing is configured for `/tenant/1/`, `/tenant/2/`, etc.
4. System partition (Tenant 0) shares `FhirTenant1` database

**The App Service image deployment itself is still hands-off** - it automatically pulls and runs your
Docker image from ACR/GHCR on restart. Only the database schema step above requires a manual run.

**Verify Setup** (Optional - Check logs or database after deployment):
```sql
-- Check that the MI user was created
SELECT * FROM sys.database_principals
WHERE type IN ('E', 'X');  -- E = External (Azure AD)

-- Check role membership
SELECT DP1.name as DatabaseUser, DP2.name as RoleName
FROM sys.database_role_members as DRM
RIGHT OUTER JOIN sys.database_principals as DP1 on DRM.member_principal_id = DP1.principal_id
LEFT OUTER JOIN sys.database_principals as DP2 on DRM.role_principal_id = DP2.principal_id
WHERE DP1.name = '<SQL UAMI display name>';
```

### 9. Configure Application Settings (Optional)

Both templates already configure the tenant's SQL connection. Do not replace that setting merely
to apply unrelated application options, and never redirect an ARM deployment from `FhirDatabase`
to an assumed `FhirTenant1` catalog.

```bash
# This updates only application options; it preserves the deployed database destination.
az webapp config appsettings set \
  --resource-group fhir-dev-rg \
  --name fhir-dev-yourorg \
  --settings \
    ASPNETCORE_ENVIRONMENT=production \
    Tenants__Mode=Isolated \
  --output none
```

For an intentional connection change, use the actual entry's
`Tenants__Configurations__{index}__Storage__ConnectionString` key and verify its current catalog
and selected identity using step 8 before changing anything. `User ID` selects an existing managed
identity's client ID, resolved with `az identity show --ids <verified-resource-id>`, not an App Service name.
Provision the corresponding SQL
user and grants before startup; authentication or permission failures are errors, not a
warning-and-continue setup path.

### 10. Test Deployment

```bash
# Get App Service URL from deployment outputs
APP_URL=$(az deployment group show \
  --resource-group ignixa-fhir-rg \
  --name <deployment-name> \
  --query properties.outputs.appServiceUrl.value \
  --output tsv)

# Test capability statement (auto-detects single tenant or uses tenant 1)
curl "$APP_URL/metadata"

# Multi-tenant: Test Tenant 1
curl "$APP_URL/tenant/1/metadata"

# Multi-tenant: Test Tenant 2
curl "$APP_URL/tenant/2/metadata"

# Create a test Patient resource in Tenant 1
curl -X PUT "$APP_URL/tenant/1/Patient/test-123" \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Patient",
    "id": "test-123",
    "name": [{"family": "Doe", "given": ["John"]}]
  }'

# Create a different Patient in Tenant 2 (isolated from Tenant 1)
curl -X PUT "$APP_URL/tenant/2/Patient/test-456" \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Patient",
    "id": "test-456",
    "name": [{"family": "Smith", "given": ["Jane"]}]
  }'

# Retrieve the Patient from Tenant 1
curl "$APP_URL/tenant/1/Patient/test-123"

# Search in Tenant 1 (only returns Tenant 1's patients)
curl "$APP_URL/tenant/1/Patient?name=Doe"

# Search in Tenant 2 (only returns Tenant 2's patients)
curl "$APP_URL/tenant/2/Patient?name=Smith"
```

**Expected Results:**
- `/metadata` and `/tenant/{id}/metadata` return CapabilityStatement (FHIR conformance)
- `/tenant/1/Patient/test-123` creates and returns the Patient resource
- `/tenant/2/Patient/test-456` creates a separate Patient in Tenant 2's database
- For Bicep, data is isolated per tenant (`FhirTenant1`, `FhirTenant2`, etc.); ARM's single tenant uses `FhirDatabase`
- All operations use Managed Identity (no credentials exposed)

## Deployment Outputs

The **ARM** template produces the following outputs:

| Output | Purpose |
|--------|---------|
| `appServiceUrl` | URL of the deployed FHIR Server |
| `appServiceName` | App Service resource name |
| `userAssignedIdentityPrincipalId` | User-Assigned Managed Identity principal ID (for SQL) |
| `userAssignedIdentityClientId` | User-Assigned Managed Identity client ID |
| `sqlServerFqdn` | SQL Server fully qualified domain name |
| `sqlServerName` | SQL Server name |
| `databaseName` | Database name (used by Tenant 1) |
| `storageAccountName` | Storage account name (export/import) |
| `durableTaskStorageAccountName` | Storage account name (DurableTask backend) |
| `appInsightsConnectionString` | Application Insights connection string |
| `dockerImageDeployed` | Full Docker image name deployed to App Service |

Bicep's top-level `main.bicep` exposes `tenantDatabases` instead of `databaseName`, and does not expose
the ARM-only UAMI client/principal outputs or an identity resource-ID output. For either template,
step 8 resolves the SQL UAMI from the App Service's attached identities and verifies it with
`az identity show`; it does not rely on those template-specific output names.

## Default Configuration

After deployment, the FHIR server is configured with:

### Tenant Configuration

- **Tenant 0** (System Partition) - SQL storage inherited from tenant 1, used for system operations
- **Tenant 1** (Production) - **Azure SQL Database** (auto-configured)
  - Storage Type: `SqlServer`
  - FHIR Version: Configurable via `fhirVersion` (ARM default: 4.3/R4B; Bicep default: 4.0/R4)
  - Catalog: ARM uses `FhirDatabase`; Bicep uses `FhirTenant1` for tenant 1
  - Connection: Uses Managed Identity authentication
  - Database: Schema is not auto-initialized -- run the schema-upgrade CLI once after first deploy (see [step 8](#8-database-schema-deployment))

### Storage Configuration

- **FHIR Data Storage** (exports/imports) - Azure Blob Storage
  - Account: `{appName}storage`
  - Containers: `fhir-exports`, `fhir-imports`, `fhirstorage`
  - Authentication: Managed Identity

- **DurableTask Backend** - Azure Storage
  - Account: `{appName}tasks`
  - Authentication: Managed Identity
  - Task Hub: `ignixa`

## Managed Identity Configuration

### How Managed Identity Works

The deployment creates **two Managed Identities** with different purposes:

1. **User-Assigned Managed Identity (UAMI)** - Dedicated identity for SQL authentication
   - Created as a standalone Azure resource
   - Assigned as SQL Server administrator with Entra ID-only authentication
   - Client ID embedded in connection strings for SQL authentication
   - ARM outputs: `userAssignedIdentityPrincipalId`, `userAssignedIdentityClientId`
   - For either template, resolve the attached identity resource and its `id`/`clientId`/`principalId`
     using the lookup in step 8 rather than assuming those outputs exist

2. **System-Assigned Managed Identity (SAMI)** - Automatic identity for Azure resources
   - Automatically created with the App Service
   - Used for Storage and other Azure resource authentication
   - Granted RBAC roles: "Storage Blob Data Contributor"
   - Authenticates using `DefaultAzureCredential` in application code

### Docker Container Registry Authentication

GHCR is a **public registry** - no authentication needed.

The image `ghcr.io/brendankowitz/ignixa-fhir` is publicly accessible without credentials. Leave `dockerRegistryUsername` and `dockerRegistryPassword` empty in your parameters.

**To use a private GHCR image** (if you fork the repo and make it private):

Generate a GitHub Personal Access Token (PAT) with `read:packages` scope and provide in parameters:

```json
"dockerRegistryUsername": { "value": "your-github-username" },
"dockerRegistryPassword": { "value": "your-github-pat" }
```

### Connection Strings

**SQL Database with a User-Assigned Managed Identity**:

The deployment selects the SQL UAMI by its client ID. The example below illustrates the
`Active Directory Managed Identity` form; ARM currently emits `Active Directory Default`.
Use the actual deployed setting from step 8 for schema deployment. An intentional configuration
change belongs under `Tenants__Configurations__1__Storage__ConnectionString` (or the actual entry index):

```
Server=tcp:fhir-dev-yourorg-sql.database.windows.net,1433;
Initial Catalog=<actual deployed tenant catalog>;
User ID=<clientId from az identity show>;
Encrypt=true;
TrustServerCertificate=false;
Connection Timeout=30;
Authentication=Active Directory Managed Identity;
```

**Without Explicit User ID** (uses the system-assigned managed identity):

If intentionally selecting the App Service's system-assigned managed identity instead, omit
`User ID`. This is a different principal from the template's SQL UAMI and needs its own SQL user
and grants:

```
Server=tcp:fhir-dev-yourorg-sql.database.windows.net,1433;
Initial Catalog=<actual deployed tenant catalog>;
Encrypt=true;
TrustServerCertificate=false;
Connection Timeout=30;
Authentication=Active Directory Managed Identity;
```

In both cases, explicitly provision the selected identity in each database using an Entra administrator.
Neither the application nor the schema CLI creates the user. Missing users or grants fail SQL operations.

**Azure Storage** (uses Managed Identity at runtime):
```csharp
// In code, use DefaultAzureCredential
var credential = new DefaultAzureCredential();
var blobClient = new BlobContainerClient(
    new Uri("https://fhirdevyourorg.blob.core.windows.net/fhir-exports"),
    credential);
```


## Security Considerations

### Authentication & Authorization

✅ **Enabled**:
- Azure AD-only authentication for SQL Database (no SQL passwords)
- RBAC authorization for all Azure resources
- TLS 1.2 minimum for all communications
- HTTPS only for App Service
- Soft delete and purge protection for Key Vault
- Transparent data encryption (TDE) for SQL Database

✅ **Disabled**:
- Local authentication on SQL Database (no `sa` account)
- Shared access keys on Storage Account
- Access policies on Key Vault (RBAC only)

### Network Security

**Default Configuration**:
- Network Security Group (NSG) in **listening/audit mode** (enabled by default)
- Virtual Network with App Service subnet and service endpoints
- NSG Flow Logs enabled (30-day retention) for monitoring traffic
- SQL Server firewall rules (allows Azure Services to bypass)
- Storage Account and Key Vault network ACLs
- Public network access enabled

**NSG Listening Mode (Audit)**:
- All traffic is **allowed** but **logged and monitored**
- Useful for understanding traffic patterns before implementing restrictions
- Flow logs available in Log Analytics Workspace for analysis
- No traffic is blocked - purely observational

**To disable NSG** (if not needed):
```bash
az deployment group create \
  --resource-group ignixa-fhir-rg \
  --template-file main.bicep \
  --parameters appName=ignixa-fhir-demo enableNetworkSecurity=false
```

**To transition from listening mode to enforcement**:
1. Review NSG Flow Logs in Log Analytics for 1-2 weeks
2. Identify necessary inbound/outbound rules
3. Update NSG security rules from `Allow` with audit to `Deny` with enforcement
4. Test thoroughly before moving to production

**To restrict further with Private Endpoints**:
1. Create Private Endpoints for SQL, Storage, Key Vault
2. Update NSG rules to restrict public access
3. Disable public network access on resources
4. Update network ACLs to restrict to VNet

## Monitoring and Logging

### Application Insights

Monitor application performance and diagnostics:

```bash
# View Application Insights in Azure Portal
# https://portal.azure.com → Resource Group → Application Insights

# Query logs (KQL - Kusto Query Language)
# Example: Check failed requests
requests
| where success == false
| project timestamp, name, resultCode, duration
```

### Log Analytics

Query centralized logs:

```bash
# View Log Analytics Workspace in Azure Portal
# https://portal.azure.com → Resource Groups → Log Analytics Workspaces
```

### NSG Flow Logs (Network Security Monitoring)

Monitor network traffic in listening mode:

```bash
# Get NSG Flow Logs from Log Analytics
# Query: AzureNetworkAnalytics_CL table

# Example KQL query: Top source IPs accessing the App Service
AzureNetworkAnalytics_CL
| where TimeGenerated > ago(1h)
| where Subnet_s == "app-service-subnet"
| summarize Count = count() by SrcIP_s
| top 10 by Count desc

# Example: Monitor outbound connections to SQL Server
AzureNetworkAnalytics_CL
| where TimeGenerated > ago(1h)
| where DestPort_d == 1433
| summarize Count = count() by DestIP_s, Action_s
| sort by Count desc

# Example: Check for denied traffic (should be none in listening mode)
AzureNetworkAnalytics_CL
| where TimeGenerated > ago(24h)
| where Action_s == "D"  // D = Deny
| summarize Count = count() by SrcIP_s, DestIP_s, DestPort_d
```

**Flow Log Data Retention**: 30 days (configurable in network-security.bicep)

### Alerts

Pre-configured alerts trigger when:
- CPU usage exceeds 80%
- Failed requests exceed 10 in 15 minutes

## Cost Management

### Estimated Monthly Costs (Development)

| Resource | SKU | Monthly Cost |
|----------|-----|--------------|
| App Service (Linux) | Basic B2 | ~$50 |
| SQL Database | Basic (5 DTU) | ~$5 |
| Storage Accounts (2) | Standard LRS | ~$2-10 |
| Application Insights | PAYG | ~$5-20 |
| Log Analytics | PAYG (1GB) | ~$5-10 |
| Docker Registry | GHCR (free) | **$0** |
| **Total** | | **~$65-95** |

### Cost Optimization

1. **Scale down** App Service for dev (B1 instead of B2)
2. **Use SQL Database DTU auto-scale** to avoid over-provisioning
3. **Archive old logs** in Log Analytics
4. **Delete unused snapshots** from SQL backups

## Troubleshooting

### Deployment Fails: "Template validation failed"

**Solution**: Verify parameter file syntax:
```bash
az bicep build-params parameters/dev.bicepparam
```

### SQL Connection Error: "Login failed for user"

**Solution**: Verify Managed Identity setup:
```sql
-- Check database user exists
SELECT * FROM sys.database_principals
WHERE name = 'fhir-dev-yourorg';

-- Verify role membership
SELECT DP1.name as User, DP2.name as Role
FROM sys.database_role_members as DRM
RIGHT OUTER JOIN sys.database_principals as DP1 on DRM.member_principal_id = DP1.principal_id
LEFT OUTER JOIN sys.database_principals as DP2 on DRM.role_principal_id = DP2.principal_id
WHERE DP1.name = 'fhir-dev-yourorg';
```

### App Service Can't Access Storage

**Solution**: Verify role assignment:
```bash
az role assignment list \
  --assignee-object-id <managed-identity-principal-id> \
  --resource-group fhir-dev-rg \
  --output table
```

### Docker Image Pull Failed

**Solution**: Verify GHCR access. For public images, no credentials are needed:

```bash
# Verify the image is publicly accessible
docker pull ghcr.io/brendankowitz/ignixa-fhir:latest
```

If using a **private GHCR image**, verify credentials are set:
```bash
# Check credentials in App Service
az webapp config appsettings list \
  --resource-group ignixa-fhir-rg \
  --name ignixa-fhir-demo \
  --query "[?name=='DOCKER_REGISTRY_SERVER_USERNAME' || name=='DOCKER_REGISTRY_SERVER_PASSWORD']"
```

## Cleanup

To remove all deployed resources:

```bash
# Delete entire resource group (CAUTION: This deletes everything)
az group delete --name fhir-dev-rg --yes

# Or delete specific resources:
az deployment group delete --name fhir-deployment --resource-group fhir-dev-rg
```

## References

- [Azure Bicep Documentation](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [Azure SQL Managed Identity](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview)
- [App Service Managed Identity](https://learn.microsoft.com/en-us/azure/app-service/overview-managed-identity)
- [Azure RBAC](https://learn.microsoft.com/en-us/azure/role-based-access-control/overview)

## Support

For issues or questions:

1. Check [Azure FHIR Server Documentation](https://docs.microsoft.com/en-us/azure/healthcare-apis/fhir/)
2. Review [Bicep Best Practices](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/best-practices)
3. Open an issue in the repository

---

**Last Updated**: October 2025
**Maintained By**: Ignixa Contributors
**License**: MIT
