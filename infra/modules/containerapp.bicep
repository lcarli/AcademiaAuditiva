// Container App for Academia Auditiva.
// Pulls secrets from Key Vault via user-assigned MI, scales 1..N on HTTP.

param name string
param location string
param tags object
param environmentId string
param managedIdentityId string
param managedIdentityClientId string
param containerImage string
param registryServer string
param keyVaultUri string
param sqlServerFqdn string
param sqlDatabaseName string
param appInsightsConnectionString string
param minReplicas int = 1
param maxReplicas int = 3
param targetPort int = 8080
param cpu string = '0.5'
param memory string = '1.0Gi'

@description('Bootstrap admin email for the application (Admin__Email).')
param adminEmail string = ''

// SQL connection string built from outputs. AAD auth via the user-assigned MI.
// User Id=<MI clientId> is required for Active Directory Default to pick the right identity in a multi-MI host.
var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;User Id=${managedIdentityClientId}'

// Key Vault secret names map 1:1 to env vars (KV uses --, .NET config uses __).
var kvSecrets = [
  { kvName: 'ConnectionStrings--DefaultConnection', envName: 'ConnectionStrings__DefaultConnection', refName: 'connectionstrings--defaultconnection' }
  { kvName: 'Facebook--AppId', envName: 'Facebook__AppId', refName: 'facebook--appid' }
  { kvName: 'Facebook--AppSecret', envName: 'Facebook__AppSecret', refName: 'facebook--appsecret' }
  { kvName: 'Smtp--Host', envName: 'Smtp__Host', refName: 'smtp--host' }
  { kvName: 'Smtp--Port', envName: 'Smtp__Port', refName: 'smtp--port' }
  { kvName: 'Smtp--User', envName: 'Smtp__User', refName: 'smtp--user' }
  { kvName: 'Smtp--Password', envName: 'Smtp__Password', refName: 'smtp--password' }
]

var kvSecretRefs = [for s in kvSecrets: {
  name: s.refName
  keyVaultUrl: '${keyVaultUri}secrets/${s.kvName}'
  identity: managedIdentityId
}]

var staticEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'ASPNETCORE_URLS', value: 'http://+:${targetPort}' }
  { name: 'AzureKeyVault__Url', value: keyVaultUri }
  { name: 'ManagedIdentityClientId', value: managedIdentityClientId }
  { name: 'AZURE_CLIENT_ID', value: managedIdentityClientId }
  { name: 'ApplicationInsights__ConnectionString', value: appInsightsConnectionString }
  { name: 'SqlConnection__Default', value: sqlConnectionString }
  { name: 'Admin__Email', value: adminEmail }
]

var secretEnv = [for s in kvSecrets: {
  name: s.envName
  secretRef: s.refName
}]

var allEnv = concat(staticEnv, secretEnv)

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: [
        {
          server: registryServer
          identity: managedIdentityId
        }
      ]
      secrets: kvSecretRefs
    }
    template: {
      containers: [
        {
          name: 'app'
          image: containerImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: allEnv
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/health/live', port: targetPort }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: targetPort }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: targetPort }
              initialDelaySeconds: 10
              periodSeconds: 30
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output id string = app.id
output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
output latestRevisionName string = app.properties.latestRevisionName
