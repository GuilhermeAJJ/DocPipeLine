# DocPipeline — Processamento Inteligente de Faturas

Pipeline serverless e event-driven no Azure que recebe faturas, extrai os dados com
**Azure AI Document Intelligence**, decide por **confiança** o que pode ser lançado
automaticamente e o que precisa de **revisão humana**, e expõe tudo numa UI de validação.

Projeto de portfólio com foco em **arquitetura de soluções** e estudo prático para a
certificação **AI-102** (Azure AI Engineer Associate).

> Idioma: código e identificadores em inglês; documentação e UI em português.

---

## Por que faturas?

Processamento manual de contas a pagar é dor universal de qualquer financeiro. O modelo
`prebuilt-invoice` do Document Intelligence já entrega campos estruturados (fornecedor,
total, vencimento, itens) sem treino — então o esforço vai para onde está o diferencial de
arquiteto: **roteamento por confiança, human-in-the-loop, segurança, custo e observabilidade.**

---

## Arquitetura

```
[Blazor Upload] ──► Blob Storage ──► Event Grid ──► Azure Function (IngestInvoice)
                                                          │
                                                          ▼
                                          Document Intelligence (prebuilt-invoice)
                                                          │
                                              score de confiança por campo
                                  ┌───────────────────────┴───────────────────────┐
                            ≥ threshold                                       < threshold
                                  │                                                │
                                  ▼                                                ▼
                          Cosmos DB (AutoApproved)                 Cosmos DB (PendingReview)
                                                                                   │
                                                                                   ▼
                                                              Blazor — Fila de Revisão Humana
                                                          └───────────────────────┬───────────────────────┘
                                                                                  ▼
                                                      App Insights (confiança média, custo/doc, % automação)
```

Detalhamento técnico, decisões e roadmap completo em [`docs/architecture.md`](docs/architecture.md).

---

## Stack

| Camada | Tecnologia |
|---|---|
| Extração | Azure AI Document Intelligence (`prebuilt-invoice`), SDK `Azure.AI.DocumentIntelligence` 1.0 |
| Compute | Azure Functions .NET 8 isolated worker, plano Consumption (Y1) |
| Ingestão | Blob Storage + Event Grid (push, sem polling) |
| Persistência | Cosmos DB (NoSQL), particionado por fornecedor |
| UI de revisão | Blazor Server (.NET 8) |
| Segurança | Managed Identity + `DefaultAzureCredential` (zero chave no app) |
| Observabilidade | Application Insights + OpenTelemetry |
| Infra | Bicep (modular) |

---

## Estrutura do repositório

```
infra/                     # Bicep — fundação Azure (Fase 0)
  main.bicep
  main.parameters.json
  modules/                 # storage, documentintelligence, cosmos, functionapp, roles, observability
src/
  DocPipeline.sln
  DocPipeline.Core/        # modelos de domínio (InvoiceDocument, ReviewStatus, ...)
  DocPipeline.Functions/   # IngestInvoice + mapeamento + repositório Cosmos
  DocPipeline.Web/         # Blazor Server — fila de revisão
docs/architecture.md
samples/                   # coloque aqui faturas de teste (PDF/PNG/JPG)
```

---

## Pré-requisitos

- [.NET SDK 8.0](https://dotnet.microsoft.com/download) ✅ (já instalado)
- **Azure CLI** — instale: `winget install Microsoft.AzureCLI`
- **Azure Functions Core Tools v4** — instale: `winget install Microsoft.Azure.FunctionsCoreTools`
- **Bicep** — vem com o Azure CLI (`az bicep install`)
- Assinatura Azure (free tier serve)

> ⚠️ Reinicie o terminal após instalar o `az`/`func` para o PATH atualizar.

---

## Deploy (Fase 0 — fundação)

```bash
az login
az group create -n rg-docpipeline -l brazilsouth

# Provisiona storage, Document Intelligence (F0), Cosmos (free tier),
# Function App, App Insights e todas as role assignments (RBAC).
az deployment group create \
  -g rg-docpipeline \
  -f infra/main.bicep \
  -p infra/main.parameters.json
```

### Publicar a Function

```bash
cd src/DocPipeline.Functions
func azure functionapp publish <nome-do-function-app>   # sai no output do deploy
```

### Conectar o Event Grid (após a function estar publicada)

A assinatura do Event Grid precisa que a function já exista (há um handshake de validação):

```bash
az eventgrid event-subscription create \
  --name invoices-to-function \
  --source-resource-id $(az storage account show -g rg-docpipeline -n <storage> --query id -o tsv) \
  --endpoint-type webhook \
  --endpoint "https://<function-app>.azurewebsites.net/runtime/webhooks/blobs?functionName=IngestInvoice&code=<blob-extension-key>" \
  --included-event-types Microsoft.Storage.BlobCreated
```

Pronto: suba uma fatura no container `invoices-in` e acompanhe no App Insights.

---

## Rodar local

Para autenticação local, o `DefaultAzureCredential` usa o seu `az login`:

```bash
az login
# preencha os endpoints reais em src/DocPipeline.Functions/local.settings.json
# e em src/DocPipeline.Web/appsettings.json (Cosmos:Endpoint)
cd src/DocPipeline.Web
dotnet run        # UI de revisão em https://localhost:xxxx/review
```

---

## Custo

Tudo foi desenhado para caber nos **tiers gratuitos** (Document Intelligence F0,
Cosmos free tier, Functions Consumption, App Insights 5 GB/mês). Em ambiente de
demonstração, o custo fica próximo de **R$ 0/mês**.

---

## Roadmap

- [x] **Fase 0** — Fundação (Bicep, identidade, RBAC)
- [ ] **Fase 1** — Pipeline feliz (blob → extração → Cosmos)
- [ ] **Fase 2** — Human-in-the-loop (threshold + edição/confirmação na fila)
- [ ] **Fase 3** — Observabilidade (dashboard confiança/custo/automação)
- [ ] **Fase 4** — Azure OpenAI: perguntas em linguagem natural sobre as faturas

## Mapeamento AI-102

| Domínio da prova | Onde aparece |
|---|---|
| Planejar e gerenciar soluções de Azure AI | Bicep, Managed Identity, RBAC, custo |
| Document Intelligence | `IngestInvoice` + `prebuilt-invoice` |
| Soluções generativas (Azure OpenAI) | Fase 4 (consulta em linguagem natural) |
