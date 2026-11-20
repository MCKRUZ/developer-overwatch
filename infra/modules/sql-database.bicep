@description('Environment name used for resource naming.')
param environmentName string

@description('Short suffix that makes names unique across all of Azure (vault, storage, registry and SQL server names are global).')
param nameSuffix string

@description('Azure region for SQL resources. Can differ from the apps: some subscriptions may not create SQL servers in every region.')
param location string

@description('Tags to apply to all resources.')
param tags object

@description('Name of the API managed identity, shown as the server admin login.')
param adminIdentityName string

@description('Client (application) ID of the API managed identity. Azure SQL identifies a managed identity by this ID, not its object ID.')
param adminIdentityClientId string

var serverName = 'overwatch-${environmentName}-sql-${nameSuffix}'
var databaseName = 'overwatch-${environmentName}-db'

// Entra sign-in only, with the API's managed identity as admin: no password exists anywhere, and the API can run
// its own migrations on startup without a separate step to create a database user.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
      login: adminIdentityName
      sid: adminIdentityClientId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// Container Apps on the consumption plan have no fixed outbound address, so Azure services are allowed through;
// Entra-only sign-in means a caller still needs a token for an identity the server accepts.
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// prod: Standard S0 (10 DTU, 250 GB); dev: Basic (5 DTU, 2 GB).
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: environmentName == 'prod' ? 'S0' : 'Basic'
    tier: environmentName == 'prod' ? 'Standard' : 'Basic'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: environmentName == 'prod' ? 268435456000 : 2147483648
    zoneRedundant: false
  }
}

@description('SQL Server fully qualified domain name.')
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('Database name.')
output databaseName string = sqlDatabase.name

@description('Connection string with no credential; the API attaches a managed-identity token to each connection.')
output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabase.name};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
