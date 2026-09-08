metadata description = 'Blob storage for attachments and knowledge documents.'

param location string
param tags object
param name string

@description('Principal granted read and write access to blobs. The API identity.')
param writerPrincipalId string

param logAnalyticsWorkspaceId string
param isProduction bool

// Storage Blob Data Contributor. Data-plane only: the identity can read and write blobs but
// cannot reconfigure the account, rotate keys, or change network rules.
var blobContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: isProduction ? 'Standard_ZRS' : 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false

    // Shared keys are disabled outright. With them off, the only way in is Entra, so a leaked
    // key cannot exist - and the user delegation SAS the API issues for downloads is derived
    // from the caller's own identity rather than from an account secret.
    allowSharedKeyAccess: false

    allowCrossTenantReplication: false
    publicNetworkAccess: 'Enabled'

    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }

    encryption: {
      services: {
        blob: { enabled: true, keyType: 'Account' }
        file: { enabled: true, keyType: 'Account' }
      }
      keySource: 'Microsoft.Storage'
      requireInfrastructureEncryption: isProduction
    }
  }
}

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    // Versioning and soft delete together mean an attachment deleted in error, or overwritten,
    // is recoverable without going to a backup.
    isVersioningEnabled: true

    deleteRetentionPolicy: {
      enabled: true
      days: isProduction ? 90 : 14
    }

    containerDeleteRetentionPolicy: {
      enabled: true
      days: isProduction ? 90 : 14
    }

    changeFeed: {
      enabled: isProduction
    }
  }
}

resource attachmentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: 'attachments'
  properties: {
    publicAccess: 'None'
  }
}

resource knowledgeContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: 'knowledge'
  properties: {
    publicAccess: 'None'
  }
}

resource exportsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: 'exports'
  properties: {
    publicAccess: 'None'
  }
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, writerPrincipalId, blobContributorRoleId)
  scope: storageAccount
  properties: {
    principalId: writerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource blobDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: blobServices
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'StorageRead', enabled: true }
      { category: 'StorageWrite', enabled: true }
      { category: 'StorageDelete', enabled: true }
    ]
  }
}

output blobServiceUri string = storageAccount.properties.primaryEndpoints.blob
output accountName string = storageAccount.name
