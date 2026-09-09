using '../main.bicep'

// Development. Serverless SQL that auto-pauses, no edge services, no cache, and AI off.
//
// AI is off because Azure OpenAI needs model quota that a new subscription does not have, and
// Azure AI Search Basic is the single largest line item in this environment. With it off the
// assistant and the virtual agent report themselves unconfigured, which is their designed
// behaviour -- they never fabricate an answer. Turn it on once quota is granted.
param environment = 'dev'
param location = 'centralindia'
param workload = 'nexaops'

// Replace with the object id of the Entra group that administers SQL in this subscription.
// There is no SQL-authentication administrator, so an incorrect value here locks everyone out.
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'NexaOps SQL Administrators'

param deployAiServices = false
param deployEdgeServices = false
