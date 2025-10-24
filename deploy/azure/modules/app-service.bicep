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
      appSettings: [
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
      ]
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
