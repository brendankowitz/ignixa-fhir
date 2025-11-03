# FHIR Server Azure Deployment Guide

This directory contains Azure Infrastructure as Code (IaC) templates for deploying the Ignixa FHIR Server to Microsoft Azure using Bicep.

## Overview

The deployment uses **Bicep templates** or **ARM JSON templates** with **Managed Identity** for secure, passwordless authentication across all Azure services:

- **App Service (Linux)**: Runs the FHIR Server Docker container with System-Assigned Managed Identity
- **Azure SQL Database**: FHIR data storage with Azure AD-only authentication (no SQL passwords)
- **Blob Storage (2 accounts)**: FHIR data storage + DurableTask orchestration backend
- **Application Insights**: Application monitoring and logging
- **Log Analytics**: Centralized logging workspace
- **Docker/ACR Support**: Configured to pull Docker images from Azure Container Registry or any Docker registry

## Deployment Options

### Option 1: ARM Template (Single JSON File) ⚡ Quickest

Use the consolidated `azuredeploy.json` template for one-click deployment:

```bash
az deployment group create \
  --resource-group fhir-dev-rg \
  --template-file azuredeploy.json \
  --parameters azuredeploy.parameters.json
```

**Best for**: Quick deployments, CI/CD pipelines, users familiar with ARM templates

### Option 2: Bicep Modules (Modular IaC) 🔧 Most Flexible

Use the modular Bicep templates for advanced customization:

```bash
az deployment group create \
  --resource-group fhir-dev-rg \
  --template-file main.bicep \
  --parameters parameters/dev.bicepparam
```

**Best for**: Advanced scenarios, incremental deployments, easier to maintain and customize

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
      "value": "https://ignixa.azurecr.io"  // Your ACR URL
    },
    "dockerImage": {
      "value": "ignixa-fhir"  // Docker image name
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

**Using ARM Template** (recommended for quick start):

```bash
az deployment group create \
  --resource-group ignixa-fhir-rg \
  --template-file azuredeploy.json \
  --parameters azuredeploy.parameters.json
```

**OR using Bicep Modules** (for advanced scenarios):

```bash
az deployment group create \
  --resource-group ignixa-fhir-rg \
  --template-file main.bicep \
  --parameters appName=ignixa-fhir-demo
```

**OR using PowerShell script**:
```powershell
cd scripts
.\deploy.ps1 -Environment production `
    -ResourceGroup ignixa-fhir-rg `
    -Location eastus
```

Deployment takes approximately **5-10 minutes**.

### 5. Configure ACR Authentication

Choose one of the authentication methods:

**Option 1: Public ACR with Anonymous Pull (Recommended)**

Enable anonymous pull on your ACR - no credentials needed:

```bash
az acr update --name ignixa --anonymous-pull-enabled true
```

Leave `dockerRegistryUsername` and `dockerRegistryPassword` empty in parameters.

**Option 2: Username/Password for Private Registries**

Enable admin credentials and provide them in the parameters file:

```bash
# Enable admin account on ACR
az acr update --name ignixa --admin-enabled true

# Get credentials
az acr credential show --name ignixa
```

Then update `azuredeploy.parameters.json`:
```json
"dockerRegistryUsername": {
  "value": "ignixa"
},
"dockerRegistryPassword": {
  "value": "your-acr-admin-password"
}
```

### 6. Build and Push Docker Image

Build your Docker image and push to ACR:

```bash
# Build the Docker image
docker build -t ignixa.azurecr.io/ignixa-fhir:latest .

# Login to ACR
az acr login --name ignixa

# Push the image
docker push ignixa.azurecr.io/ignixa-fhir:latest
```

### 7. Restart App Service (to pull new image)

After pushing a new Docker image, restart the App Service to pull and run it:

```bash
az webapp restart \
  --resource-group ignixa-fhir-rg \
  --name ignixa-fhir-demo
```

### 8. Application Auto-Initialization

The FHIR Server **automatically initializes the entire database** on first run. No manual SQL scripts needed!

**What Gets Configured:**
- ✅ **Tenant 1** (Default Production Tenant) - Configured to use the SQL database
- ✅ **Database Schema** - Auto-created with all tables, indexes, and stored procedures
- ✅ **Managed Identity** - Database user auto-created with permissions
- ✅ **DurableTask Backend** - Connected to Azure Storage with Managed Identity
- ✅ **Export/Import Storage** - Connected to Azure Blob Storage with Managed Identity

**Automatic Full Initialization** (On First Run):

1. **Schema Creation** (if database is empty)
   - ✅ Detects empty database automatically
   - ✅ Loads embedded schema (97.sql) from application assembly
   - ✅ Creates all tables, views, indexes, functions, stored procedures
   - ✅ Configures partition functions for performance
   - ✅ Creates all 17 Table-Valued Parameter types

2. **Managed Identity Setup** (if User ID in connection string)
   - ✅ Extracts User ID from connection string
   - ✅ Creates database user for App Service MI
   - ✅ Assigns `db_datareader` role (read permissions)
   - ✅ Assigns `db_datawriter` role (write permissions)
   - ✅ Grants EXECUTE on schema (for stored procedures/functions)
   - ✅ Grants CREATE TABLE (for schema evolution)

**No manual deployment needed** - The App Service automatically pulls and runs your Docker image from ACR!

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
WHERE DP1.name = 'fhir-dev-yourorg';
```

### 9. Configure Application Settings (Optional)

Add application configuration to the deployed App Service. The connection string includes the App Service name (`User ID` parameter) for automatic MI setup:

```bash
# Set environment variables with MI User ID for auto-setup
az webapp config appsettings set \
  --resource-group fhir-dev-rg \
  --name fhir-dev-yourorg \
  --settings \
    ASPNETCORE_ENVIRONMENT=production \
    Tenants:Mode=Isolated \
    "ConnectionStrings:FhirDatabase=Server=tcp:fhir-dev-yourorg-sql.database.windows.net,1433;Initial Catalog=FhirDatabase;User ID=fhir-dev-yourorg;Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;Authentication=Active Directory Managed Identity;"
```

The `User ID=fhir-dev-yourorg` parameter tells the application to automatically create and configure the Managed Identity database user on first run.

### 10. Test Deployment

```bash
# Get App Service URL from deployment outputs
APP_URL=$(az deployment group show \
  --resource-group ignixa-fhir-rg \
  --name <deployment-name> \
  --query properties.outputs.appServiceUrl.value \
  --output tsv)

# Test capability statement
curl "$APP_URL/metadata"

# Create a test Patient resource (Tenant 1)
curl -X PUT "$APP_URL/Patient/test-123" \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Patient",
    "id": "test-123",
    "name": [{"family": "Doe", "given": ["John"]}]
  }'

