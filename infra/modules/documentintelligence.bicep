// Azure AI Document Intelligence (Form Recognizer) on the FREE F0 tier.
// disableLocalAuth forces Entra ID auth — no API keys floating around.
@description('Document Intelligence account name.')
param name string

@description('Region.')
param location string

resource di 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  kind: 'FormRecognizer'
  sku: {
    name: 'F0' // free tier: 500 pages/month. Note: only ONE F0 Document Intelligence per subscription.
  }
  properties: {
    customSubDomainName: name // required for Entra ID (token) auth
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true // keys off — Managed Identity only
  }
}

output name string = di.name
output endpoint string = di.properties.endpoint
