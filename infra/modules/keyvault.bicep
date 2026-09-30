// Azure Key Vault with RBAC authorization, reachable only through its
// private endpoint (public network access disabled).
// Roles assigned:
//   - Container App MI: Key Vault Secrets User (read-only)
//   - Container App MI: Key Vault Crypto Service Encryption User on the
//     Data Protection key only (get/wrap/unwrap)
//   - Human admin (aadAdminObjectId): Key Vault Secrets Officer (manage secrets)
// Secrets other than ConnectionStrings--DefaultConnection (written by
// resources.bicep) are optional and never declared here, so a redeploy can't
// overwrite them: set them with infra/scripts/seed-keyvault.ps1.

param name string
param location string
param tags object
param tenantId string
param managedIdentityPrincipalId string
param aadAdminObjectId string
param purgeProtection bool = false

@description('Built-in role: Key Vault Secrets User')
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

@description('Built-in role: Key Vault Secrets Officer')
var kvSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

@description('Built-in role: Key Vault Crypto Service Encryption User')
var kvCryptoServiceEncryptionUserRoleId = 'e147488a-f6f5-4113-8e2d-b22465e65bf6'

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
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Deny'
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

// Wraps the ASP.NET Core Data Protection key ring stored in blob storage.
// ARM only creates the first version; redeploys leave an existing key untouched.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: kv
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource kvMiDataProtection 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: dataProtectionKey
  name: guid(dataProtectionKey.id, managedIdentityPrincipalId, kvCryptoServiceEncryptionUserRoleId)
  properties: {
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvCryptoServiceEncryptionUserRoleId)
  }
}

output id string = kv.id
output name string = kv.name
output uri string = kv.properties.vaultUri
output dataProtectionKeyUri string = dataProtectionKey.properties.keyUri
