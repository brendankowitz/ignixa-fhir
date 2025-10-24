// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('SQL Server name (must be globally unique)')
param sqlServerName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Database name')
param databaseName string = 'FhirDatabase'

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

// Create FHIR Database with appropriate collation for case-sensitive searches
resource fhirDatabase 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS' // Default: case-insensitive
    maxSizeBytes: 2147483648 // 2 GB
    catalogCollation: 'DATABASE_DEFAULT'
    createMode: 'Default'
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
  }
}

// Enable Transparent Data Encryption (TDE) for database encryption at rest
resource databaseTde 'Microsoft.Sql/servers/databases/transparentDataEncryption@2021-11-01' = {
  parent: fhirDatabase
  name: 'current'
  properties: {
    state: 'Enabled'
  }
}

// Configure database security policy
resource databaseSecurityAlerts 'Microsoft.Sql/servers/databases/securityAlertPolicies@2021-11-01' = {
  parent: fhirDatabase
  name: 'default'
  properties: {
    state: 'Enabled'
    disabledAlerts: []
    emailAddresses: []
    emailNotificationAdmins: false
    retentionDays: 0
  }
}

// Output SQL Server details
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerName string = sqlServer.name
output sqlServerResourceId string = sqlServer.id
output databaseName string = fhirDatabase.name
output databaseResourceId string = fhirDatabase.id

// Connection string for reference (use Managed Identity at runtime, not this string)
output connectionStringTemplate string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${databaseName};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;'
