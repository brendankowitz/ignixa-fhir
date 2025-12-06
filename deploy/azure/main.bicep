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

@description('Azure SQL database admin email (AAD user or group)')
param sqlAdminEmail string = ''

@description('Number of tenants to provision (1-50). Each tenant gets a separate database.')
@minValue(1)
@maxValue(50)
param tenantCount int = 1

@description('FHIR version for all tenants')
param fhirVersion string = '4.0'

// Deploy App Service (with System-Assigned Managed Identity)
module appService './modules/app-service.bicep' = {
  name: 'app-service-deployment'
  params: {
    appName: appName
    location: location
    environment: environment
    tenantCount: tenantCount
    fhirVersion: fhirVersion
    sqlServerFqdn: sqlServer.outputs.sqlServerFqdn
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
  }
  dependsOn: [
    sqlServer
    tenantDatabases
    monitoring
  ]
}

// Deploy SQL Server (single server for all tenant databases)
module sqlServer './modules/sql-server.bicep' = {
  name: 'sql-server-deployment'
  params: {
    sqlServerName: '${appName}-sql'
    location: location
    disableLocalAuth: true
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
  dependsOn: [
    sqlServer
  ]
}

// Deploy Blob Storage (with Managed Identity access only)
module storage './modules/storage.bicep' = {
  name: 'storage-deployment'
  params: {
    storageAccountName: replace('${appName}storage', '-', '')
    location: location
    principalId: appService.outputs.managedIdentityPrincipalId
    disableLocalAuth: true
  }
}

// Deploy Key Vault (with RBAC-only authorization)
module keyVault './modules/key-vault.bicep' = {
  name: 'keyvault-deployment'
  params: {
    keyVaultName: '${appName}-kv'
    location: location
    tenantId: subscription().tenantId
    appServicePrincipalId: appService.outputs.managedIdentityPrincipalId
  }
}

// Deploy monitoring (Application Insights + Log Analytics)
module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring-deployment'
  params: {
    appInsightsName: '${appName}-insights'
    location: location
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
output keyVaultUri string = keyVault.outputs.keyVaultUri
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output tenantCount int = tenantCount
