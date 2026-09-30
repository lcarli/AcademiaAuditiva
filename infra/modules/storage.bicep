// Storage account for Academia Auditiva audio assets and exercise logs.
// Containers:
//   - piano-audio          : private (source mp3 sample bank, accessed via MI)
//   - piano-audio-mixed    : private (server-mixed per-round clips, 1h lifecycle)
//   - piano-audio-crypto   : private (legacy — kept until consumers retire)
//   - exercice-logs        : private (server-only writes)
//   - dataprotection-keys  : private (ASP.NET Core Data Protection key ring)
//
// The Container App's user-assigned MI receives "Storage Blob Data Contributor"
// so the app can read source mp3s, write mixed blobs, write logs and maintain
// the Data Protection key ring without keys.
//
// Only the app talks to the account, through its blob private endpoint:
// public network access and shared key auth are disabled, and browsers get
// audio from the app, never from blob URLs, so there is no CORS.

param name string
param location string
param tags object
param managedIdentityPrincipalId string

@description('Built-in role: Storage Blob Data Contributor')
var blobContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource sa 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Deny'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: sa
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
  }
}

resource pianoAudio 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'piano-audio'
  properties: {
    publicAccess: 'None'
  }
}

// Per-round mixed audio clips written by AudioMixerService. The blob
// name is a content hash (mix-{sha256}.wav), so identical rounds reuse
// the same blob. The lifecycle rule below deletes anything in this
// container that hasn't been touched for a day (Azure lifecycle has
// day-level granularity); plenty for a 15-min round TTL.
resource pianoAudioMixed 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'piano-audio-mixed'
  properties: {
    publicAccess: 'None'
  }
}

resource mixedLifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: sa
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          enabled: true
          name: 'delete-mixed-after-1h'
          type: 'Lifecycle'
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 1
                }
              }
            }
            filters: {
              blobTypes: [ 'blockBlob' ]
              prefixMatch: [ 'piano-audio-mixed/' ]
            }
          }
        }
      ]
    }
  }
}

resource pianoAudioCrypto 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'piano-audio-crypto'
  properties: {
    publicAccess: 'None'
  }
}

resource exerciceLogs 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'exercice-logs'
  properties: {
    publicAccess: 'None'
  }
}

// Shared key ring so auth/antiforgery cookies survive deploys and work
// across replicas. The keys inside are wrapped by a Key Vault key.
resource dataProtectionKeys 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'dataprotection-keys'
  properties: {
    publicAccess: 'None'
  }
}

resource miBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sa
  name: guid(sa.id, managedIdentityPrincipalId, blobContributorRoleId)
  properties: {
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobContributorRoleId)
  }
}

output id string = sa.id
output name string = sa.name
output blobEndpoint string = sa.properties.primaryEndpoints.blob
output pianoAudioBaseUrl string = '${sa.properties.primaryEndpoints.blob}piano-audio/'
output dataProtectionBlobUri string = '${sa.properties.primaryEndpoints.blob}${dataProtectionKeys.name}/keys.xml'
