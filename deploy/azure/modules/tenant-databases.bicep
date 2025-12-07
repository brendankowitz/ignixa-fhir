// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('SQL Server name (must already exist)')
param sqlServerName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Number of tenant databases to create (1-50)')
@minValue(1)
@maxValue(50)
param tenantCount int

@description('Environment name')
param environment string = 'production'

@description('Database SKU name (Basic, S0, S1, etc.)')
param databaseSku string = 'Basic'

@description('Database tier (Basic, Standard, Premium)')
param databaseTier string = 'Basic'

@description('Database capacity (DTU or vCore)')
param databaseCapacity int = 5

@description('Maximum database size in bytes')
param maxSizeBytes int = 2147483648 // 2 GB

// Reference the existing SQL Server
resource sqlServer 'Microsoft.Sql/servers@2021-11-01' existing = {
  name: sqlServerName
}

// Create a database for each tenant
// Tenant databases are named: FhirTenant1, FhirTenant2, etc.
resource tenantDatabases 'Microsoft.Sql/servers/databases@2021-11-01' = [for i in range(1, tenantCount): {
  parent: sqlServer
  name: 'FhirTenant${i}'
  location: location
  sku: {
    name: databaseSku
    tier: databaseTier
    capacity: databaseCapacity
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS' // Default: case-insensitive
    maxSizeBytes: maxSizeBytes
    catalogCollation: 'DATABASE_DEFAULT'
    createMode: 'Default'
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
    tenantId: string(i)
  }
}]

// Enable Transparent Data Encryption (TDE) for each database
resource databaseTde 'Microsoft.Sql/servers/databases/transparentDataEncryption@2021-11-01' = [for i in range(1, tenantCount): {
  parent: tenantDatabases[i - 1]
  name: 'current'
  properties: {
    state: 'Enabled'
  }
}]

// Configure security alerts for each database
resource databaseSecurityAlerts 'Microsoft.Sql/servers/databases/securityAlertPolicies@2021-11-01' = [for i in range(1, tenantCount): {
  parent: tenantDatabases[i - 1]
  name: 'default'
  properties: {
    state: 'Enabled'
    disabledAlerts: []
    emailAddresses: []
    emailAccountAdmins: false
    retentionDays: 0
  }
}]

// Output database names for consumption by other modules
output databaseNames array = [for i in range(1, tenantCount): 'FhirTenant${i}']
output connectionStringTemplates array = [for i in range(1, tenantCount): 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=FhirTenant${i};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;Authentication=Active Directory Managed Identity;']
