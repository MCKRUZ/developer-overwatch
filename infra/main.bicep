targetScope = 'resourceGroup'

@description('Environment name (dev, staging, prod).')
param environmentName string = 'dev'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Docker image tag for the API container.')
param apiImageTag string = 'latest'

@description('Docker image tag for the Dashboard container.')
param dashboardImageTag string = 'latest'

@description('SQL Server administrator login.')
@secure()
param sqlAdminLogin string = ''

@description('SQL Server administrator password.')
@secure()
param sqlAdminPassword string = ''

@description('Use Azure SQL instead of SQLite. When false, SQLite with Azure File share is used.')
param useSqlServer bool = false

var tags = {
  environment: environmentName
  project: 'developer-overwatch'
}

module containerRegistry 'modules/container-registry.bicep' = {
  name: 'container-registry'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module sqlDatabase 'modules/sql-database.bicep' = if (useSqlServer) {
  name: 'sql-database'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
  }
}

module containerApps 'modules/container-apps.bicep' = {
  name: 'container-apps'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    acrLoginServer: containerRegistry.outputs.loginServer
    acrName: containerRegistry.outputs.name
    apiImageTag: apiImageTag
    dashboardImageTag: dashboardImageTag
    logAnalyticsId: monitoring.outputs.logAnalyticsId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultUri: keyVault.outputs.uri
    databaseProvider: useSqlServer ? 'sqlserver' : 'sqlite'
    sqlConnectionString: useSqlServer ? sqlDatabase.outputs.connectionString : ''
  }
}

@description('URL of the deployed API.')
output apiUrl string = containerApps.outputs.apiUrl

@description('URL of the deployed Dashboard.')
output dashboardUrl string = containerApps.outputs.dashboardUrl

@description('ACR login server for docker push.')
output acrLoginServer string = containerRegistry.outputs.loginServer

@description('Application Insights connection string for telemetry.')
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
