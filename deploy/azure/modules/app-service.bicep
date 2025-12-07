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

@description('Docker registry URL (login server, e.g., https://ghcr.io)')
param dockerRegistryUrl string = 'https://ghcr.io'

@description('Docker image name (e.g., brendankowitz/ignixa-fhir)')
param dockerImage string = 'brendankowitz/ignixa-fhir'

@description('Docker image tag (e.g., release, 1.0.0)')
param dockerImageTag string = 'release'

@description('Docker registry username (leave empty for public registries)')
param dockerRegistryUsername string = ''

@description('Docker registry password (leave empty for public registries)')
@secure()
param dockerRegistryPassword string = ''

@description('Application Insights Instrumentation Key for monitoring')
param appInsightsInstrumentationKey string = ''

@description('Application Insights Connection String')
param appInsightsConnectionString string = ''

@description('User-Assigned Managed Identity resource ID (for SQL authentication)')
param uamiResourceId string = ''

@description('User-Assigned Managed Identity client ID (for SQL AAD auth)')
param uamiClientId string = ''

@description('SQL Server FQDN (for tenant connection strings)')
param sqlServerFqdn string = ''

@description('Storage account name (for DurableTask background jobs)')
param storageAccountName string = ''

@description('Number of tenant databases to configure (1-50)')
param tenantCount int = 1

@description('FHIR version for all tenants (e.g., 4.0, 5.0)')
param fhirVersion string = '4.0'

// Construct full Docker image reference (with registry host for linuxFxVersion)
var registryHost = replace(dockerRegistryUrl, 'https://', '')
var dockerImageFull = '${registryHost}/${dockerImage}:${dockerImageTag}'
var useDockerAuth = !empty(dockerRegistryUsername) && !empty(dockerRegistryPassword)

// Generate dynamic tenant configurations (returns array of arrays, will be flattened when concatenated)
var tenantConfigurations = [for i in range(1, tenantCount): [
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
    value: 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=FhirTenant${i};User ID=${uamiClientId};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;Authentication=Active Directory Managed Identity;'
  }
]]

// Create App Service Plan (Linux container)
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${appName}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: appServicePlanSku
    tier: appServicePlanTier
  }
  properties: {
    reserved: true // Linux
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
  }
}

// Create App Service (Linux Container)
resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  kind: 'app,linux,container'
  identity: {
    type: !empty(uamiResourceId) ? 'SystemAssigned, UserAssigned' : 'SystemAssigned'
    userAssignedIdentities: !empty(uamiResourceId) ? {
      '${uamiResourceId}': {}
    } : null
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOCKER|${dockerImageFull}'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      healthCheckPath: '/health/check'
      appSettings: concat([
        {
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'false'
        }
        {
          name: 'WEBSITES_PORT'
          value: '80'
        }
        {
          name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
          value: '600' // 10 minutes for heavy initialization (search parameters, schema)
        }
        {
          name: 'DOCKER_REGISTRY_SERVER_URL'
          value: dockerRegistryUrl
        }
        {
          name: 'DOCKER_REGISTRY_SERVER_USERNAME'
          value: useDockerAuth ? dockerRegistryUsername : ''
        }
        {
          name: 'DOCKER_REGISTRY_SERVER_PASSWORD'
          value: useDockerAuth ? dockerRegistryPassword : ''
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment
        }
        {
          name: 'DOTNET_ENVIRONMENT'
          value: environment
        }
        {
          name: 'ASPNETCORE_URLS'
          value: 'http://+:80'
        }
        {
          name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
          value: 'true'
        }
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
        // DurableTask configuration for background job processing (import/export)
        {
          name: 'DurableTask__Provider'
          value: 'AzureStorage'
        }
        {
          name: 'DurableTask__AzureStorage__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'DurableTask__AzureStorage__StorageAccountName'
          value: storageAccountName
        }
        {
          name: 'DurableTask__AzureStorage__TaskHubName'
          value: 'ignixa'
        }
        // BlobStorage configuration for FHIR import/export data files
        {
          name: 'BlobStorage__Provider'
          value: 'Azure'
        }
        {
          name: 'BlobStorage__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'BlobStorage__StorageAccountUri'
          value: 'https://${storageAccountName}.blob.${az.environment().suffixes.storage}'
        }
        {
          name: 'BlobStorage__ContainerName'
          value: 'fhir-exports'
        }
        // System Partition (Tenant 0) - always required
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
          value: 'true'
        }
      ], flatten(tenantConfigurations))
    }
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
  }
}

// Output the managed identity principal ID (needed by other modules for RBAC)
output managedIdentityPrincipalId string = appService.identity.principalId

// Output the app service URL
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'

// Output the app service name
output appServiceName string = appService.name

// Output the app service resource ID
output appServiceResourceId string = appService.id
