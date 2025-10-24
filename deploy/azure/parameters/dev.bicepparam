using '../main.bicep'

// Development environment parameters
param environment = 'dev'
param location = 'eastus'

// Unique app name for development (change to your unique prefix)
// Must be globally unique - use your organization/project name
param appName = 'fhir-dev-yourorg'

// Optional: Azure AD user/group object ID for SQL admin
// Can be left empty if using certificate authentication
param sqlAdminEmail = ''
