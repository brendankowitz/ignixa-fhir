---
sidebar_position: 2
title: Azure Deployment
description: Deploy Ignixa to Azure
---

# Azure Deployment

Deploy Ignixa to Azure App Service with SQL Server and managed identity.

## One-Click Deploy

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fbrendankowitz%2Fignixa-fhir%2Fmain%2Fdeploy%2Fazure%2Fazuredeploy.json)

**Deploys:**
- Linux App Service (B1)
- Azure SQL Database (Basic)
- Storage Account
- Managed Identity (passwordless auth)

After deployment: `https://{appName}.azurewebsites.net/metadata`

## CLI Deployment

```bash
# Create resource group
az group create --name ignixa-rg --location eastus

# Deploy
az deployment group create \
  --resource-group ignixa-rg \
  --template-file deploy/azure/main.bicep \
  --parameters appName=ignixa-demo
```

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `appName` | required | Base name for resources |
| `location` | resource group | Azure region |
| `sku` | B1 | App Service SKU |
| `sqlSku` | Basic | SQL Database SKU |

## Architecture

```
┌────────────────────────────────────────────────────────┐
│                  Azure Resource Group                   │
├────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌────────────────┐  │
│  │ App Service │──│  SQL Server │  │ Storage Account│  │
│  └──────┬──────┘  └─────────────┘  └────────────────┘  │
│         │                                    ▲          │
│         └────────────────────────────────────┘          │
│                   Managed Identity                      │
└────────────────────────────────────────────────────────┘
```

## Key Resources

### App Service

```bicep
resource appSettings 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'appsettings'
  properties: {
    Tenants__Configurations__1__Storage__ConnectionString: '@Microsoft.KeyVault(...)'
    ASPNETCORE_ENVIRONMENT: 'Production'
  }
}
```

### SQL Server (Entra ID)

```bicep
resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: '${appName}-sql'
  properties: {
    administrators: {
      azureADOnlyAuthentication: true
      login: identity.name
      sid: identity.properties.clientId
    }
  }
}
```

### Health Check

```bicep
resource healthCheck 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'web'
  properties: {
    healthCheckPath: '/health/check'
  }
}
```

## Related

- [Docker Deployment](/docs/server/deployment/docker)
- [Configuration](/docs/server/configuration)
