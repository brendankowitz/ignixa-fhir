// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('Network Security Perimeter name')
param nspName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Environment name')
param environment string = 'production'

@description('Storage Account resource ID to associate')
param storageAccountId string = ''

@description('SQL Server resource ID to associate')
param sqlServerId string = ''

@description('Log Analytics workspace resource ID to associate')
param logAnalyticsWorkspaceId string = ''

// Create Network Security Perimeter (in learning/audit mode for monitoring)
resource networkSecurityPerimeter 'Microsoft.Network/networkSecurityPerimeters@2023-08-01-preview' = {
  name: nspName
  location: location
  tags: {
    environment: environment
    purpose: 'FHIR Server Security Perimeter - Learning Mode'
  }
  properties: {}
}

// Create NSP Profile (required before creating associations)
resource nspProfile 'Microsoft.Network/networkSecurityPerimeters/profiles@2023-08-01-preview' = {
  parent: networkSecurityPerimeter
  name: 'default-profile'
  properties: {}
}

// Associate Storage Account with NSP (learning mode for audit/monitoring)
resource storageAssociation 'Microsoft.Network/networkSecurityPerimeters/resourceAssociations@2023-08-01-preview' = if (!empty(storageAccountId)) {
  parent: networkSecurityPerimeter
  name: 'storage-association'
  properties: {
    privateLinkResource: {
      id: storageAccountId
    }
    profile: {
      id: nspProfile.id
    }
    accessMode: 'Learning'
  }
}

// Associate SQL Server with NSP (learning mode for audit/monitoring)
resource sqlAssociation 'Microsoft.Network/networkSecurityPerimeters/resourceAssociations@2023-08-01-preview' = if (!empty(sqlServerId)) {
  parent: networkSecurityPerimeter
  name: 'sql-association'
  properties: {
    privateLinkResource: {
      id: sqlServerId
    }
    profile: {
      id: nspProfile.id
    }
    accessMode: 'Learning'
  }
}

// Associate Log Analytics workspace with NSP (learning mode for audit/monitoring)
resource logAnalyticsAssociation 'Microsoft.Network/networkSecurityPerimeters/resourceAssociations@2023-08-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  parent: networkSecurityPerimeter
  name: 'loganalytics-association'
  properties: {
    privateLinkResource: {
      id: logAnalyticsWorkspaceId
    }
    profile: {
      id: nspProfile.id
    }
    accessMode: 'Learning'
  }
}

// Output NSP details
output nspId string = networkSecurityPerimeter.id
output nspName string = networkSecurityPerimeter.name
