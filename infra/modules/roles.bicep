// RBAC: grant the function's Managed Identity exactly what it needs — nothing more.
// This least-privilege story is the security centerpiece of the project.
param storageName string
param diName string
param cosmosName string
param openAiName string

@description('Principal (object) id of the function\'s system-assigned identity.')
param principalId string

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}
resource di 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: diName
}
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: cosmosName
}
resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: openAiName
}

// Built-in role definition ids (stable GUIDs across Azure).
var storageBlobDataOwner = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
var cognitiveServicesUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87618995b6')
var cognitiveServicesOpenAiUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')

// Read invoices dropped in blob storage.
resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, principalId, storageBlobDataOwner)
  scope: storage
  properties: {
    roleDefinitionId: storageBlobDataOwner
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Call the Document Intelligence analyze API via Entra ID.
resource diRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(di.id, principalId, cognitiveServicesUser)
  scope: di
  properties: {
    roleDefinitionId: cognitiveServicesUser
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Call the Azure OpenAI chat completions API via Entra ID (Phase 4 enrichment).
resource openAiRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openai.id, principalId, cognitiveServicesOpenAiUser)
  scope: openai
  properties: {
    roleDefinitionId: cognitiveServicesOpenAiUser
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Cosmos uses its OWN data-plane RBAC system (not standard Azure RBAC).
// 00000000-...-000000000002 is the built-in "Cosmos DB Data Contributor" role.
resource cosmosDataRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, principalId, 'data-contributor')
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: principalId
    scope: cosmos.id
  }
}
