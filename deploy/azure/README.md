# FHIR Server Azure Deployment Guide

This directory contains Azure Infrastructure as Code (IaC) templates for deploying the Ignixa FHIR Server to Microsoft Azure using Bicep.

## Overview

The deployment uses **Bicep** templates with **Managed Identity** for secure, passwordless authentication across all Azure services:

- **App Service**: Runs the FHIR Server application with System-Assigned Managed Identity
- **Azure SQL Database**: FHIR data storage with Azure AD-only authentication (no SQL passwords)
- **Blob Storage**: Document and export storage with Managed Identity access
- **Key Vault**: Secrets management using RBAC (no access policies)
- **Application Insights**: Application monitoring and logging
- **Log Analytics**: Centralized logging workspace

## Directory Structure

```
deploy/azure/
├── main.bicep                          # Main orchestration template
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

## Quick Start

### 1. Login to Azure

```powershell
# Login to Azure
az login

# Optionally specify subscription
az account set --subscription "My Subscription Name"
```

### 2. Update Parameter File

Edit the appropriate parameter file with your values:

**Development**:
```bash
vi parameters/dev.bicepparam
```

Update:
```bicep
param appName = 'fhir-dev-yourorg'  # Must be globally unique
param sqlAdminEmail = ''             # Optional: your Azure AD email
```

**Production**:
```bash
vi parameters/production.bicepparam
```

Update:
```bicep
param appName = 'fhir-prod-yourorg' # Must be globally unique
param sqlAdminEmail = 'admin@yourorg.com'  # Recommended: service principal or admin
```

### 3. Run Deployment Script

**PowerShell** (Windows):
```powershell
cd scripts
.\deploy.ps1 -Environment dev `
    -ResourceGroup fhir-dev-rg `
    -Location eastus
```

**Bash** (Linux/macOS):
```bash
cd scripts
bash deploy.sh --environment dev \
    --resource-group fhir-dev-rg \
    --location eastus
```

**Directly with Azure CLI**:
```bash
az deployment group create \
  --name fhir-deployment \
  --resource-group fhir-dev-rg \
  --template-file main.bicep \
  --parameters parameters/dev.bicepparam
```

### 4. Deploy Application (Auto-Initializes Everything)

The FHIR Server **automatically initializes the entire database** on first run. No manual SQL scripts needed!

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

**Build and Deploy Application**:

```bash
# Build Docker image (if using containers)
docker build -t fhir-server:latest .

# Or publish .NET application
dotnet publish -c Release -o publish

# Deploy to App Service
az webapp deployment source config-zip \
  --resource-group fhir-dev-rg \
  --name fhir-dev-yourorg \
  --src publish.zip
```

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

### 5. Configure Application Settings

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

### 6. Test Deployment

```bash
# Get App Service URL from deployment outputs
APP_URL="https://fhir-dev-yourorg.azurewebsites.net"

# Test capability statement
curl "$APP_URL/metadata"

# Test health check (if implemented)
curl "$APP_URL/health"
```

## Deployment Outputs

The deployment produces the following outputs:

| Output | Purpose |
|--------|---------|
| `appServiceUrl` | URL of the deployed FHIR Server |
| `appServiceName` | App Service resource name |
| `appServiceManagedIdentityPrincipalId` | Managed Identity principal ID for RBAC |
| `sqlServerFqdn` | SQL Server fully qualified domain name |
| `sqlServerName` | SQL Server name |
| `databaseName` | Database name |
| `storageAccountName` | Storage account name |
| `keyVaultUri` | Key Vault URI for secret references |
| `appInsightsConnectionString` | Application Insights connection string |

## Managed Identity Configuration

### How Managed Identity Works

1. **App Service** has System-Assigned Managed Identity automatically created
2. **Azure AD** grants credentials to the App Service
3. **RBAC Role Assignments** grant permissions to specific resources:
   - Storage: "Storage Blob Data Contributor" role
   - Key Vault: "Key Vault Secrets User" role
   - SQL Server: "SQL Server Contributor" role (server-level)

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

**Key Vault** (uses Managed Identity at runtime):
```csharp
var credential = new DefaultAzureCredential();
var client = new SecretClient(
    new Uri("https://fhir-dev-yourorg-kv.vault.azure.net/"),
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
| App Service | Basic B2 | ~$50 |
| SQL Database | Basic (5 DTU) | ~$5 |
| Storage Account | Standard LRS | ~$1-5 |
| Key Vault | Standard | ~$0.60 |
| Application Insights | PAYG | ~$5-20 |
| Log Analytics | PAYG (1GB) | ~$5-10 |
| **Total** | | **~$70-90** |

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

### Key Vault "Access Denied" Error

**Solution**: Verify Key Vault RBAC assignment:
```bash
az role assignment list \
  --scope /subscriptions/<subscription-id>/resourceGroups/fhir-dev-rg/providers/Microsoft.KeyVault/vaults/fhir-dev-yourorg-kv \
  --output table
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
