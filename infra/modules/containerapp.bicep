// Container App for Academia Auditiva. Scales 1..N on HTTP.
// The app reads its secrets (Facebook, SMTP, Admin) straight from Key Vault
// at startup (AzureKeyVault__Url) with the user-assigned MI, through the
// vault's private endpoint. There are no Container Apps secret references,
// so optional secrets don't need placeholders in the vault.

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
param appVersion string = ''
param minReplicas int = 1
param maxReplicas int = 3
param targetPort int = 8080
param cpu string = '0.5'
param memory string = '1.0Gi'

@description('Bootstrap admin email for the application (Admin__Email).')
param adminEmail string = ''

@description('Storage account blob endpoint (e.g. https://staaprd...blob.core.windows.net/).')
param storageBlobEndpoint string = ''

@description('Blob URI of the ASP.NET Core Data Protection key ring (keys.xml).')
param dataProtectionBlobUri string = ''

@description('Versionless Key Vault key URI used to wrap the Data Protection key ring.')
param dataProtectionKeyUri string = ''

@description('Ingress custom domains: [{ name, bindingType: Disabled | SniEnabled, certificateId? }].')
param customDomains array = []

// SQL connection string built from outputs. AAD auth via the user-assigned MI,
// so it holds no secret. User Id=<MI clientId> is required for Active
// Directory Default to pick the right identity in a multi-MI host.
var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;User Id=${managedIdentityClientId}'

var envVars = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'ASPNETCORE_HTTP_PORTS', value: string(targetPort) }
  { name: 'AzureKeyVault__Url', value: keyVaultUri }
  { name: 'ManagedIdentityClientId', value: managedIdentityClientId }
  { name: 'AZURE_CLIENT_ID', value: managedIdentityClientId }
  { name: 'ApplicationInsights__ConnectionString', value: appInsightsConnectionString }
  { name: 'APP_VERSION', value: empty(appVersion) ? containerImage : appVersion }
  { name: 'ConnectionStrings__DefaultConnection', value: sqlConnectionString }
  { name: 'Admin__Email', value: adminEmail }
  { name: 'Storage__BlobEndpoint', value: storageBlobEndpoint }
  { name: 'DataProtection__BlobUri', value: dataProtectionBlobUri }
  { name: 'DataProtection__KeyIdentifier', value: dataProtectionKeyUri }
]

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
        // Keep affinity even though round state is now backed by SQL Server;
        // it reduces audio-cache churn and remains compatible with
        // activeRevisionsMode 'Single'.
        stickySessions: {
          affinity: 'sticky'
        }
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        customDomains: customDomains
      }
      registries: [
        {
          server: registryServer
          identity: managedIdentityId
        }
      ]
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
          env: envVars
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
