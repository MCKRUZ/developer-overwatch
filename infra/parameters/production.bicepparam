using '../main.bicep'

param environmentName = 'prod'
param location = 'eastus2'

// Azure SQL, not SQLite: the Azure file share cannot give SQLite the write locks it needs, so the API cannot start.
// This subscription is refused SQL servers in eastus2, so the database sits in centralus.
param useSqlServer = true
param sqlLocation = 'centralus'

// Sign-in is required on every Azure deploy; these fail to build when the variables are missing.
param oidcAuthority = readEnvironmentVariable('OIDC_AUTHORITY')
param oidcAudience = readEnvironmentVariable('OIDC_AUDIENCE')

// Set by the deploy workflow: false for the first stage, and the commit being deployed as the image tag.
param deployApps = bool(readEnvironmentVariable('OVERWATCH_DEPLOY_APPS', 'true'))
param apiImageTag = readEnvironmentVariable('OVERWATCH_IMAGE_TAG', 'latest')
param dashboardImageTag = readEnvironmentVariable('OVERWATCH_IMAGE_TAG', 'latest')
