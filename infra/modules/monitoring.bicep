@description('Environment name used for resource naming.')
param environmentName string

@description('Azure region for monitoring resources.')
param location string

@description('Tags to apply to all resources.')
param tags object

var logAnalyticsName = 'overwatch-${environmentName}-log'
var appInsightsName = 'overwatch-${environmentName}-insights'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

@description('Resource ID of the Log Analytics Workspace.')
output logAnalyticsId string = logAnalytics.id

@description('Application Insights connection string.')
output appInsightsConnectionString string = appInsights.properties.ConnectionString

@description('Application Insights instrumentation key.')
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
