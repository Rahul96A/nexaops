metadata description = 'Azure Cache for Redis. The distributed cache behind IApplicationCache.'

param location string
param tags object
param name string
param sku object
param keyVaultName string
param logAnalyticsWorkspaceId string

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: sku
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    redisConfiguration: {
      // Evict the least recently used key when memory runs out. A cache that refuses writes
      // when full turns a capacity problem into an availability problem.
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Redis has no Entra data-plane authentication on the Basic and Standard tiers, so the access
// key is unavoidable. It goes straight into Key Vault and is referenced from there; it is never
// written into an app setting, a pipeline variable, or the repository.
resource connectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Redis-ConnectionString'
  properties: {
    value: '${redis.properties.hostName}:${redis.properties.sslPort},password=${redis.listKeys().primaryKey},ssl=True,abortConnect=False'
    contentType: 'text/plain'
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: redis
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output hostName string = redis.properties.hostName
output secretName string = connectionSecret.name
output secretUri string = connectionSecret.properties.secretUri
