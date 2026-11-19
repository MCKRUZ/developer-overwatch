using '../main.bicep'

param environmentName = 'dev'
param location = 'eastus2'

// Sign-in is required on every Azure deploy; these fail to build when the variables are missing.
param oidcAuthority = readEnvironmentVariable('OIDC_AUTHORITY')
param oidcAudience = readEnvironmentVariable('OIDC_AUDIENCE')

// Set by the deploy workflow: false for the first stage, and the commit being deployed as the image tag.
param deployApps = bool(readEnvironmentVariable('OVERWATCH_DEPLOY_APPS', 'true'))
param apiImageTag = readEnvironmentVariable('OVERWATCH_IMAGE_TAG', 'latest')
param dashboardImageTag = readEnvironmentVariable('OVERWATCH_IMAGE_TAG', 'latest')
