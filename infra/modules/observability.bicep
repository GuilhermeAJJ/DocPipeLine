// Log Analytics + Application Insights — the telemetry backbone for Phase 3.
@description('Short prefix for resource names.')
param baseName string

@description('Region.')
param location string

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${baseName}-law'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${baseName}-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: law.id
  }
}

output connectionString string = appInsights.properties.ConnectionString
