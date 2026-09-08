metadata description = 'Service Bus for integration events consumed out of process.'

param location string
param tags object
param name string

@allowed(['Standard', 'Premium'])
param sku string

@description('Principal granted send and receive. The API identity.')
param senderPrincipalId string

param logAnalyticsWorkspaceId string

// Azure Service Bus Data Owner: send and receive, but no ability to reconfigure the namespace.
var dataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

var topics = [
  'nexaops-incident-created'
  'nexaops-incident-assigned'
  'nexaops-incident-resolved'
  'nexaops-sla-breached'
  'nexaops-notification-requested'
]

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: sku
    tier: sku
    capacity: sku == 'Premium' ? 1 : null
  }
  properties: {
    minimumTlsVersion: '1.2'

    // No SAS keys. Publishers and subscribers authenticate with their own identity, so there is
    // no connection string to distribute or rotate.
    disableLocalAuth: true

    zoneRedundant: sku == 'Premium'
    publicNetworkAccess: 'Enabled'
  }
}

resource topicResources 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = [
  for topic in topics: {
    parent: namespace
    name: topic
    properties: {
      defaultMessageTimeToLive: 'P14D'
      enableBatchedOperations: true
      supportOrdering: false

      // Duplicate detection guards the at-least-once delivery a retrying publisher produces.
      requiresDuplicateDetection: true
      duplicateDetectionHistoryTimeWindow: 'PT10M'
    }
  }
]

resource dataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, senderPrincipalId, dataOwnerRoleId)
  scope: namespace
  properties: {
    principalId: senderPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', dataOwnerRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: namespace
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'OperationalLogs', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
output namespaceName string = namespace.name
