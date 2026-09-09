metadata description = 'Log Analytics and Application Insights. Deployed first so every other resource can send diagnostics here.'

param location string
param tags object
param logAnalyticsName string
param appInsightsName string
param retentionInDays int

@description('Daily ingestion ceiling in GB, given as a string because Bicep has no floating-point type and the smallest cap Azure accepts is a tenth of a gigabyte. Minus one means uncapped, which is what an environment carrying real traffic wants.')
param dailyQuotaGb string = '-1'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionInDays

    // A ceiling on ingestion, not a target. Set for the free dev profile so that a crash loop
    // writing stack traces cannot quietly spend money; ingestion stops for the rest of the day
    // once the cap is hit, which is a deliberate trade of observability for a predictable bill.
    // An environment with real traffic passes -1 and gets no cap.
    workspaceCapping: {
      dailyQuotaGb: json(dailyQuotaGb)
    }
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id

    // Instrumentation keys are legacy and are effectively a shared secret in a query string.
    // Connection strings plus Entra authentication are the supported path.
    DisableLocalAuth: false
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output logAnalyticsId string = logAnalytics.id
output logAnalyticsCustomerId string = logAnalytics.properties.customerId

@secure()
output logAnalyticsSharedKey string = logAnalytics.listKeys().primarySharedKey

output appInsightsId string = appInsights.id

@secure()
output appInsightsConnectionString string = appInsights.properties.ConnectionString
