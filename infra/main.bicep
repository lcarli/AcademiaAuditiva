// =============================================================================
// Academia Auditiva — main.bicep
// Subscription-scoped entry point. Creates the resource group and deploys the
// full stack via the resources module.
//
// Deploy with the wrapper, which keeps the running image and the custom domain
// certificates (a plain `az deployment sub create` resets the image to the
// placeholder below):
//   az login --tenant 1d70d939-06d2-4348-b658-58cb38886348
//   ./infra/scripts/deploy-infra.ps1 -WhatIf
//   ./infra/scripts/deploy-infra.ps1
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

@description('Initial container image. Bicep deploys a placeholder; CD updates it, and deploy-infra.ps1 passes the running image on redeploys.')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Azure SQL DB SKU.')
@allowed([ 'Basic', 'S0', 'S1', 'GP_S_Gen5_1' ])
param sqlSku string = 'Basic'

@description('Email of the application\'s bootstrap admin (Admin__Email). Created on first start if missing, with the Key Vault secret Admin--InitialPassword when set.')
param appAdminEmail string = ''

@description('VNet address space. The Container Apps subnet takes the first /23, private endpoints the third /24.')
param vnetAddressPrefix string = '10.60.0.0/16'

@description('Custom domains: [{ name, validationMethod: HTTP (apex, A record to the environment IP) | CNAME (subdomain, CNAME to the app FQDN) }]. The DNS records, including asuid.<name> TXT, must exist before a domain is added.')
param customDomains array = []

@description('True when the managed certificates of customDomains already exist (deploy-infra.ps1 detects it): bind them directly instead of issuing them.')
param customDomainCertificatesExist bool = false

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
    appAdminEmail: appAdminEmail
    vnetAddressPrefix: vnetAddressPrefix
    customDomains: customDomains
    customDomainCertificatesExist: customDomainCertificatesExist
  }
}

// -----------------------------------------------------------------------------
// Outputs
// -----------------------------------------------------------------------------

output resourceGroupName string = rg.name
output keyVaultName string = stack.outputs.keyVaultName
output keyVaultUri string = stack.outputs.keyVaultUri
output containerRegistryLoginServer string = stack.outputs.containerRegistryLoginServer
output containerAppName string = stack.outputs.containerAppName
output containerAppFqdn string = stack.outputs.containerAppFqdn
output containerAppEnvironmentName string = stack.outputs.containerAppEnvironmentName
output containerAppEnvironmentStaticIp string = stack.outputs.containerAppEnvironmentStaticIp
output customDomainVerificationId string = stack.outputs.customDomainVerificationId
output sqlServerFqdn string = stack.outputs.sqlServerFqdn
output sqlDatabaseName string = stack.outputs.sqlDatabaseName
output managedIdentityClientId string = stack.outputs.managedIdentityClientId
output managedIdentityPrincipalId string = stack.outputs.managedIdentityPrincipalId
output applicationInsightsConnectionString string = stack.outputs.applicationInsightsConnectionString
output storageAccountName string = stack.outputs.storageAccountName
output storageBlobEndpoint string = stack.outputs.storageBlobEndpoint
output audioBaseUrl string = stack.outputs.audioBaseUrl
