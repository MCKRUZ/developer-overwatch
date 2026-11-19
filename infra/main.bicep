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

@description('OIDC authority the API trusts for dashboard sign-in, e.g. https://login.microsoftonline.com/<tenant-id>/v2.0. Required: without it every dashboard read, prompt text included, would be open to anyone who finds the URL.')
@minLength(1)
param oidcAuthority string

@description('Audience the API requires in dashboard sign-in tokens. For an Entra app registration issuing v2 tokens this is the app registration client ID.')
@minLength(1)
param oidcAudience string

@description('False deploys only the registry, monitoring, Key Vault and API identity, so a first deploy can push images and create the agent key before any app starts.')
param deployApps bool = true

@description('Key Vault secret names holding accepted agent API keys. List two (old and new) temporarily during a key rotation. Each secret must exist in the vault before deploying.')
@minLength(1)
param agentApiKeySecretNames array = ['agent-api-key']

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

module apiIdentity 'modules/api-identity.bicep' = {
  name: 'api-identity'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    keyVaultName: keyVault.outputs.name
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

module containerApps 'modules/container-apps.bicep' = if (deployApps) {
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
    apiIdentityId: apiIdentity.outputs.id
    agentApiKeySecretNames: agentApiKeySecretNames
    databaseProvider: useSqlServer ? 'sqlserver' : 'sqlite'
    sqlConnectionString: useSqlServer ? sqlDatabase.outputs.connectionString : ''
    oidcAuthority: oidcAuthority
    oidcAudience: oidcAudience
  }
}

@description('URL of the deployed API; empty when deployApps is false.')
output apiUrl string = containerApps.?outputs.apiUrl ?? ''

@description('URL of the deployed Dashboard; empty when deployApps is false.')
output dashboardUrl string = containerApps.?outputs.dashboardUrl ?? ''

@description('Name of the Key Vault holding the agent API keys.')
output keyVaultName string = keyVault.outputs.name

@description('Key Vault secret names the API reads agent keys from; each must exist before the apps deploy.')
output agentApiKeySecretNames array = agentApiKeySecretNames

@description('ACR login server for docker push.')
output acrLoginServer string = containerRegistry.outputs.loginServer

@description('Application Insights connection string for telemetry.')
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
