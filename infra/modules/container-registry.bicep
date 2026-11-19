@description('Environment name used for naming and SKU selection.')
param environmentName string

@description('Short suffix that makes names unique across all of Azure (vault, storage, registry and SQL server names are global).')
param nameSuffix string

@description('Azure region for the Container Registry.')
param location string

@description('Tags to apply to all resources.')
param tags object

var name = 'overwatchacr${environmentName}${nameSuffix}'
var sku = environmentName == 'prod' ? 'Standard' : 'Basic'

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    adminUserEnabled: true
  }
}

@description('The login server URL for the Container Registry.')
output loginServer string = acr.properties.loginServer

@description('The name of the Container Registry.')
output name string = acr.name
