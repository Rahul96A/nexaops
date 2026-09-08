metadata description = 'Azure Container Registry holding the API image.'

param location string
param tags object
param name string

@description('Principal granted pull access. The Container App identity.')
param pullerPrincipalId string

param isProduction bool

// AcrPull. Pull only: a compromised runtime identity cannot push a tampered image.
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: isProduction ? 'Premium' : 'Basic'
  }
  properties: {
    // Admin user is a shared username and password baked into the registry. Off: the pipeline
    // and the runtime both authenticate with their own identity.
    adminUserEnabled: false

    publicNetworkAccess: 'Enabled'

    policies: isProduction ? {
      retentionPolicy: {
        status: 'enabled'
        days: 30
      }
    } : null
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullerPrincipalId, acrPullRoleId)
  scope: registry
  properties: {
    principalId: pullerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalType: 'ServicePrincipal'
  }
}

output loginServer string = registry.properties.loginServer
output name string = registry.name
