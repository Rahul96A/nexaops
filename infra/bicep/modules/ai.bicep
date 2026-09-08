metadata description = 'Azure OpenAI and Azure AI Search backing the NexaOps assistant and RAG.'

param location string
param tags object
param openAiName string
param searchName string

@description('Principal granted inference and query access. The API identity.')
param consumerPrincipalId string

param logAnalyticsWorkspaceId string
param isProduction bool

// Cognitive Services OpenAI User: call the deployed models, but not create or delete them.
var openAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

// Search Index Data Contributor and Search Service Contributor: read and write documents, and
// manage indexes, which the ingestion pipeline needs in order to create them.
var searchIndexDataContributorRoleId = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
var searchServiceContributorRoleId = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: 'Enabled'

    // API keys are disabled. The application authenticates with its Managed Identity, which is
    // why no AI key exists in configuration and none can reach the browser.
    disableLocalAuth: true

    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'gpt-4o'
  sku: {
    name: 'GlobalStandard'
    capacity: isProduction ? 50 : 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
    versionUpgradeOption: 'OnceCurrentVersionExpired'

    // Azure content filtering stays on. It is one layer among several, not the security model:
    // the tool registry is what actually constrains what the assistant can reach.
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'text-embedding-3-large'
  sku: {
    name: 'Standard'
    capacity: isProduction ? 50 : 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }

  // Deployments on one account are created serially.
  dependsOn: [chatDeployment]
}

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: searchName
  location: location
  tags: tags
  sku: {
    name: isProduction ? 'standard' : 'basic'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    replicaCount: isProduction ? 2 : 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    semanticSearch: 'standard'

    // Admin and query keys are disabled for the same reason as everywhere else: a key that does
    // not exist cannot be committed, logged, or shipped to a browser.
    disableLocalAuth: true
  }
}

resource openAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, consumerPrincipalId, openAiUserRoleId)
  scope: openAi
  properties: {
    principalId: consumerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', openAiUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource searchDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, consumerPrincipalId, searchIndexDataContributorRoleId)
  scope: search
  properties: {
    principalId: consumerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource searchServiceContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, consumerPrincipalId, searchServiceContributorRoleId)
  scope: search
  properties: {
    principalId: consumerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource openAiDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: openAi
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      // Every request and its token usage, which is what makes AI spend attributable per tenant.
      { category: 'RequestResponse', enabled: true }
      { category: 'Audit', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output openAiEndpoint string = openAi.properties.endpoint
output openAiName string = openAi.name
output chatDeploymentName string = chatDeployment.name
output embeddingDeploymentName string = embeddingDeployment.name
output searchEndpoint string = 'https://${search.name}.search.windows.net'
output searchName string = search.name
