---
sidebar_position: 2
title: Azure Deployment
description: Deploy Ignixa to Azure
---

# Azure Deployment

Deploy Ignixa to Azure using Infrastructure as Code with Bicep templates.

## One-Click Deployment

Deploy a single-tenant Ignixa instance:

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fbrendankowitz%2Fignixa-fhir%2Fmain%2Fdeploy%2Fazure%2Fazuredeploy.json)

## Architecture Overview

The Azure deployment provisions:

```
┌─────────────────────────────────────────────────────────────┐
│                    Azure Resource Group                      │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────┐  │
│  │ App Service │◀──▶│ SQL Server  │    │ Storage Account │  │
│  │   (Linux)   │    │  Database   │    │   (Blob/Table)  │  │
│  └──────┬──────┘    └─────────────┘    └─────────────────┘  │
│         │                                        ▲           │
│         │                                        │           │
│         └────────────────────────────────────────┘           │
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   Managed Identity                       ││
│  │             (Passwordless Authentication)                ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

- Azure CLI installed
- Azure subscription
- Contributor access to resource group

## CLI Deployment

### Single Tenant

```bash
# Create resource group
az group create --name ignixa-rg --location eastus

# Deploy
az deployment group create \
  --resource-group ignixa-rg \
  --template-file deploy/azure/azuredeploy.json \
  --parameters appName=ignixa-demo
```

### Multi-Tenant

```bash
az deployment group create \
  --resource-group ignixa-prod \
  --template-file deploy/azure/main.bicep \
  --parameters appName=ignixa-prod tenantCount=10
```

## Bicep Template Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `appName` | string | required | Base name for resources |
| `location` | string | resource group | Azure region |
| `tenantCount` | int | 1 | Number of tenants |
| `sku` | string | B1 | App Service SKU |
| `sqlSku` | string | Basic | SQL Database SKU |

## Managed Identity

The deployment uses Managed Identity for secure, passwordless authentication:

```bicep
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-identity'
  location: location
}
```

### SQL Access

The managed identity is granted `db_owner` on the database:

```sql
CREATE USER [ignixa-identity] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [ignixa-identity];
```

### Storage Access

```bicep
resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-...')
    principalId: identity.properties.principalId
  }
}
```

## App Service Configuration

### Application Settings

```bicep
resource appSettings 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'appsettings'
  properties: {
    Storage__Provider: 'SqlServer'
    Storage__ConnectionString: '@Microsoft.KeyVault(SecretUri=${sqlConnectionString})'
    Tenancy__Mode: tenantCount > 1 ? 'MultiTenant' : 'SingleTenant'
    ASPNETCORE_ENVIRONMENT: 'Production'
  }
}
```

### Health Checks

```bicep
resource healthCheck 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'web'
  properties: {
    healthCheckPath: '/health/ready'
  }
}
```

## SQL Server Setup

### Server Configuration

```bicep
resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: '${appName}-sql'
  location: location
  properties: {
    administrators: {
      azureADOnlyAuthentication: true
      principalType: 'Application'
      login: identity.name
      sid: identity.properties.clientId
    }
  }
}
```

### Database Configuration

```bicep
resource database 'Microsoft.Sql/servers/databases@2022-05-01-preview' = {
  parent: sqlServer
  name: 'IgnixaFhir'
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648  // 2GB
  }
  sku: {
    name: sqlSku
    tier: 'Basic'
  }
}
```

## Storage Account

For bulk operations and blob storage:

```bicep
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: '${appName}storage'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}
```

## Networking

### Virtual Network Integration

```bicep
resource vnet 'Microsoft.Network/virtualNetworks@2023-05-01' = {
  name: '${appName}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: ['10.0.0.0/16']
    }
    subnets: [
      {
        name: 'app-subnet'
        properties: {
          addressPrefix: '10.0.1.0/24'
          delegations: [{
            name: 'appService'
            properties: {
              serviceName: 'Microsoft.Web/serverFarms'
            }
          }]
        }
      }
    ]
  }
}
```

### Private Endpoints

```bicep
resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-05-01' = {
  name: '${appName}-sql-pe'
  location: location
  properties: {
    subnet: {
      id: vnet.properties.subnets[1].id
    }
    privateLinkServiceConnections: [{
      name: 'sql-connection'
      properties: {
        privateLinkServiceId: sqlServer.id
        groupIds: ['sqlServer']
      }
    }]
  }
}
```

## Monitoring

### Application Insights

```bicep
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}
```

### Log Analytics

```bicep
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}
```

## Scaling

### Auto-Scale Rules

```bicep
resource autoScale 'Microsoft.Insights/autoscalesettings@2022-10-01' = {
  name: '${appName}-autoscale'
  location: location
  properties: {
    targetResourceUri: appServicePlan.id
    profiles: [{
      name: 'default'
      capacity: {
        minimum: '1'
        maximum: '10'
        default: '1'
      }
      rules: [{
        metricTrigger: {
          metricName: 'CpuPercentage'
          operator: 'GreaterThan'
          threshold: 70
        }
        scaleAction: {
          direction: 'Increase'
          type: 'ChangeCount'
          value: '1'
        }
      }]
    }]
  }
}
```

## Cost Optimization

| SKU | Monthly Estimate | Use Case |
|-----|-----------------|----------|
| B1 + Basic SQL | ~$25 | Development |
| P1v3 + S1 SQL | ~$200 | Production |
| P2v3 + S3 SQL | ~$500 | High volume |

## Related Documentation

- [Docker Deployment](/docs/server/deployment/docker)
- [Configuration](/docs/getting-started/configuration)
