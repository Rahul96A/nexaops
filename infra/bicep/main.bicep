metadata description = 'NexaOps platform infrastructure for one environment.'

targetScope = 'resourceGroup'

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

@description('Environment name. Drives sizing, redundancy and naming.')
@allowed(['dev', 'test', 'stg', 'prod'])
param environment string

@description('Azure region. Defaults to Central India for data residency.')
param location string = 'centralindia'

@description('Short workload name used to build resource names.')
@minLength(3)
@maxLength(10)
param workload string = 'nexaops'

@description('Entra ID object id of the group that administers SQL. Required: SQL has no SQL-authentication administrator, so without this nobody can administer the database.')
param sqlAdminGroupObjectId string

@description('Display name of that Entra group.')
param sqlAdminGroupName string

@description('Deploy Azure OpenAI and Azure AI Search. Turn off where the subscription has no AI quota.')
param deployAiServices bool = true

@description('''
Deploy a distributed cache.

Off below production on purpose. IApplicationCache falls back to an in-process cache when no
connection string is present, which is correct for a single-replica environment and saves the
cheapest Managed Redis tier's standing charge. Turn it on wherever more than one replica runs,
because two replicas with private caches will disagree.
''')
param deployCache bool = environment == 'prod'

@description('''
Deploy Service Bus.

Off below production. IEventPublisher reports itself unconfigured without it and the product
works exactly as before -- nothing in NexaOps consumes its own events yet; the namespace exists
for out-of-process consumers a customer adds later. A Standard namespace carries a standing
monthly charge for a queue nobody is reading.
''')
param deployMessaging bool = environment == 'prod'

@description('''
Use the Azure SQL free offer where the sizing allows it.

Caps the environment at no cost: the database pauses when the monthly allowance is used up
rather than billing for the overage. One free database per subscription, so only one environment
can take it.
''')
param useSqlFreeLimit bool = false

@description('''
Deploy an Azure Container Registry.

ACR has no free tier -- Basic carries a standing monthly charge -- so a free environment pulls a
public image from GitHub Container Registry instead and needs no registry of its own.
''')
param deployRegistry bool = true

@description('Deploy Front Door and API Management. Usually production only.')
param deployEdgeServices bool = environment == 'prod'

@description('Custom domain served by Front Door. Empty uses the default endpoint host name.')
param customDomain string = ''

@description('''
Full image reference for the API, e.g. myregistry.azurecr.io/nexaops-api:1.0.0.

Empty provisions on a public placeholder so an environment can be created before anything has
been built -- the registry has to exist before an image can be pushed to it. The deploy pipeline
supplies the real reference on every release.
''')
param containerImage string = ''

@description('Tags applied to every resource.')
param tags object = {
  workload: workload
  environment: environment
  managedBy: 'bicep'
  dataClassification: 'customer'
}

// ---------------------------------------------------------------------------
// Naming
// ---------------------------------------------------------------------------
// A short hash of the resource group id keeps globally unique names (storage, ACR, Key Vault)
// from colliding across subscriptions without hard-coding anything environment specific.

var suffix = substring(uniqueString(resourceGroup().id), 0, 6)
var prefix = '${workload}-${environment}'

var names = {
  logAnalytics: 'log-${prefix}'
  appInsights: 'appi-${prefix}'
  keyVault: 'kv-${workload}${environment}${suffix}'
  storage: 'st${workload}${environment}${suffix}'
  sqlServer: 'sql-${prefix}-${suffix}'
  sqlDatabase: 'sqldb-${workload}'
  serviceBus: 'sb-${prefix}-${suffix}'
  redis: 'redis-${prefix}-${suffix}'
  containerRegistry: 'cr${workload}${environment}${suffix}'
  containerAppsEnvironment: 'cae-${prefix}'
  apiContainerApp: 'ca-${prefix}-api'
  managedIdentity: 'id-${prefix}-api'
  openAi: 'aoai-${prefix}-${suffix}'
  aiSearch: 'srch-${prefix}-${suffix}'
  frontDoor: 'afd-${prefix}'
  apiManagement: 'apim-${prefix}-${suffix}'
}

