// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('SQL Server resource ID')
param sqlServerResourceId string

@description('SQL Database resource ID')
param sqlDatabaseResourceId string

@description('Principal ID of the App Service Managed Identity')
param appServicePrincipalId string

@description('Environment name')
param environment string = 'production'

// Note: SQL Server and Database resources are references to existing resources
// created in other modules. We use the IDs passed as parameters to assign RBAC roles.

// Assign SQL Server Contributor role at server level (allows MI to authenticate via Azure AD)
// This is necessary for the App Service to establish connections using its managed identity
resource sqlServerAzureAdRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(sqlServerResourceId, appServicePrincipalId, 'e8de7b46-7ce0-41cb-b8c5-f02ef6b100f6')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'e8de7b46-7ce0-41cb-b8c5-f02ef6b100f6') // SQL Server Contributor
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Note: Database-level role assignments are typically handled through SQL scripts
// This module provides the RBAC infrastructure for Azure-level permissions.
// SQL database reader/writer roles are assigned via T-SQL in setup-sql-mi.sql
