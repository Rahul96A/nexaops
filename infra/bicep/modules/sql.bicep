metadata description = 'Azure SQL Database with Entra-only authentication, auditing and threat detection.'

param location string
param tags object
param serverName string
param databaseName string

@description('Entra group that administers the server. There is no SQL-authentication administrator, so this is the only administrative path.')
param adminGroupObjectId string
param adminGroupName string

param sku object
param maxSizeBytes int
param zoneRedundant bool

@allowed(['Local', 'Zone', 'Geo'])
param backupStorageRedundancy string

param logAnalyticsWorkspaceId string

@description('Long-term backup retention. Production only; the cost is not justified elsewhere.')
param enableLongTermRetention bool

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'

    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: adminGroupName
      sid: adminGroupObjectId
      tenantId: subscription().tenantId

      // The decisive setting: SQL authentication is disabled outright. There is no password to
      // leak, rotate or find in a configuration file, and the application connects with its
      // Managed Identity.
      azureADOnlyAuthentication: true
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: sku
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: maxSizeBytes
    zoneRedundant: zoneRedundant
    requestedBackupStorageRedundancy: backupStorageRedundancy
    readScale: 'Disabled'

    // Auto-pause is a false economy above development: the first request after a pause pays a
    // cold start of tens of seconds, which a service desk agent experiences as a broken product.
    autoPauseDelay: startsWith(sku.name, 'GP_S_') ? 60 : null
  }
}

// Allows other Azure services, including the Container App, to reach the server. Private
// endpoints are the next step for a customer who requires no public network path at all.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource auditing 'Microsoft.Sql/servers/auditingSettings@2023-08-01-preview' = {
  parent: sqlServer
  name: 'default'
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
    auditActionsAndGroups: [
      'BATCH_COMPLETED_GROUP'
      'SUCCESSFUL_DATABASE_AUTHENTICATION_GROUP'
      'FAILED_DATABASE_AUTHENTICATION_GROUP'
    ]
  }
}

// Alerts on SQL injection attempts, anomalous access patterns and exfiltration signals. This is
// defence in depth: the application parameterises every query, and this catches what that misses.
resource threatProtection 'Microsoft.Sql/servers/advancedThreatProtectionSettings@2023-08-01-preview' = {
  parent: sqlServer
  name: 'Default'
  properties: {
    state: 'Enabled'
  }
}

resource longTermRetention 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-08-01-preview' = if (enableLongTermRetention) {
  parent: database
  name: 'default'
  properties: {
    weeklyRetention: 'P4W'
    monthlyRetention: 'P12M'
    yearlyRetention: 'P7Y'
    weekOfYear: 1
  }
}

resource shortTermRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01-preview' = {
  parent: database
  name: 'default'
  properties: {
    // Point-in-time restore window. 35 days is the maximum and costs little.
    retentionDays: enableLongTermRetention ? 35 : 7
    diffBackupIntervalInHours: 12
  }
}

resource databaseDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: database
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'SQLInsights', enabled: true }
      { category: 'Errors', enabled: true }
      { category: 'Timeouts', enabled: true }
      { category: 'Blocks', enabled: true }
      { category: 'Deadlocks', enabled: true }
      { category: 'QueryStoreRuntimeStatistics', enabled: true }
    ]
    metrics: [
      { category: 'Basic', enabled: true }
    ]
  }
}

// An Entra connection string: no user id, no password, nothing to protect.
output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output serverName string = sqlServer.name
output databaseName string = database.name
