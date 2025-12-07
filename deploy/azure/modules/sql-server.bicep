// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('SQL Server name (must be globally unique)')
param sqlServerName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Environment name')
param environment string = 'production'

@description('SQL Server admin password (placeholder - Azure AD-only auth is used instead)')
@minLength(8)
@maxLength(128)
@secure()
param sqlAdminPassword string = newGuid()

@description('User-Assigned Managed Identity principal ID (for SQL AAD admin)')
param sqlAdminPrincipalId string = ''

@description('User-Assigned Managed Identity name (for SQL AAD admin)')
param sqlAdminName string = ''

@description('Azure AD tenant ID')
param tenantId string = subscription().tenantId

// Create Azure SQL Server with System-Assigned Managed Identity
// Uses Azure AD-only authentication (no local SQL logins)
resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    administratorLogin: 'azureAdmin' // Placeholder, not used with AD-only auth
    administratorLoginPassword: sqlAdminPassword // Placeholder, not used with AD-only auth
    version: '12.0'
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: '1.2'
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

// Configure User-Assigned Managed Identity as SQL Server admin (required for AAD-only authentication)
resource sqlAadAdmin 'Microsoft.Sql/servers/administrators@2021-11-01' = if (!empty(sqlAdminPrincipalId) && !empty(sqlAdminName)) {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: sqlAdminName
    sid: sqlAdminPrincipalId
    tenantId: tenantId
  }
}

// Enable Azure AD-only authentication (no local SQL logins)
// NOTE: This depends on sqlAadAdmin being configured first
resource sqlAzureAdOnlyAuth 'Microsoft.Sql/servers/azureADOnlyAuthentications@2021-11-01' = if (!empty(sqlAdminPrincipalId)) {
  parent: sqlServer
  name: 'Default'
  properties: {
    azureADOnlyAuthentication: true
  }
  dependsOn: [
    sqlAadAdmin
  ]
}

// Output SQL Server details
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerName string = sqlServer.name
output sqlServerResourceId string = sqlServer.id
