metadata description = 'Container Apps environment and the NexaOps API application.'

param location string
param tags object
param environmentName string
param appName string

@description('Resource id of the user-assigned identity the app runs as.')
param identityId string

@description('Client id of that identity. DefaultAzureCredential needs it to pick the right one when several are attached.')
param identityClientId string

param registryLoginServer string
param logAnalyticsCustomerId string

@secure()
param logAnalyticsSharedKey string

@secure()
param appInsightsConnectionString string

param keyVaultUri string

@secure()
param sqlConnectionString string

param blobServiceUri string
param serviceBusNamespace string
param openAiEndpoint string
param searchEndpoint string

param minReplicas int
param maxReplicas int
@description('CPU cores, as a string because Bicep cannot express a fractional literal directly. Converted with json() at the point of use.')
param cpu string
param memory string
param environmentLabel string
param zoneRedundant bool

@description('''
Full image reference to run, overriding the registry and tag.

Exists to break a bootstrap deadlock: the container app cannot be created pointing at an image
that does not exist, and the image cannot be pushed until the registry it goes to has been
created. Provisioning therefore starts on a public placeholder, and the deploy pipeline replaces
it with the real image on the first release.

Leave empty in the pipeline, which supplies the registry and tag instead.
''')
param containerImage string = ''

// Empty means nothing has been published to the registry yet, so provisioning starts on
// Microsoft's own Container Apps sample: public, tiny, and obviously not this product, so an
// environment left sitting on it is recognisable at a glance rather than looking like a broken
// NexaOps.
var effectiveImage = empty(containerImage)
  ? 'mcr.microsoft.com/k8se/quickstart:latest'
  : containerImage

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    zoneRedundant: zoneRedundant
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  tags: tags

  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }

  properties: {
    managedEnvironmentId: environment.id

    configuration: {
      activeRevisionsMode: 'Single'

      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false

        // The client address arrives in X-Forwarded-For. Without this the API would record the
        // ingress address for every request, making rate limiting and audit useless.
        clientCertificateMode: 'ignore'

        corsPolicy: {
          allowedOrigins: ['*']
          allowedMethods: ['GET', 'POST', 'PATCH', 'PUT', 'DELETE', 'OPTIONS']
          allowedHeaders: ['*']
          maxAge: 3600
        }
      }

      registries: [
        {
          server: registryLoginServer
          identity: identityId
        }
      ]

      // Secrets are held by the platform and surfaced to the process as environment variables.
      // None of them appears in the image, the repository, or a pipeline log.
      secrets: [
        { name: 'sql-connection-string', value: sqlConnectionString }
        { name: 'appinsights-connection-string', value: appInsightsConnectionString }
      ]
    }

    template: {
      containers: [
        {
          name: 'api'
          image: effectiveImage

          resources: {
            cpu: json(cpu)
            memory: memory
          }

          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }

            // Tells DefaultAzureCredential which user-assigned identity to use.
            { name: 'AZURE_CLIENT_ID', value: identityClientId }

            { name: 'ConnectionStrings__NexaOpsDb', secretRef: 'sql-connection-string' }
            { name: 'ApplicationInsights__ConnectionString', secretRef: 'appinsights-connection-string' }

            // Key Vault is layered over configuration at startup, so a setting can be replaced
            // by a vault reference without a redeploy.
            { name: 'Azure__KeyVaultUri', value: keyVaultUri }

            { name: 'Auth__Mode', value: 'Local' }

            // The JWT signing key is resolved from Key Vault by the configuration provider.
            // It is deliberately absent here.
            { name: 'Storage__BlobServiceUri', value: blobServiceUri }
            { name: 'Messaging__FullyQualifiedNamespace', value: serviceBusNamespace }

            { name: 'AzureAi__Endpoint', value: openAiEndpoint }
            { name: 'AzureAi__ChatDeployment', value: empty(openAiEndpoint) ? '' : 'gpt-4o' }
            { name: 'AzureAi__EmbeddingDeployment', value: empty(openAiEndpoint) ? '' : 'text-embedding-3-large' }
            { name: 'AzureAi__UseManagedIdentity', value: 'true' }
            { name: 'AzureAi__SearchEndpoint', value: searchEndpoint }

            // Migrations run as an explicit pipeline step in every deployed environment, so a
            // restarting replica never alters the schema.
            { name: 'Database__MigrateOnStartup', value: 'false' }
            { name: 'Demo__SeedOnStartup', value: 'false' }

            { name: 'Environment__Label', value: environmentLabel }
          ]

          // Every probe sets timeoutSeconds explicitly. The platform default is one second,
          // which is far too short for the readiness check: it opens a SQL connection that has
          // to acquire an Entra token first, and on a cold start that alone exceeds a second.
          // Left at the default, readiness fails with "A task was canceled" at 999ms every
          // time and the revision never becomes ready — a failure that looks like a broken
          // database and is really a broken probe.
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              // Readiness checks the database, so a replica that cannot reach SQL is taken out
              // of rotation rather than serving failures.
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 15
              timeoutSeconds: 15
              failureThreshold: 3
            }
            {
              // Liveness only, so startup is not gated on the database being reachable. A
              // replica that is up but cannot see SQL should report itself unready, not be
              // killed and restarted in a loop.
              type: 'Startup'
              httpGet: { path: '/health/live', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 5
              timeoutSeconds: 5
              failureThreshold: 30
            }
          ]
        }
      ]

      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output fqdn string = api.properties.configuration.ingress.fqdn
output name string = api.name
output environmentId string = environment.id
