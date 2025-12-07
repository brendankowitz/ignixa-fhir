// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('User-Assigned Managed Identity name (must be globally unique)')
param identityName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Environment name')
param environment string = 'production'

// Create User-Assigned Managed Identity
resource userAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: {
    environment: environment
    purpose: 'SQL Authentication'
  }
}

// Output identity details
output identityId string = userAssignedIdentity.id
output principalId string = userAssignedIdentity.properties.principalId
output clientId string = userAssignedIdentity.properties.clientId
output identityName string = userAssignedIdentity.name
output identityResourceId string = userAssignedIdentity.id