# Retrieve the Patient
curl "$APP_URL/Patient/test-123"

# Search for Patients
curl "$APP_URL/Patient?name=Doe"
```

**Expected Results:**
- `/metadata` returns CapabilityStatement (FHIR conformance)
- `/Patient/test-123` creates and returns the Patient resource
- Data is stored in Azure SQL Database (Tenant 1)
- All operations use Managed Identity (no credentials exposed)

## Deployment Outputs

The deployment produces the following outputs:

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

## Default Configuration

After deployment, the FHIR server is configured with:

### Tenant Configuration

- **Tenant 0** (System Partition) - FileSystem storage, used for transaction ID allocation
- **Tenant 1** (Production) - **Azure SQL Database** (auto-configured)
  - Storage Type: `SqlEntityFramework`
  - FHIR Version: Configurable via `fhirVersion` parameter (default: 4.3/R4B)
  - Connection: Uses Managed Identity authentication
  - Database: Auto-initialized on first startup

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
   - Outputs: `userAssignedIdentityPrincipalId`, `userAssignedIdentityClientId`

2. **System-Assigned Managed Identity (SAMI)** - Automatic identity for Azure resources
   - Automatically created with the App Service
   - Used for Storage and other Azure resource authentication
   - Granted RBAC roles: "Storage Blob Data Contributor"
   - Authenticates using `DefaultAzureCredential` in application code

### Docker Container Registry Authentication

**Option 1: Public ACR with Anonymous Pull (Recommended)**

Enable anonymous pull on your ACR:

```bash
az acr update --name ignixa --anonymous-pull-enabled true
```

No authentication parameters needed - leave username/password empty.

**Option 2: Username/Password for Private Registries**

Enable admin credentials and provide username/password in parameters:

```bash
# Enable admin account on ACR
az acr update --name ignixa --admin-enabled true

# Get credentials
az acr credential show --name ignixa
```

Then update parameters:
```json
"dockerRegistryUsername": { "value": "ignixa" },
"dockerRegistryPassword": { "value": "password-from-above" }
```

### Connection Strings

**SQL Database with Auto-MI Setup** (uses Managed Identity at runtime):

The application **automatically detects and configures the MI user** if you embed the App Service name in the User ID parameter:

```
Server=tcp:fhir-dev-yourorg-sql.database.windows.net,1433;
Initial Catalog=FhirDatabase;
User ID=fhir-dev-yourorg;
Encrypt=true;
TrustServerCertificate=false;
Connection Timeout=30;
Authentication=Active Directory Managed Identity;
```

**Without Explicit User ID** (uses running process identity):

Alternatively, if you omit the User ID parameter, the application uses the running process identity (App Service Managed Identity):

```
Server=tcp:fhir-dev-yourorg-sql.database.windows.net,1433;
Initial Catalog=FhirDatabase;
Encrypt=true;
TrustServerCertificate=false;
Connection Timeout=30;
Authentication=Active Directory Managed Identity;
```

In this case, you must manually set up the MI user on the database (or the app will log a warning and continue).

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

Current deployment uses:
- Public network access enabled (can be restricted with firewall rules)
- Bypass Azure Services for SQL and Storage
- Adjust network ACLs as needed for production

**To restrict further**:
1. Deploy Virtual Network (VNet)
2. Create Private Endpoints for SQL, Storage, Key Vault
3. Update network ACLs to restrict to VNet

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
| ACR (optional) | Basic | ~$5 |
| **Total** | | **~$70-100** |

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

**Solution**: Verify ACR access or credentials:

For Public ACR:
```bash
# Verify anonymous pull is enabled
az acr show --name ignixa --query anonymousPullEnabled
```

For Private ACR:
```bash
# Verify credentials are set correctly
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
