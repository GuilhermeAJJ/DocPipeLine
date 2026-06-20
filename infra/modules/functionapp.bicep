// Azure Functions on the Consumption (Y1) plan — .NET 8 isolated worker.
//
// Auth design (worth explaining in an interview):
//  - AzureWebJobsStorage uses a storage KEY. This is platform plumbing the Consumption
//    plan requires for its internal content share; it is NOT application data access.
//  - ALL application access (blob trigger, Document Intelligence, Cosmos) uses the
//    function's system-assigned Managed Identity. Zero app secrets.
@description('Short prefix for resource names.')
param baseName string

@description('Region.')
param location string

param storageName string
param appInsightsConnectionString string
param diEndpoint string
param cosmosEndpoint string
param openAiEndpoint string
param openAiDeployment string
param databaseName string
param containerName string
param confidenceThreshold string

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${baseName}-plan'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
}

resource func 'Microsoft.Web/sites@2023-12-01' = {
  name: '${baseName}-func'
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      appSettings: [
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }

        // Platform plumbing (content share) — key-based, required by Consumption plan.
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageName};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        // Consumption GOTCHA: `az functionapp create` sets these two automatically,
        // but Bicep does NOT. Without them the app has no content share and fails to
        // start. Same connection string as above; the share name must be lowercase.
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageName};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower('${baseName}func${take(uniqueString(resourceGroup().id), 6)}')
        }

        // App-level blob access for the Event Grid blob trigger — IDENTITY based (no key).
        { name: 'StorageConnection__blobServiceUri', value: storage.properties.primaryEndpoints.blob }
        { name: 'StorageConnection__queueServiceUri', value: storage.properties.primaryEndpoints.queue }

        // Endpoints consumed by DefaultAzureCredential in code.
        { name: 'DocumentIntelligence__Endpoint', value: diEndpoint }
        { name: 'Cosmos__Endpoint', value: cosmosEndpoint }
        { name: 'OpenAI__Endpoint', value: openAiEndpoint }
        { name: 'OpenAI__Deployment', value: openAiDeployment }

        { name: 'Pipeline__ConfidenceThreshold', value: confidenceThreshold }
        { name: 'Pipeline__CosmosDatabaseName', value: databaseName }
        { name: 'Pipeline__CosmosContainerName', value: containerName }
      ]
    }
  }
}

output name string = func.name
output principalId string = func.identity.principalId
