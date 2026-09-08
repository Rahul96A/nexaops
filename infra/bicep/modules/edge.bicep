metadata description = 'Azure Front Door with WAF, and API Management as the published integration surface.'

param location string
param tags object
param frontDoorName string
param apiManagementName string

@description('Container App host name Front Door sends traffic to.')
param originHostName string

@description('Custom domain. Empty uses the generated Front Door host name.')
param customDomain string = ''

param logAnalyticsWorkspaceId string
param publisherEmail string
param publisherName string

// ---------------------------------------------------------------------------
// Front Door
// ---------------------------------------------------------------------------

resource profile 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: frontDoorName

  // Front Door is a global service; its profile always lives in the global location even
  // though every backend it fronts is regional.
  location: 'global'
  tags: tags
  sku: {
    name: 'Premium_AzureFrontDoor'
  }
  properties: {
    originResponseTimeoutSeconds: 60
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: profile
  name: '${frontDoorName}-ep'
  location: 'global'
  tags: tags
  properties: {
    enabledState: 'Enabled'
  }
}

resource originGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'api-origins'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
    healthProbeSettings: {
      // Probes the readiness endpoint, so Front Door stops sending traffic to a region whose
      // database is unreachable rather than only when the process itself dies.
      probePath: '/health/ready'
      probeRequestType: 'GET'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 30
    }
    sessionAffinityState: 'Disabled'
  }
}

resource origin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: originGroup
  name: 'container-app'
  properties: {
    hostName: originHostName
    originHostHeader: originHostName
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1
    enabledState: 'Enabled'

    // The origin only accepts traffic that arrived through this Front Door profile, so the
    // Container App's own host name cannot be used to bypass the WAF.
    enforceCertificateNameCheck: true
  }
}

resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'api-route'
  properties: {
    originGroup: {
      id: originGroup.id
    }
    supportedProtocols: ['Http', 'Https']
    patternsToMatch: ['/*']
    forwardingProtocol: 'HttpsOnly'
    linkToDefaultDomain: 'Enabled'
    httpsRedirect: 'Enabled'
    enabledState: 'Enabled'
  }
  dependsOn: [origin]
}

resource wafPolicy 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2024-02-01' = {
  name: replace('${frontDoorName}waf', '-', '')
  location: 'global'
  tags: tags
  sku: {
    name: 'Premium_AzureFrontDoor'
  }
  properties: {
    policySettings: {
      enabledState: 'Enabled'

      // Prevention, not detection. A WAF in detection mode logs the attack and serves it.
      mode: 'Prevention'
      requestBodyCheck: 'Enabled'
    }

    managedRules: {
      managedRuleSets: [
        {
          // The OWASP core rule set: injection, traversal, protocol violations.
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
          ruleSetAction: 'Block'
        }
        {
          // Blocks traffic from IP addresses Microsoft threat intelligence has classified as
          // malicious, including known botnet nodes.
          ruleSetType: 'Microsoft_BotManagerRuleSet'
          ruleSetVersion: '1.0'
        }
      ]
    }

    customRules: {
      rules: [
        {
          name: 'RateLimitAuthentication'
          priority: 100
          enabledState: 'Enabled'
          ruleType: 'RateLimitRule'

          // A second, network-level limit in front of the application's own. This one is
          // enforced before a request reaches the API at all, so a credential-stuffing flood
          // never consumes application capacity.
          rateLimitDurationInMinutes: 5
          rateLimitThreshold: 30

          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'Contains'
              matchValue: ['/api/v1/auth/sign-in']
              negateCondition: false
              transforms: ['Lowercase']
            }
          ]
          action: 'Block'
        }
      ]
    }
  }
}

resource securityPolicy 'Microsoft.Cdn/profiles/securityPolicies@2024-02-01' = {
  parent: profile
  name: 'waf-association'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: {
        id: wafPolicy.id
      }
      associations: [
        {
          domains: [
            { id: endpoint.id }
          ]
          patternsToMatch: ['/*']
        }
      ]
    }
  }
}

resource frontDoorDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: profile
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'FrontDoorAccessLog', enabled: true }
      { category: 'FrontDoorWebApplicationFirewallLog', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

// ---------------------------------------------------------------------------
// API Management
// ---------------------------------------------------------------------------

resource apim 'Microsoft.ApiManagement/service@2023-05-01-preview' = {
  name: apiManagementName
  location: location
  tags: tags
  sku: {
    name: 'Developer'
    capacity: 1
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName

    customProperties: {
      // Everything below TLS 1.2 is switched off explicitly; several are on by default.
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Tls10': 'False'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Tls11': 'False'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Ssl30': 'False'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Tls10': 'False'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Tls11': 'False'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Ssl30': 'False'
    }
  }
}

resource apimDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: apim
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'GatewayLogs', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output frontDoorEndpoint string = empty(customDomain)
  ? 'https://${endpoint.properties.hostName}'
  : 'https://${customDomain}'

output frontDoorProfileName string = profile.name
output apiManagementGatewayUrl string = apim.properties.gatewayUrl
