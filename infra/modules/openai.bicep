// Azure OpenAI (Phase 4) — the enrichment brain. gpt-4.1-mini on GlobalStandard:
// economical, pay-per-token. Entra ID auth only (custom subdomain); the function's
// managed identity gets "Cognitive Services OpenAI User" via the roles module.
@description('Azure OpenAI account name (also the custom subdomain — must be globally unique).')
param name string

@description('Region — gpt-4.1-mini availability (e.g. eastus2). Need not match the rest of the stack.')
param location string

@description('Deployment name the app references (OpenAI:Deployment).')
param deploymentName string = 'gpt-41-mini'

param modelName string = 'gpt-4.1-mini'
param modelVersion string = '2025-04-14'

@description('GlobalStandard capacity in thousands of tokens/min.')
param capacity int = 10

resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: name // required for Entra ID (token) auth
    publicNetworkAccess: 'Enabled'
  }
}

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: deploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

output name string = openai.name
output endpoint string = openai.properties.endpoint
output deploymentName string = deployment.name
