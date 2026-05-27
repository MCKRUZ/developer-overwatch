@description('Environment name used for resource naming.')
param environmentName string

@description('Azure region for the Key Vault.')
param location string

@description('Tags to apply to all resources.')
param tags object

var name = 'overwatch-${environmentName}-kv'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: environmentName == 'prod' ? true : false
  }
}

@description('The name of the Key Vault.')
output name string = keyVault.name

@description('The URI of the Key Vault.')
output uri string = keyVault.properties.vaultUri
