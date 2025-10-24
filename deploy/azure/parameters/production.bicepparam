using '../main.bicep'

// Production environment parameters
param environment = 'production'
param location = 'eastus'

// Unique app name for production (change to your unique prefix)
// Must be globally unique - use your organization/project name
// Example: 'fhir-prod-healthcorp'
param appName = 'fhir-prod-yourorg'

// Required: Azure AD user or group object ID for SQL admin
// This should be a service principal or Azure AD security group
// with elevated privileges for production SQL management
param sqlAdminEmail = 'your-sql-admin-email@domain.com'
