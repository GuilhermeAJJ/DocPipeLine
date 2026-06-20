// ──────────────────────────────────────────────────────────────────────────
// DocPipeline — RECONCILE template (hybrid: adopt the LIVE environment).
//
// The live stack was provisioned partly by hand (Storage, Document
// Intelligence, Cosmos) and partly via `az` CLI (Log Analytics, App Insights,
// Function App, Event Grid). This file is the source of truth for what Bicep
// MANAGES going forward — it does NOT recreate those resources. Instead it:
//   • references the live resources as `existing` (by their real names), and
//   • declares only the pieces that are safe + idempotent to manage as code:
//       - RBAC role assignments for the function's managed identity, and
//       - the Event Grid subscription that wires blob events → the function.
//
// To build the whole thing from an empty resource group, use
// `main.greenfield.bicep` instead.
//
//   az deployment group create -g <ServiçosCognitivos> \
//     -f infra/main.bicep -p infra/main.parameters.json
//
// Tip: run `... what-if` first to preview changes against the live resources.
// ──────────────────────────────────────────────────────────────────────────
targetScope = 'resourceGroup'

@description('Existing storage account — the invoice landing zone.')
param storageName string = 'faturasportifolitb'

@description('Existing Azure AI Document Intelligence account.')
param documentIntelligenceName string = 'AnalisadorFatura'

@description('Existing Cosmos DB account (lives in West US 2).')
param cosmosName string = 'bancofaturastb'

@description('Existing Function App (system-assigned managed identity).')
param functionAppName string = 'docpipe-func-tb'

@description('Existing Event Grid system topic auto-created for the storage account.')
param eventGridSystemTopicName string = 'faturasportifolitb-f625a748-9e4a-4682-b5f0-5949efe68df5'

@description('Blob container whose BlobCreated events trigger ingestion.')
param incomingContainer string = 'invoices-in'

@description('Name of the ingest Function (matches [Function(...)] in code).')
param ingestFunctionName string = 'IngestInvoice'

@description('Existing Azure OpenAI account (Phase 4 enrichment).')
param openAiName string = 'docpipe-openai-tb'

@description('Existing Application Insights component (for the Phase 3 failure alert).')
param appInsightsName string = 'docpipe-ai'

@description('Region for new resources declared here (alerts). Defaults to the RG region.')
param location string = resourceGroup().location

// The function already exists; we only need its identity for role assignments.
resource func 'Microsoft.Web/sites@2023-12-01' existing = {
  name: functionAppName
}

// Least-privilege RBAC for the function's managed identity.
module roles 'modules/roles.bicep' = {
  name: 'roles'
  params: {
    storageName: storageName
    diName: documentIntelligenceName
    cosmosName: cosmosName
    openAiName: openAiName
    principalId: func.identity.principalId
  }
}

// Event Grid: blob lands in invoices-in → webhook → IngestInvoice.
module eventgrid 'modules/eventgrid.bicep' = {
  name: 'eventgrid'
  params: {
    systemTopicName: eventGridSystemTopicName
    functionAppName: functionAppName
    incomingContainer: incomingContainer
    ingestFunctionName: ingestFunctionName
  }
}

// Phase 3 observability — a log alert on processing failures.
module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    appInsightsName: appInsightsName
    location: location
  }
}

output functionPrincipalId string = func.identity.principalId
output eventSubscriptionName string = eventgrid.outputs.subscriptionName
