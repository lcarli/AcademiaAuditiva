// Azure SQL Server + Database with AAD-only authentication.
// The Container App MI must be granted as a contained DB user via the
// post-deploy script `infra/scripts/grant-mi-sql.ps1` (run once after deploy).

param sqlServerName string
param sqlDatabaseName string
param location string
param tags object
param aadAdminObjectId string
param aadAdminLogin string
param aadTenantId string
@allowed([ 'Basic', 'S0', 'S1', 'GP_S_Gen5_1' ])
param sku string = 'Basic'

var skuMap = {
  Basic: { name: 'Basic', tier: 'Basic', capacity: 5 }
  S0: { name: 'S0', tier: 'Standard', capacity: 10 }
  S1: { name: 'S1', tier: 'Standard', capacity: 20 }
  GP_S_Gen5_1: { name: 'GP_S_Gen5_1', tier: 'GeneralPurpose', capacity: 1 }
}

resource server 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: aadAdminLogin
      sid: aadAdminObjectId
      tenantId: aadTenantId
      azureADOnlyAuthentication: true
    }
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: '1.2'
    version: '12.0'
  }
}

// Allow Azure services (Container Apps egress) to reach the SQL server.
// Range 0.0.0.0 - 0.0.0.0 is the documented "Allow Azure services" toggle.
resource fwAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: server
  name: 'AllowAllAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: server
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: skuMap[sku]
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    readScale: 'Disabled'
    requestedBackupStorageRedundancy: 'Local'
    autoPauseDelay: sku == 'GP_S_Gen5_1' ? 60 : -1
    minCapacity: sku == 'GP_S_Gen5_1' ? json('0.5') : null
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = db.name
