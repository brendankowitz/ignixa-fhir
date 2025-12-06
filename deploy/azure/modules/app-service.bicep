// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('App name - must be globally unique')
param appName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Environment name (dev, staging, production)')
param environment string = 'production'

@description('App Service Plan SKU (default: B2 for basic production)')
param appServicePlanSku string = 'B2'

@description('App Service Plan Tier')
param appServicePlanTier string = 'Basic'

@description('Application Insights Instrumentation Key for monitoring')
param appInsightsInstrumentationKey string = ''

@description('Application Insights Connection String')
param appInsightsConnectionString string = ''

@description('Number of tenants to configure (1-50)')
@minValue(1)
@maxValue(50)
param tenantCount int = 1

@description('FHIR version for all tenants')
param fhirVersion string = '4.0'

@description('SQL Server FQDN for tenant database connections')
param sqlServerFqdn string

// Create App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${appName}-plan'
  location: location
  sku: {
    name: appServicePlanSku
    tier: appServicePlanTier
  }
  properties: {
    reserved: false // Windows
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
  }
}

// Create App Service (Web App) with System-Assigned Managed Identity
resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  kind: 'app'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false

    siteConfig: {
      netFrameworkVersion: 'v9.0'
      http20Enabled: true
      minTlsVersion: '1.2'
      defaultDocuments: []

      // ASP.NET Core configuration
      appSettings: concat([
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment
        }
        {
          name: 'ASPNETCORE_URLS'
          value: 'http://+:80'
        }
        {
          name: 'DOTNET_ENVIRONMENT'
          value: environment
        }
        // Application Insights monitoring
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsightsInstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'XDT_MicrosoftApplicationInsights_Mode'
          value: 'default'
        }
        // Tenant configuration
        {
          name: 'Tenants__Mode'
          value: 'Isolated'
        }
        // System partition (Tenant 0) - reserved for transaction IDs
        {
          name: 'Tenants__Configurations__0__TenantId'
          value: '0'
        }
        {
          name: 'Tenants__Configurations__0__DisplayName'
          value: 'System Partition (Reserved)'
        }
        {
          name: 'Tenants__Configurations__0__FhirVersion'
          value: fhirVersion
        }
        {
          name: 'Tenants__Configurations__0__IsActive'
          value: 'true'
        }
        {
          name: 'Tenants__Configurations__0__IsSystemPartition'
          value: 'true'
        }
        {
          name: 'Tenants__Configurations__0__Storage__Type'
          value: 'SqlEntityFramework'
        }
        {
          name: 'Tenants__Configurations__0__Storage__InheritConnectionStringFromTenant'
          value: '1'
        }
      ],
      // Generate tenant configurations dynamically (Tenant 1 through tenantCount)
      flatten([for i in range(1, tenantCount): [
        {
          name: 'Tenants__Configurations__${i}__TenantId'
          value: string(i)
        }
        {
          name: 'Tenants__Configurations__${i}__DisplayName'
          value: 'Tenant ${i}'
        }
        {
          name: 'Tenants__Configurations__${i}__FhirVersion'
          value: fhirVersion
        }
        {
          name: 'Tenants__Configurations__${i}__IsActive'
          value: 'true'
        }
        {
          name: 'Tenants__Configurations__${i}__IsSystemPartition'
          value: 'false'
        }
        {
          name: 'Tenants__Configurations__${i}__Storage__Type'
          value: 'SqlEntityFramework'
        }
        {
          name: 'Tenants__Configurations__${i}__Storage__ConnectionString'
          value: 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=FhirTenant${i};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;Authentication=Active Directory Managed Identity;'
        }
      ]]))
    }
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
  }
}

// Configure HTTPS only (redundant but explicit security setting)
resource appServiceHttpsConfig 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: appService
  name: 'web'
  properties: {
    httpsOnly: true
    minTlsVersion: '1.2'
  }
}

// Output the managed identity principal ID (needed by other modules for RBAC)
output managedIdentityPrincipalId string = appService.identity.principalId

// Output the app service URL
output appServiceUrl string = 'https://${appService.defaultHostName}'

// Output the app service name
output appServiceName string = appService.name

// Output the app service resource ID
output appServiceResourceId string = appService.id
