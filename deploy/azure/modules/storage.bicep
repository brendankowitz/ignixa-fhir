// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('Storage account name (must be globally unique, 3-24 alphanumeric lowercase)')
param storageAccountName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Principal ID of the App Service Managed Identity (for RBAC role assignment)')
param principalId string

@description('Disable local storage auth (Managed Identity only)')
param disableLocalAuth bool = true

@description('Environment name')
param environment string = 'production'

// Create Storage Account with System Managed Identity
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS' // Locally Redundant Storage (sufficient for non-critical data)
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: !disableLocalAuth // Disable shared key (local auth)
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices' // Allow Azure services (App Service)
      defaultAction: 'Allow' // Allow all by default (can be restricted later)
    }
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server'
  }
}

// Create blob service
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

// Create container for FHIR exports/imports
resource exportContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'fhir-exports'
  properties: {
    publicAccess: 'None'
  }
}

// Create container for bulk import staging
resource importContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'fhir-imports'
  properties: {
    publicAccess: 'None'
  }
}

// Assign Storage Blob Data Contributor role to App Service Managed Identity
// This allows the App Service to read/write blobs in the storage account
// Only create role assignment if principalId is provided (storage deployed after app service)
resource storageBlobContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  scope: storageAccount
  name: guid(storageAccount.id, principalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe') // Storage Blob Data Contributor role
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Output storage account details
output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output storageAccountResourceId string = storageAccount.id

// Output container names for application configuration
output exportContainerName string = exportContainer.name
output importContainerName string = importContainer.name
