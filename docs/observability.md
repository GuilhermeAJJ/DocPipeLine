# Observabilidade — DocPipeline (Fase 3)

A função exporta telemetria pro **Application Insights** (`docpipe-ai`) via OpenTelemetry
(ver `src/DocPipeline.Functions/Program.cs`). Há duas fontes de dados:

- **`traces`** — os logs estruturados (`_logger.LogInformation(...)`). Os parâmetros nomeados
  (`{Status}`, `{Confidence}`, `{Vendor}`, `{BlobName}`) viram colunas em `customDimensions`.
- **`customMetrics`** — as métricas customizadas de `InvoiceMetrics.cs`:
  - `docpipeline.invoices.processed` (contador, dimensão `status`)
  - `docpipeline.invoices.min_confidence` (histograma, dimensão `status`)

Cole as queries abaixo em **App Insights → Logs**. Linguagem: **KQL**.

> A coluna exata de dimensão pode aparecer como `customDimensions["status"]` ou
> `customDimensions.status` dependendo do exportador — ajuste se necessário.

---

## 1. Throughput — faturas processadas ao longo do tempo

```kql
traces
| where message has "->"            // a linha "Blob X -> Status (...)"
| summarize faturas = count() by bin(timestamp, 1h)
| render timechart
```

## 2. O split que justifica o projeto: auto-aprovado vs revisão vs falha

```kql
traces
| extend status = tostring(customDimensions.Status)
| where isnotempty(status)
| summarize total = count() by status
| render piechart
```

## 3. Distribuição da confiança mínima (valida o threshold de 0.80)

```kql
customMetrics
| where name == "docpipeline.invoices.min_confidence"
| extend status = tostring(customDimensions.status)
| summarize avg(value), p50 = percentile(value, 50), p10 = percentile(value, 10) by status
```

Versão por log (caso a métrica não esteja populada ainda):

```kql
traces
| extend conf = todouble(customDimensions.Confidence)
| where isnotempty(conf)
| summarize avg(conf), min(conf), max(conf) by bin(timestamp, 1d)
| render columnchart
```

## 4. Falhas recentes (com a mensagem de erro)

```kql
traces
| where tostring(customDimensions.Status) == "Failed" or message has "Failed to process blob"
| project timestamp, message, blob = tostring(customDimensions.BlobName)
| order by timestamp desc
```

## 5. Latência ponta-a-ponta da função

```kql
requests
| where name has "IngestInvoice"
| summarize p50 = percentile(duration, 50), p95 = percentile(duration, 95), count() by bin(timestamp, 1h)
| render timechart
```

---

## Alerta

A regra `docpipe-failed-invoices` (em `infra/modules/alerts.bicep`) roda a query da
seção 4 a cada 15 min e dispara se houver qualquer falha. Sem action group anexado, ela
só aparece no portal; pra receber e-mail/SMS, passe `actionGroupIds` no deploy:

```bash
az deployment group create -g <ServiçosCognitivos> \
  -f infra/main.bicep -p infra/main.parameters.json \
  -p actionGroupIds='["/subscriptions/.../actionGroups/<seu-grupo>"]'
```
