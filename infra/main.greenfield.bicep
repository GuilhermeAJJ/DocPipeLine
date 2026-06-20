// ──────────────────────────────────────────────────────────────────────────
// DocPipeline — GREENFIELD template (build the whole stack from scratch).
//
// This is the "ideal" infrastructure-as-code: deploy into an EMPTY resource
// group and it creates every resource with generated, unique names.
//
//   az group create -n rg-docpipeline -l brazilsouth
//   az deployment group create -g rg-docpipeline \
//     -f infra/main.greenfield.bicep -p infra/main.greenfield.parameters.json
//
// ⚠️ The LIVE environment was NOT built with this file — several resources were
// created by hand / via `az` CLI with custom names and across two regions.
// To manage what actually exists, use `main.bicep` (the reconcile template),
// which references the live resources as `existing`. Keep this file as the
// from-zero reference / disaster-recovery blueprint.
// ──────────────────────────────────────────────────────────────────────────
targetScope = 'resourceGroup'

@minLength(3)
@maxLength(11)
@description('Short alphanumeric prefix for all resources, e.g. "docpipe".')
param baseName string

@description('Region for all resources.')
param location string = resourceGroup().location

@description('Confidence threshold (0-1, as string) below which invoices route to human review.')
param confidenceThreshold string = '0.80'

param databaseName string = 'docpipeline'
param containerName string = 'invoices'

@description('Region for the Azure OpenAI account (gpt-4.1-mini availability, e.g. eastus2).')
param openAiLocation string = 'eastus2'

var suffix = take(uniqueString(resourceGroup().id), 8)
var storageName = toLower('${baseName}st${suffix}')
var diName = '${baseName}-di-${suffix}'
var cosmosName = toLower('${baseName}-cosmos-${suffix}')
var openAiName = toLower('${baseName}-openai-${suffix}')

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    baseName: baseName
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    storageName: storageName
    location: location
  }
}

module di 'modules/documentintelligence.bicep' = {
  name: 'documentintelligence'
  params: {
    name: diName
    location: location
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    accountName: cosmosName
    location: location
    databaseName: databaseName
    containerName: containerName
  }
}

module openai 'modules/openai.bicep' = {
  name: 'openai'
  params: {
    name: openAiName
    location: openAiLocation
  }
}

module functionApp 'modules/functionapp.bicep' = {
  name: 'functionapp'
  params: {
    baseName: baseName
    location: location
    storageName: storage.outputs.name
    appInsightsConnectionString: observability.outputs.connectionString
    diEndpoint: di.outputs.endpoint
    cosmosEndpoint: cosmos.outputs.endpoint
    openAiEndpoint: openai.outputs.endpoint
    openAiDeployment: openai.outputs.deploymentName
    databaseName: databaseName
    containerName: containerName
    confidenceThreshold: confidenceThreshold
  }
}

module roles 'modules/roles.bicep' = {
  name: 'roles'
  params: {
    storageName: storage.outputs.name
    diName: di.outputs.name
    cosmosName: cosmos.outputs.name
    openAiName: openai.outputs.name
    principalId: functionApp.outputs.principalId
  }
}

output functionAppName string = functionApp.outputs.name
output storageAccount string = storage.outputs.name
output documentIntelligenceEndpoint string = di.outputs.endpoint
output cosmosEndpoint string = cosmos.outputs.endpoint
