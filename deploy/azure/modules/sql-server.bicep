// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('SQL Server name (must be globally unique)')
param sqlServerName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Disable local SQL authentication (Managed Identity only)')
param disableLocalAuth bool = true

@description('Azure AD admin object ID for SQL Server')
param sqlAdminObjectId string = ''

@description('Azure AD admin display name')
param sqlAdminDisplayName string = ''

@description('Azure AD admin type (User or Group)')
param sqlAdminType string = 'User'

@description('Tenant ID for Azure AD integration')
param tenantId string = subscription().tenantId

@description('Environment name')
param environment string = 'production'

// Create Azure SQL Server with System Managed Identity
resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    administratorLogin: 'sqladmin' // Required even though disabled
    administratorLoginPassword: uniqueString(resourceGroup().id, deployment().name)
    version: '12.0'
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: '1.2'
    administrators: !empty(sqlAdminObjectId) ? {
      administratorType: 'ActiveDirectory'
      login: sqlAdminDisplayName
      sid: sqlAdminObjectId
      tenantId: tenantId
    } : null
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
  }
}

// Allow Azure Services (including App Service) to access SQL Server
resource sqlFirewallRule 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: sqlServer
  name: 'AllowAllAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Set Azure AD as the only authentication method (disable local auth)
resource sqlAzureAdOnlyAuth 'Microsoft.Sql/servers/azureADOnlyAuthentications@2021-11-01' = if (disableLocalAuth) {
  parent: sqlServer
  name: 'Default'
  properties: {
    azureADOnlyAuthentication: true
  }
  dependsOn: [
    sqlFirewallRule
  ]
}

// Output SQL Server details
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerName string = sqlServer.name
output sqlServerResourceId string = sqlServer.id
