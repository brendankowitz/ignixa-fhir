// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

@description('Application Insights name')
param appInsightsName string

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Environment name')
param environment string = 'production'

@description('Log retention in days (default 30 days)')
param logRetentionDays int = 30

// Create Log Analytics Workspace (required for Application Insights)
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appInsightsName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018' // Pay-as-you-go pricing
    }
    retentionInDays: logRetentionDays
    workspaceCapping: {
      dailyQuotaGb: 10 // 10 GB daily cap to control costs
    }
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server Monitoring'
  }
}

// Create Application Insights connected to Log Analytics
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    RetentionInDays: logRetentionDays
    DisableIpMasking: false
    ImmediatePurgeDataOn30Days: true
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
  tags: {
    environment: environment
    purpose: 'FHIR Server Monitoring'
  }
}

// Create alert rule for high CPU usage
resource highCpuAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${appInsightsName}-high-cpu'
  location: 'global'
  properties: {
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Cpu Percentage'
          metricName: 'ProcessorTime'
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: []
  }
  tags: {
    environment: environment
  }
}

// Create alert rule for failed requests
resource failedRequestsAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${appInsightsName}-failed-requests'
  location: 'global'
  properties: {
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Failed Requests Count'
          metricName: 'failedRequests'
          operator: 'GreaterThan'
          threshold: 10
          timeAggregation: 'Count'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: []
  }
  tags: {
    environment: environment
  }
}

// Output monitoring configuration for App Service
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
output appInsightsResourceId string = appInsights.id
