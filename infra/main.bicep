// =============================================================================
// Academia Auditiva — main.bicep
// Subscription-scoped entry point. Creates the resource group and deploys the
// full stack via the resources module.
//
// Deploy:
//   az login --tenant 1d70d939-06d2-4348-b658-58cb38886348
//   az account set --subscription 3dc8ff32-42e4-4152-b194-46b704ed70f2
//   az deployment sub create \
//     --location canadacentral \
//     --template-file infra/main.bicep \
//     --parameters infra/main.parameters.prd.json
// =============================================================================

targetScope = 'subscription'

@description('Short prefix used in every resource name (2-4 chars).')
@minLength(2)
@maxLength(4)
param resourcePrefix string = 'aa'

@description('Environment suffix: dev | stg | prd.')
@allowed([ 'dev', 'stg', 'prd' ])
param envName string = 'prd'

@description('Azure region for all resources.')
param location string = 'canadacentral'

@description('Tags applied to every resource.')
param tags object = {
  application: 'AcademiaAuditiva'
  environment: envName
  managedBy: 'bicep'
}

@description('AAD object id (GUID) of the user/group set as Azure SQL AAD admin. Will also receive Key Vault Secrets Officer to manage secrets locally.')
param aadAdminObjectId string

@description('AAD UPN/email of the user/group set as Azure SQL AAD admin (display login).')
param aadAdminLogin string = 'lucas.decarli.ca@gmail.com'

@description('Tenant id where AAD principals live.')
param aadTenantId string = subscription().tenantId

@description('Container App SKU: 1 = always warm; 0 = scale to zero.')
@minValue(0)
@maxValue(2)
param containerAppMinReplicas int = 1

@description('Container App max replicas (HTTP scaling).')
@minValue(1)
@maxValue(10)
param containerAppMaxReplicas int = 3

@description('Initial container image. Bicep deploys a placeholder; azd or CI updates it after the first push.')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Azure SQL DB SKU.')
@allowed([ 'Basic', 'S0', 'S1', 'GP_S_Gen5_1' ])
param sqlSku string = 'Basic'

// -----------------------------------------------------------------------------
// Resource Group
// -----------------------------------------------------------------------------

var rgName = 'rg-${resourcePrefix}-${envName}'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: rgName
  location: location
  tags: tags
}

// -----------------------------------------------------------------------------
// Stack
// -----------------------------------------------------------------------------

module stack 'resources.bicep' = {
  name: 'stack-${envName}'
  scope: rg
  params: {
    resourcePrefix: resourcePrefix
    envName: envName
    location: location
    tags: tags
    aadAdminObjectId: aadAdminObjectId
    aadAdminLogin: aadAdminLogin
    aadTenantId: aadTenantId
    containerAppMinReplicas: containerAppMinReplicas
    containerAppMaxReplicas: containerAppMaxReplicas
    containerImage: containerImage
    sqlSku: sqlSku
  }
}

// -----------------------------------------------------------------------------
// Outputs
// -----------------------------------------------------------------------------

output resourceGroupName string = rg.name
output keyVaultName string = stack.outputs.keyVaultName
output keyVaultUri string = stack.outputs.keyVaultUri
output containerRegistryLoginServer string = stack.outputs.containerRegistryLoginServer
output containerAppFqdn string = stack.outputs.containerAppFqdn
output sqlServerFqdn string = stack.outputs.sqlServerFqdn
output sqlDatabaseName string = stack.outputs.sqlDatabaseName
output managedIdentityClientId string = stack.outputs.managedIdentityClientId
output managedIdentityPrincipalId string = stack.outputs.managedIdentityPrincipalId
output applicationInsightsConnectionString string = stack.outputs.applicationInsightsConnectionString
