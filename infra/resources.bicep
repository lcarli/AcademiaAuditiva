// =============================================================================
// resources.bicep — full stack inside the resource group
// =============================================================================

targetScope = 'resourceGroup'

param resourcePrefix string
param envName string
param location string
param tags object
param aadAdminObjectId string
param aadAdminLogin string
param aadTenantId string
param containerAppMinReplicas int
param containerAppMaxReplicas int
param containerImage string
param sqlSku string

var nameBase = '${resourcePrefix}-${envName}'
var nameBaseFlat = '${resourcePrefix}${envName}'
var unique6 = take(uniqueString(resourceGroup().id), 6)

var keyVaultName = take('kv-${nameBase}-${unique6}', 24)
var acrName = take('cr${nameBaseFlat}${unique6}', 50)
var sqlServerName = 'sql-${nameBase}-${unique6}'
var sqlDatabaseName = 'sqldb-${nameBase}'
var managedIdentityName = 'id-${nameBase}'
var logAnalyticsName = 'log-${nameBase}'
var appInsightsName = 'appi-${nameBase}'
var containerAppEnvName = 'cae-${nameBase}'
var containerAppName = 'ca-${nameBase}'
var storageAccountName = take('st${nameBaseFlat}${unique6}', 24)

// -----------------------------------------------------------------------------
// Identity
// -----------------------------------------------------------------------------

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    name: managedIdentityName
    location: location
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Monitoring (Log Analytics + App Insights)
// -----------------------------------------------------------------------------

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
    location: location
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Container Registry
// -----------------------------------------------------------------------------

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    name: acrName
    location: location
    tags: tags
    managedIdentityPrincipalId: identity.outputs.principalId
  }
}

// -----------------------------------------------------------------------------
// Key Vault (RBAC)
// -----------------------------------------------------------------------------

module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: keyVaultName
    location: location
    tags: tags
    tenantId: aadTenantId
    managedIdentityPrincipalId: identity.outputs.principalId
    aadAdminObjectId: aadAdminObjectId
    purgeProtection: envName == 'prd'
  }
}

// -----------------------------------------------------------------------------
// Azure SQL
// -----------------------------------------------------------------------------

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    location: location
    tags: tags
    aadAdminObjectId: aadAdminObjectId
    aadAdminLogin: aadAdminLogin
    aadTenantId: aadTenantId
    sku: sqlSku
  }
}

// -----------------------------------------------------------------------------
// Storage (audio assets + exercise logs)
// -----------------------------------------------------------------------------

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: storageAccountName
    location: location
    tags: tags
    managedIdentityPrincipalId: identity.outputs.principalId
  }
}

// -----------------------------------------------------------------------------
// Container Apps
// -----------------------------------------------------------------------------

// Write the SQL connection string to KV deterministically. Built from the
// same parts the container app would use; keeping this resource here (rather
// than as a placeholder in modules/keyvault.bicep) means redeploys do not
// clobber an externally-rotated value with a placeholder, and the value is
// always in sync with the SQL outputs.
resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: '${keyVaultName}/ConnectionStrings--DefaultConnection'
  properties: {
    value: 'Server=tcp:${sql.outputs.serverFqdn},1433;Initial Catalog=${sql.outputs.databaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;User Id=${identity.outputs.clientId}'
    contentType: 'text/plain'
  }
  dependsOn: [
    keyvault
    sql
    identity
  ]
}

module caeEnv 'modules/containerapp-env.bicep' = {
  name: 'cae-env'
  params: {
    name: containerAppEnvName
    location: location
    tags: tags
    logAnalyticsCustomerId: monitoring.outputs.logAnalyticsCustomerId
    logAnalyticsSharedKey: monitoring.outputs.logAnalyticsSharedKey
  }
}

module containerApp 'modules/containerapp.bicep' = {
  name: 'containerapp'
  params: {
    name: containerAppName
    location: location
    tags: tags
    environmentId: caeEnv.outputs.id
    managedIdentityId: identity.outputs.id
    managedIdentityClientId: identity.outputs.clientId
    containerImage: containerImage
    registryServer: registry.outputs.loginServer
    keyVaultUri: keyvault.outputs.uri
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    minReplicas: containerAppMinReplicas
    maxReplicas: containerAppMaxReplicas
    adminEmail: aadAdminLogin
    storageBlobEndpoint: storage.outputs.blobEndpoint
  }
  dependsOn: [
    sqlConnectionStringSecret
  ]
}

// -----------------------------------------------------------------------------
// Outputs
// -----------------------------------------------------------------------------

output keyVaultName string = keyvault.outputs.name
output keyVaultUri string = keyvault.outputs.uri
output containerRegistryLoginServer string = registry.outputs.loginServer
output containerAppFqdn string = containerApp.outputs.fqdn
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output managedIdentityClientId string = identity.outputs.clientId
output managedIdentityPrincipalId string = identity.outputs.principalId
output applicationInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output storageAccountName string = storage.outputs.name
output storageBlobEndpoint string = storage.outputs.blobEndpoint
output audioBaseUrl string = storage.outputs.pianoAudioBaseUrl
