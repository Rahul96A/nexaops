using '../main.bicep'

// Staging mirrors production closely enough to be a meaningful gate: zone-redundant SQL, the
// same AI deployments, and the same migration and smoke-test path.
param environment = 'stg'
param location = 'centralindia'
param workload = 'nexaops'

param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'NexaOps SQL Administrators'

param deployAiServices = true

// Edge services are the one deliberate difference: Front Door and API Management are expensive
// and are validated in production behind a manual approval.
param deployEdgeServices = false
