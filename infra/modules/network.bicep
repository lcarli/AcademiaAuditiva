// Virtual network for the Container Apps environment and the private
// endpoints of Key Vault, SQL and Blob storage, plus the private DNS zones
// that make their public host names resolve to the private endpoints from
// inside the VNet. Key Vault, SQL and Storage keep public network access
// disabled; the app reaches them only through this network.

param name string
param location string
param tags object

@description('VNet address space. The Container Apps subnet takes the first /23, the private endpoints subnet the third /24.')
param addressPrefix string = '10.60.0.0/16'

var containerAppsSubnetName = 'snet-cae'
var privateEndpointsSubnetName = 'snet-pe'

var keyVaultDnsZoneName = 'privatelink.vaultcore.azure.net'
var sqlDnsZoneName = 'privatelink${environment().suffixes.sqlServerHostname}'
var blobDnsZoneName = 'privatelink.blob.${environment().suffixes.storage}'
var dnsZoneNames = [
  keyVaultDnsZoneName
  sqlDnsZoneName
  blobDnsZoneName
]

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [ addressPrefix ]
    }
    subnets: [
      {
        // Workload profiles environments need a subnet delegated to
        // Microsoft.App/environments (/27 minimum).
        name: containerAppsSubnetName
        properties: {
          addressPrefix: cidrSubnet(addressPrefix, 23, 0)
          delegations: [
            {
              name: 'Microsoft.App.environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: privateEndpointsSubnetName
        properties: {
          addressPrefix: cidrSubnet(addressPrefix, 24, 2)
        }
      }
    ]
  }
}

resource dnsZones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for zone in dnsZoneNames: {
  name: zone
  location: 'global'
  tags: tags
}]

resource dnsZoneLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (zone, i) in dnsZoneNames: {
  parent: dnsZones[i]
  name: 'link-${name}'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}]

output id string = vnet.id
output containerAppsSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, containerAppsSubnetName)
output privateEndpointsSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, privateEndpointsSubnetName)
output keyVaultDnsZoneId string = dnsZones[0].id
output sqlDnsZoneId string = dnsZones[1].id
output blobDnsZoneId string = dnsZones[2].id
