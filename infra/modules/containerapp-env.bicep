// Container Apps Managed Environment with Log Analytics integration.
// Runs in its own VNet subnet so apps reach Key Vault, SQL and Blob through
// private endpoints; ingress stays public (internal: false).
// The VNet can only be set when the environment is created: moving an
// existing environment into a VNet means deleting and recreating it.

param name string
param location string
param tags object
param logAnalyticsCustomerId string
@secure()
param logAnalyticsSharedKey string

@description('Subnet delegated to Microsoft.App/environments.')
param infrastructureSubnetId string

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnetId
      internal: false
    }
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

output id string = env.id
output name string = env.name
output defaultDomain string = env.properties.defaultDomain

@description('Public IP of the ingress: the apex domain A record points here.')
output staticIp string = env.properties.staticIp

@description('Value of the asuid.<host> TXT records that prove custom domain ownership.')
output customDomainVerificationId string = env.properties.customDomainConfiguration.customDomainVerificationId
