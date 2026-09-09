metadata description = 'Azure Managed Redis. The distributed cache behind IApplicationCache.'

param location string
param tags object
param name string

@description('Managed Redis SKU, e.g. Balanced_B0. Azure Cache for Redis SKUs do not apply here.')
param skuName string

param keyVaultName string
param logAnalyticsWorkspaceId string

// Azure Managed Redis, not Azure Cache for Redis.
//
// Microsoft.Cache/redis is retired: new instances cannot be created at all, and an attempt
// returns "Azure Cache for Redis is retiring, create Azure Managed Redis instance instead".
// The replacement is a different resource type with different SKUs and a different port, so
// this is a migration rather than a version bump.
resource redis 'Microsoft.Cache/redisEnterprise@2024-10-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: skuName
  }
}

resource database 'Microsoft.Cache/redisEnterprise/databases@2024-10-01' = {
  parent: redis
  name: 'default'
  properties: {
    // TLS only. The unencrypted protocol exists for latency-sensitive workloads inside a trusted
    // network, which is not what this is.
    clientProtocol: 'Encrypted'
    port: 10000

    // Evict the least recently used key when memory runs out. A cache that refuses writes when
    // full turns a capacity problem into an availability problem.
    evictionPolicy: 'AllKeysLRU'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// The secret name is load-bearing, not cosmetic.
//
// The API layers Key Vault over configuration, and that provider maps '--' in a secret name to
// ':' in a configuration key. The cache is read with GetConnectionString("Redis"), which is the
// key ConnectionStrings:Redis — so the secret has to be ConnectionStrings--Redis and nothing
// else.
//
// It was previously called Redis-ConnectionString, which maps to a key nothing reads. The cache
// was provisioned, billed for, and silently unused: the application fell back to its in-memory
// cache and gave no indication anything was wrong.
resource connectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ConnectionStrings--Redis'
  properties: {
    value: '${redis.properties.hostName}:10000,password=${database.listKeys().primaryKey},ssl=True,abortConnect=False'
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
