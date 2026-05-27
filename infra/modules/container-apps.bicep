@description('Environment name used for resource naming.')
param environmentName string

@description('Azure region for Container Apps resources.')
param location string

@description('Tags to apply to all resources.')
param tags object

@description('ACR login server URL.')
param acrLoginServer string

@description('ACR resource name for pull credentials.')
param acrName string

@description('Docker image tag for the API container.')
param apiImageTag string

@description('Docker image tag for the Dashboard container.')
param dashboardImageTag string

@description('Log Analytics Workspace resource ID.')
param logAnalyticsId string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Key Vault URI for secret references.')
param keyVaultUri string

@description('Database provider: sqlite or sqlserver.')
param databaseProvider string = 'sqlite'

@description('SQL Server connection string (used when databaseProvider is sqlserver).')
@secure()
param sqlConnectionString string = ''

var envName = 'overwatch-${environmentName}-env'
var apiAppName = 'overwatch-${environmentName}-api'
var dashboardAppName = 'overwatch-${environmentName}-dashboard'
var storageName = 'overwatch${environmentName}sa'
var fileShareName = 'overwatch-data'

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: acrName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource fileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: fileShareName
  properties: {
    shareQuota: 1
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: split(logAnalyticsId, '/')[8]
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: envName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource envStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: containerAppsEnv
  name: 'sqlitedata'
  properties: {
    azureFile: {
      accountName: storageAccount.name
      accountKey: storageAccount.listKeys().keys[0].value
      shareName: fileShareName
      accessMode: 'ReadWrite'
    }
  }
}

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 5000
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: acrLoginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrLoginServer}/overwatch-api:${apiImageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'Database__Provider'
              value: databaseProvider
            }
            {
              name: 'Database__ConnectionString'
              value: databaseProvider == 'sqlserver' ? sqlConnectionString : 'Data Source=/data/overwatch.db'
            }
            {
              name: 'Authentication__Authority'
              value: ''
            }
            {
              name: 'Authentication__Audience'
              value: ''
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
            {
              name: 'KeyVault__Uri'
              value: keyVaultUri
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 5000
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 5000
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 15
            }
          ]
          volumeMounts: databaseProvider == 'sqlite' ? [
            {
              volumeName: 'sqlitedata'
              mountPath: '/data'
            }
          ] : []
        }
      ]
      volumes: databaseProvider == 'sqlite' ? [
        {
          name: 'sqlitedata'
          storageName: envStorage.name
          storageType: 'AzureFile'
        }
      ] : []
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

resource dashboardApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: dashboardAppName
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 80
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: acrLoginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'dashboard'
          image: '${acrLoginServer}/overwatch-dashboard:${dashboardImageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
    }
  }
}

@description('FQDN of the API Container App.')
output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'

@description('FQDN of the Dashboard Container App.')
output dashboardUrl string = 'https://${dashboardApp.properties.configuration.ingress.fqdn}'
