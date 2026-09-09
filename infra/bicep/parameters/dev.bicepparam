using '../main.bicep'

// Development, sized to cost nothing.
//
// Every line below that turns something off is turning off a standing monthly charge, not a
// capability the product needs. The application detects each absence and says so rather than
// failing: no cache means an in-process one, no Service Bus means the event publisher reports
// itself unconfigured, no AI provider means the assistant says it is unavailable instead of
// inventing an answer.
param environment = 'dev'
param location = 'centralindia'
param workload = 'nexaops'

// Replace with the object id of the Entra group that administers SQL in this subscription.
// There is no SQL-authentication administrator, so an incorrect value here locks everyone out.
// The pipeline overrides this from a repository secret.
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'NexaOps SQL Administrators'

// The Azure SQL free offer: 100,000 vCore-seconds and 32 GB a month at no charge, and the
// database pauses rather than billing when that runs out. One per subscription.
param useSqlFreeLimit = true

// Azure Container Registry has no free tier, so images come from GitHub Container Registry,
// which is free for a public repository, and are pulled anonymously.
param deployRegistry = false

// Azure OpenAI needs model quota a new subscription does not have, and AI Search Basic is the
// largest single line item in this environment.
param deployAiServices = false

// Managed Redis has no free tier and a single replica has nothing to share a cache with.
param deployCache = false

// A Service Bus namespace carries a standing charge for a topic nobody is currently reading.
param deployMessaging = false

// Front Door and API Management are production concerns and are expensive.
param deployEdgeServices = false
