// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('Storage account name')
param storageAccountName string

@description('Principal ID of the App Service Managed Identity')
param principalId string

// Reference existing storage account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// Storage Blob Data Contributor - Required for DurableTask and BlobStorage
// Allows read, write, delete on blob containers and data
resource storageBlobContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, principalId, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Queue Data Contributor - Required for DurableTask message queues
// Allows read, write, delete on queues and messages
resource storageQueueContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, principalId, '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Table Data Contributor - Required for DurableTask history tables
// Allows read, write, delete on tables and entities
resource storageTableContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, principalId, '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
