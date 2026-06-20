// Event Grid wiring: a blob created in the "invoices-in" container pushes a
// BlobCreated event to the function's runtime webhook (blobs extension), which
// fires the EventGrid-sourced BlobTrigger. No polling.
//
// This subscription was originally created via `az` CLI; codifying it here makes
// it reproducible. The name matches the live subscription, so re-deploying
// ADOPTS it (idempotent update) rather than creating a duplicate.
@description('Existing Event Grid system topic (auto-created for the storage account).')
param systemTopicName string

@description('Existing Function App that handles the blob webhook.')
param functionAppName string

@description('Container whose blobs trigger ingestion.')
param incomingContainer string

@description('Name of the Function that ingests invoices (the [Function(...)] name).')
param ingestFunctionName string

resource func 'Microsoft.Web/sites@2023-12-01' existing = {
  name: functionAppName
}

resource topic 'Microsoft.EventGrid/systemTopics@2022-06-15' existing = {
  name: systemTopicName
}

// The blobs-extension system key authorizes Event Grid to invoke the runtime
// webhook. Pulled at deploy time via listKeys — never stored in source.
var blobExtensionKey = listKeys('${func.id}/host/default', '2023-12-01').systemKeys.blobs_extension
var webhookUrl = 'https://${func.properties.defaultHostName}/runtime/webhooks/blobs?functionName=Host.Functions.${ingestFunctionName}&code=${blobExtensionKey}'

resource subscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2022-06-15' = {
  parent: topic
  name: 'docpipe-invoices-sub'
  properties: {
    destination: {
      endpointType: 'WebHook'
      properties: {
        endpointUrl: webhookUrl
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: {
      includedEventTypes: [ 'Microsoft.Storage.BlobCreated' ]
      subjectBeginsWith: '/blobServices/default/containers/${incomingContainer}/'
      enableAdvancedFilteringOnArrays: false
    }
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
    eventDeliverySchema: 'EventGridSchema'
  }
}

output subscriptionName string = subscription.name
