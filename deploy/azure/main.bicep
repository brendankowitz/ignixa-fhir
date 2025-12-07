// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

targetScope = 'resourceGroup'

@description('Environment name (dev, staging, production)')
param environment string = 'production'

@description('Location for all resources')
param location string = resourceGroup().location

@description('FHIR server application name (must be globally unique for App Service)')
param appName string

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

@description('App Service Plan SKU (default: B2 for basic production)')
param appServicePlanSku string = 'B2'

@description('App Service Plan Tier')
param appServicePlanTier string = 'Basic'

@description('Number of tenant databases to create (1-50)')
@minValue(1)
@maxValue(50)
param tenantCount int = 1

@description('FHIR version for all tenants (e.g., 4.0, 5.0)')
param fhirVersion string = '4.0'

@description('Enable Network Security Perimeter for PaaS resources')
param enableNetworkSecurityPerimeter bool = true

// Deploy monitoring (Application Insights + Log Analytics) - needed early for app service
module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring-deployment'
  params: {
    appInsightsName: '${appName}-insights'
    location: location
  }
}

// Deploy User-Assigned Managed Identity for SQL Server authentication (critical for webapp to talk to DB)
module sqlAuthIdentity './modules/user-assigned-identity.bicep' = {
  name: 'sql-auth-identity-deployment'
  params: {
    identityName: '${appName}-sql-auth'
    location: location
    environment: environment
  }
}

// Deploy SQL Server (single server for all tenant databases)
module sqlServer './modules/sql-server.bicep' = {
  name: 'sql-server-deployment'
  params: {
    sqlServerName: '${appName}-sql'
    location: location
    sqlAdminPrincipalId: sqlAuthIdentity.outputs.principalId
    sqlAdminName: sqlAuthIdentity.outputs.identityName
    tenantId: subscription().tenantId
  }
}

// Deploy tenant databases (one per tenant)
module tenantDatabases './modules/tenant-databases.bicep' = {
  name: 'tenant-databases-deployment'
  params: {
    sqlServerName: sqlServer.outputs.sqlServerName
    location: location
    tenantCount: tenantCount
    environment: environment
  }
}

// Deploy Blob Storage (with Managed Identity access only)
module storage './modules/storage.bicep' = {
  name: 'storage-deployment'
  params: {
    storageAccountName: replace('${appName}storage', '-', '')
    location: location
    principalId: '' // Will be assigned after app service is created
    disableLocalAuth: true
  }
}

// Deploy App Service (Linux container running Ignixa)
// UAMI is critical for webapp to authenticate to SQL Server using AAD
module appService './modules/app-service.bicep' = {
  name: 'app-service-deployment'
  params: {
    appName: appName
    location: location
    environment: environment
    appServicePlanSku: appServicePlanSku
    appServicePlanTier: appServicePlanTier
    dockerRegistryUrl: dockerRegistryUrl
    dockerImage: dockerImage
    dockerImageTag: dockerImageTag
    dockerRegistryUsername: dockerRegistryUsername
    dockerRegistryPassword: dockerRegistryPassword
    appInsightsInstrumentationKey: monitoring.outputs.appInsightsInstrumentationKey
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    uamiResourceId: sqlAuthIdentity.outputs.identityResourceId
    uamiClientId: sqlAuthIdentity.outputs.clientId
    sqlServerFqdn: sqlServer.outputs.sqlServerFqdn
    storageAccountName: storage.outputs.storageAccountName
    tenantCount: tenantCount
    fhirVersion: fhirVersion
  }
}

// Assign Storage RBAC roles to App Service Managed Identity (runs after app service is created)
// DurableTask requires Blob, Queue, and Table Data Contributor roles
module storageRbac './modules/storage-rbac.bicep' = {
  name: 'storage-rbac-deployment'
  params: {
    storageAccountName: storage.outputs.storageAccountName
    principalId: appService.outputs.managedIdentityPrincipalId
  }
}

// Deploy Network Security Perimeter (associates Storage, SQL, and Log Analytics in learning/audit mode)
module networkSecurityPerimeter './modules/network-security-perimeter.bicep' = if (enableNetworkSecurityPerimeter) {
  name: 'network-security-perimeter-deployment'
  params: {
    nspName: '${appName}-nsp'
    location: location
    environment: environment
    storageAccountId: storage.outputs.storageAccountResourceId
    sqlServerId: sqlServer.outputs.sqlServerResourceId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// Output key information for next steps
output appServiceUrl string = appService.outputs.appServiceUrl
output appServiceName string = appService.outputs.appServiceName
output appServiceManagedIdentityPrincipalId string = appService.outputs.managedIdentityPrincipalId
output sqlServerFqdn string = sqlServer.outputs.sqlServerFqdn
output sqlServerName string = sqlServer.outputs.sqlServerName
output tenantDatabases array = tenantDatabases.outputs.databaseNames
output storageAccountName string = storage.outputs.storageAccountName
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
