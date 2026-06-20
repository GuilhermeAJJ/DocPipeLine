// Cosmos DB (NoSQL) on the FREE tier (first 1000 RU/s + 25 GB free per account).
// The database/container are created HERE, not in app code — under data-plane RBAC the
// app identity can read/write items but cannot create containers. Clean separation.
@description('Cosmos account name (lowercase).')
param accountName string

@description('Region.')
param location string

param databaseName string
param containerName string

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: true // <-- the free 1000 RU/s. Only one free-tier account per subscription.
    disableLocalAuth: true // keys off — Entra ID data-plane RBAC only
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
  }

  resource db 'sqlDatabases@2024-11-15' = {
    name: databaseName
    properties: {
      resource: { id: databaseName }
      options: {
        throughput: 400 // well within the free 1000 RU/s
      }
    }

    resource container 'containers@2024-11-15' = {
      name: containerName
      properties: {
        resource: {
          id: containerName
          partitionKey: {
            paths: [ '/vendorName' ] // matches InvoiceDocument.VendorName (camelCased)
            kind: 'Hash'
          }
        }
      }
    }
  }
}

output name string = account.name
output endpoint string = account.properties.documentEndpoint
