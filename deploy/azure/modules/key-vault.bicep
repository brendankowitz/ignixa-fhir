// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('Key Vault name (must be globally unique, 3-24 alphanumeric with hyphens)')
param keyVaultName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Tenant ID for Azure AD integration')
param tenantId string = subscription().tenantId

@description('Principal ID of the App Service Managed Identity (for RBAC access)')
param appServicePrincipalId string

@description('Environment name')
param environment string = 'production'

// Create Key Vault with RBAC-only authorization
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    accessPolicies: [] // RBAC-only, no access policies
    enableRbacAuthorization: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    enableSoftDelete: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
  }
}

// Assign Key Vault Secrets User role to App Service Managed Identity
// This allows the App Service to read secrets from Key Vault
resource keyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, appServicePrincipalId, '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Assign Key Vault Certificate User role (for certificate operations if needed)
resource keyVaultCertificateUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, appServicePrincipalId, 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba') // Key Vault Certificate User
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba')
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Output Key Vault details for application configuration
output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultName string = keyVault.name
