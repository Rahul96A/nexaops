metadata description = 'Key Vault holding the few secrets that cannot be replaced by Managed Identity.'

param location string
param tags object
param name string
param logAnalyticsWorkspaceId string

@description('Principal granted read access to secrets. The API identity, and nothing else.')
param readerPrincipalId string

@description('Purge protection makes a deleted vault unrecoverable-proof for 90 days. On in the environments where losing a secret would be an incident.')
param enablePurgeProtection bool

// Key Vault Secrets User. Read only: the application reads secrets and never writes them.
var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId

    // Entra authorisation rather than vault access policies: it is auditable through Azure RBAC
    // and does not require editing the vault to change who can read a secret.
    enableRbacAuthorization: true

    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: enablePurgeProtection ? true : null

    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, readerPrincipalId, secretsUserRoleId)
  scope: vault
  properties: {
    principalId: readerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: vault
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        // Every secret read is recorded. This is the audit trail for credential access.
        category: 'AuditEvent'
        enabled: true
      }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
output id string = vault.id
