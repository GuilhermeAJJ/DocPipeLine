// Phase 3 — a log alert on invoice processing failures. Runs a KQL query against
// Application Insights every 15 min and fires if any invoice failed. With no action
// group attached it simply surfaces in the portal; pass actionGroupIds to get notified.
@description('Existing Application Insights component to query.')
param appInsightsName string

@description('Region for the alert rule.')
param location string

@description('Optional action group resource IDs to notify (email/SMS/webhook). Empty = portal-only.')
param actionGroupIds array = []

resource ai 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource failureAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'docpipe-failed-invoices'
  location: location
  properties: {
    displayName: 'DocPipeline — invoice processing failures'
    description: 'Fires when the ingest function records a Failed invoice (extraction or persistence error).'
    severity: 2
    enabled: true
    scopes: [ ai.id ]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: 'traces | where tostring(customDimensions.Status) == "Failed" or message has "Failed to process blob"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: actionGroupIds
    }
    autoMitigate: true
  }
}
