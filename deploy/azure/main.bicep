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

// Deploy App Service (with System-Assigned Managed Identity)
module appService './modules/app-service.bicep' = {
  name: 'app-service-deployment'
  params: {
    appName: appName
    location: location
    environment: environment
  }
}

// Deploy SQL Database (with AAD-only authentication)
module sql './modules/sql-database.bicep' = {
  name: 'sql-deployment'
  params: {
    sqlServerName: '${appName}-sql'
    location: location
    databaseName: 'FhirDatabase'
    disableLocalAuth: true
  }
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
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlServerName string = sql.outputs.sqlServerName
output databaseName string = sql.outputs.databaseName
output storageAccountName string = storage.outputs.storageAccountName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