// Sizing per environment. Production is the only tier with zone redundancy and geo backups,
// because the others do not justify the cost and their loss is an inconvenience, not an outage.
var sizing = {
  dev: {
    sqlSku: { name: 'GP_S_Gen5_1', tier: 'GeneralPurpose', capacity: 1 }
    sqlMaxSizeBytes: 34359738368
    sqlZoneRedundant: false
    sqlBackupStorage: 'Local'
    redisSku: 'Balanced_B0'
    serviceBusSku: 'Standard'
    apiMinReplicas: 0
    apiMaxReplicas: 2
    apiCpu: '0.5'
    apiMemory: '1Gi'
    logRetentionDays: 30
  }
  test: {
    sqlSku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose', capacity: 2 }
    sqlMaxSizeBytes: 68719476736
    sqlZoneRedundant: false
    sqlBackupStorage: 'Local'
    redisSku: 'Balanced_B0'
    serviceBusSku: 'Standard'
    apiMinReplicas: 1
    apiMaxReplicas: 3
    apiCpu: '0.5'
    apiMemory: '1Gi'
    logRetentionDays: 30
  }
  stg: {
    sqlSku: { name: 'GP_Gen5_2', tier: 'GeneralPurpose', capacity: 2 }
    sqlMaxSizeBytes: 137438953472
    sqlZoneRedundant: true
    sqlBackupStorage: 'Zone'
    redisSku: 'Balanced_B1'
    serviceBusSku: 'Standard'
    apiMinReplicas: 1
    apiMaxReplicas: 5
    apiCpu: '1.0'
    apiMemory: '2Gi'
    logRetentionDays: 60
  }
  prod: {
    sqlSku: { name: 'BC_Gen5_4', tier: 'BusinessCritical', capacity: 4 }
    sqlMaxSizeBytes: 274877906944
    sqlZoneRedundant: true
    sqlBackupStorage: 'Geo'
    redisSku: 'Balanced_B3'
    serviceBusSku: 'Premium'
    apiMinReplicas: 2
    apiMaxReplicas: 20
    apiCpu: '2.0'
    apiMemory: '4Gi'
    logRetentionDays: 365
  }
}

var size = sizing[environment]

// ---------------------------------------------------------------------------
// Observability. Deployed first: everything else sends diagnostics here.
// ---------------------------------------------------------------------------

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    location: location
    tags: tags
    logAnalyticsName: names.logAnalytics
    appInsightsName: names.appInsights
    retentionInDays: size.logRetentionDays
  }
}

// ---------------------------------------------------------------------------
// Identity. One user-assigned identity is the API's single security principal:
// it authenticates to SQL, Key Vault, Storage, Service Bus and Azure OpenAI.
// No connection string, key or client secret exists anywhere in the deployment.
// ---------------------------------------------------------------------------

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.managedIdentity
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Data and platform services
// ---------------------------------------------------------------------------

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    tags: tags
    name: names.keyVault
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsId
    readerPrincipalId: apiIdentity.properties.principalId
    enablePurgeProtection: environment == 'prod' || environment == 'stg'
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    tags: tags
    serverName: names.sqlServer
    databaseName: names.sqlDatabase
    adminGroupObjectId: sqlAdminGroupObjectId
    adminGroupName: sqlAdminGroupName
    sku: size.sqlSku
    maxSizeBytes: size.sqlMaxSizeBytes
    zoneRedundant: size.sqlZoneRedundant
    backupStorageRedundancy: size.sqlBackupStorage
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsId
    enableLongTermRetention: environment == 'prod'
    useFreeLimit: useSqlFreeLimit
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    tags: tags
    name: names.storage
    writerPrincipalId: apiIdentity.properties.principalId
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsId
    isProduction: environment == 'prod'
  }
}

module serviceBus 'modules/servicebus.bicep' = if (deployMessaging) {
  name: 'servicebus'
  params: {
    location: location
    tags: tags
    name: names.serviceBus
    sku: size.serviceBusSku
    senderPrincipalId: apiIdentity.properties.principalId
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsId
  }
}

