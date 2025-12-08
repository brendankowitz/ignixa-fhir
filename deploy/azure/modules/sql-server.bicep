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

@description('User-Assigned Managed Identity principal ID (for SQL AAD admin) - REQUIRED for Entra-only auth')
param sqlAdminPrincipalId string

@description('User-Assigned Managed Identity name (for SQL AAD admin) - REQUIRED for Entra-only auth')
param sqlAdminName string

@description('Azure AD tenant ID')
param tenantId string = subscription().tenantId

// Create Azure SQL Server with Entra-only authentication enabled at creation time
// This satisfies Azure Policy: "Azure SQL Database should have Microsoft Entra-only authentication enabled during creation"
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    version: '12.0'
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: '1.2'
    // Entra-only authentication configured at creation time (required by policy)
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
      login: sqlAdminName
      sid: sqlAdminPrincipalId
      tenantId: tenantId
      azureADOnlyAuthentication: true
    }
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

// NOTE: AAD admin and Entra-only auth are now configured inline on the SQL Server resource
// to satisfy the Azure Policy requiring Entra-only auth during creation

// Output SQL Server details
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerName string = sqlServer.name
output sqlServerResourceId string = sqlServer.id
