// User-assigned Managed Identity used by the Container App.
// Grants are made by other modules (AcrPull, KV Secrets User, SQL via script).

param name string
param location string
param tags object

resource mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
  tags: tags
}

output id string = mi.id
output principalId string = mi.properties.principalId
output clientId string = mi.properties.clientId
output name string = mi.name
