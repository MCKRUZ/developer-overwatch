@description('Environment name used for resource naming.')
param environmentName string

@description('Azure region for the managed identity.')
param location string

@description('Tags to apply to all resources.')
param tags object

@description('Name of the Key Vault the API reads its secrets from.')
param keyVaultName string

// Built-in "Key Vault Secrets User": read secret contents, nothing else.
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// User-assigned rather than system-assigned so the identity and its role exist before the API's first revision tries to resolve Key Vault secrets.
resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'overwatch-${environmentName}-api-id'
  location: location
  tags: tags
}

resource secretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiIdentity.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

@description('Resource ID of the API managed identity.')
output id string = apiIdentity.id

@description('Principal (object) ID of the API managed identity.')
output principalId string = apiIdentity.properties.principalId
