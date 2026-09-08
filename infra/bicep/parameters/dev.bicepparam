using '../main.bicep'

// Development. Serverless SQL that auto-pauses, no edge services, and AI on so the assistant
// can be exercised against a real provider.
param environment = 'dev'
param location = 'centralindia'
param workload = 'nexaops'

// Replace with the object id of the Entra group that administers SQL in this subscription.
// There is no SQL-authentication administrator, so an incorrect value here locks everyone out.
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'NexaOps SQL Administrators'

param deployAiServices = true
param deployEdgeServices = false
