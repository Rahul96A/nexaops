using '../main.bicep'

// Production. Business Critical SQL with geo-redundant backups and seven-year long-term
// retention, zone-redundant compute, and Front Door with the WAF in prevention mode.
param environment = 'prod'
param location = 'centralindia'
param workload = 'nexaops'

param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'NexaOps SQL Administrators'

param deployAiServices = true
param deployEdgeServices = true

// Set once the DNS record and managed certificate exist.
param customDomain = ''