module redis 'modules/redis.bicep' = if (deployCache) {
  name: 'redis'
  params: {
    location: location
    tags: tags
    name: names.redis
    skuName: size.redisSku
    keyVaultName: keyVault.outputs.name
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsId
  }
}

module ai 'modules/ai.bicep' = if (deployAiServices) {
  name: 'ai'
  params: {
    location: location
    tags: tags
    openAiName: names.openAi
    searchName: names.aiSearch
    consumerPrincipalId: apiIdentity.properties.principalId
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsId
    isProduction: environment == 'prod'
  }
}

// ---------------------------------------------------------------------------
// Compute
// ---------------------------------------------------------------------------

module registry 'modules/registry.bicep' = if (deployRegistry) {
  name: 'registry'
  params: {
    location: location
    tags: tags
    name: names.containerRegistry
    pullerPrincipalId: apiIdentity.properties.principalId
    isProduction: environment == 'prod'
  }
}

module containerApp 'modules/containerapp.bicep' = {
  name: 'containerapp'
  params: {
    location: location
    tags: tags
    environmentName: names.containerAppsEnvironment
    appName: names.apiContainerApp
    identityId: apiIdentity.id
    identityClientId: apiIdentity.properties.clientId
    registryLoginServer: deployRegistry ? registry!.outputs.loginServer : ''
    containerImage: containerImage
    logAnalyticsCustomerId: observability.outputs.logAnalyticsCustomerId
    logAnalyticsSharedKey: observability.outputs.logAnalyticsSharedKey
    appInsightsConnectionString: observability.outputs.appInsightsConnectionString
    keyVaultUri: keyVault.outputs.uri
    sqlConnectionString: sql.outputs.connectionString
    blobServiceUri: storage.outputs.blobServiceUri
    // Safe dereference: a conditional module is null until it is deployed, and an empty
    // namespace is exactly how the publisher reports itself unconfigured.
    serviceBusNamespace: deployMessaging ? serviceBus!.outputs.fullyQualifiedNamespace : ''
    // Safe dereference: a conditional module is null until it is actually deployed, so a
    // plain property access here would fail template validation before anything is created.
    openAiEndpoint: ai.?outputs.openAiEndpoint ?? ''
    searchEndpoint: ai.?outputs.searchEndpoint ?? ''
    minReplicas: size.apiMinReplicas
    maxReplicas: size.apiMaxReplicas
    cpu: size.apiCpu
    memory: size.apiMemory
    environmentLabel: environment
    zoneRedundant: environment == 'prod' || environment == 'stg'
  }
}

// ---------------------------------------------------------------------------
// Edge. Front Door terminates TLS, absorbs volumetric attack traffic and applies
// the WAF; API Management is the published product surface for integrations.
// ---------------------------------------------------------------------------

module edge 'modules/edge.bicep' = if (deployEdgeServices) {
  name: 'edge'
  params: {
    tags: tags
    location: location
    frontDoorName: names.frontDoor
    apiManagementName: names.apiManagement
    originHostName: containerApp.outputs.fqdn
    customDomain: customDomain
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsId
    publisherEmail: 'platform@nexaops.local'
    publisherName: 'NexaOps Platform'
  }
}

// ---------------------------------------------------------------------------
// Outputs. Consumed by the deployment pipeline.
// ---------------------------------------------------------------------------

output apiUrl string = 'https://${containerApp.outputs.fqdn}'
output publicUrl string = edge.?outputs.frontDoorEndpoint ?? 'https://${containerApp.outputs.fqdn}'
// Empty when the environment pulls a public image and has no registry of its own.
output containerRegistryLoginServer string = deployRegistry ? registry!.outputs.loginServer : ''
output containerAppName string = names.apiContainerApp
output managedIdentityClientId string = apiIdentity.properties.clientId
output managedIdentityPrincipalId string = apiIdentity.properties.principalId
output keyVaultName string = keyVault.outputs.name
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = names.sqlDatabase

// The Application Insights connection string is deliberately NOT an output. It is a credential,
// deployment outputs are readable by anyone with reader access to the resource group, and the
// Container App already receives it directly as a platform secret.
