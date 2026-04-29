// Azure Key Vault with RBAC authorization.
// Roles assigned:
//   - Container App MI: Key Vault Secrets User (read-only)
//   - Human admin (aadAdminObjectId): Key Vault Secrets Officer (manage secrets)

param name string
param location string
param tags object
param tenantId string
param managedIdentityPrincipalId string
param aadAdminObjectId string
param purgeProtection bool = false

@description('Built-in role: Key Vault Secrets User')
var kvSecretsUserRoleId = '4633458b-17de-46cb-8b2f-2e01c1e00a91'

@description('Built-in role: Key Vault Secrets Officer')
var kvSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: purgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource kvMiReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, managedIdentityPrincipalId, kvSecretsUserRoleId)
  properties: {
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
  }
}

resource kvAdminOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(aadAdminObjectId)) {
  scope: kv
  name: guid(kv.id, aadAdminObjectId, kvSecretsOfficerRoleId)
  properties: {
    principalId: aadAdminObjectId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsOfficerRoleId)
  }
}

output id string = kv.id
output name string = kv.name
output uri string = kv.properties.vaultUri
