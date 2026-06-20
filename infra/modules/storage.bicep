// Blob storage — the landing zone. Invoices are dropped into the "invoices-in" container,
// which fires an Event Grid event that triggers the ingest function.
@description('Globally-unique storage account name (lowercase, <=24 chars).')
param storageName string

@description('Region.')
param location string

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    allowSharedKeyAccess: true // platform plumbing (AzureWebJobsStorage) still uses a key; app data uses identity
  }

  resource blob 'blobServices@2023-05-01' = {
    name: 'default'
    resource incoming 'containers@2023-05-01' = {
      name: 'invoices-in'
    }
  }
}

output name string = storage.name
