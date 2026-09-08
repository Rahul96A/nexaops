using '../main.bicep'

// Automated test environment. AI is off: the suite asserts the not-configured behaviour, and
// paying for inference to run tests would be waste.
param environment = 'test'
param location = 'centralindia'
param workload = 'nexaops'

param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'NexaOps SQL Administrators'

param deployAiServices = false
param deployEdgeServices = false
