# Arquitetura — DocPipeline

Documento de decisões. O objetivo aqui é registrar **por que** cada escolha foi feita —
é isso que diferencia um projeto de arquiteto de um tutorial.

## 1. Princípios

1. **Event-driven, não batch.** Cada fatura é um evento. Ingestão desacoplada via Event Grid.
2. **Serverless-first.** Sem servidor para gerenciar; custo proporcional ao uso (≈0 em demo).
3. **Confiança como cidadã de primeira classe.** A IA não decide sozinha; ela prioriza o
   trabalho humano. O threshold é configurável (não é número mágico no código).
4. **Zero segredo no aplicativo.** Todo acesso de aplicação usa Managed Identity.
5. **Infra reprodutível.** Tudo sobe e desce com Bicep.

## 2. Fluxo

1. Usuário (ou sistema) sobe a fatura no container `invoices-in`.
2. Event Grid emite `BlobCreated` → dispara a função `IngestInvoice`.
3. A função chama o Document Intelligence (`prebuilt-invoice`).
4. `InvoiceMapper` converte o resultado em `InvoiceDocument` e calcula a **menor
   confiança** entre os campos críticos (InvoiceId, InvoiceTotal, InvoiceDate, DueDate, VendorName).
5. Política de roteamento:
   - `MinConfidence >= threshold` → `AutoApproved`
   - caso contrário → `PendingReview`
6. Persiste no Cosmos (partição = fornecedor).
7. A UI Blazor lista os `PendingReview` para validação humana.

## 3. Decisões-chave

### Por que Cosmos DB (e não SQL)?
O JSON do Document Intelligence é semiestruturado e os itens de linha variam por fatura.
NoSQL absorve essa variação sem migração de schema. Free tier (1000 RU/s) cobre a demo.
Partição por `/vendorName` torna "todas as faturas do fornecedor X" uma query de partição
única — exatamente a pergunta que a Fase 4 (Azure OpenAI) vai fazer.

### Por que blob trigger via Event Grid (e não polling)?
O trigger padrão de blob faz polling do container (latência e custo de varredura). Com
`Source = EventGrid`, a função é **empurrada** pelo evento — menor latência, escala melhor.

### Auth: a nuance do AzureWebJobsStorage
No plano Consumption, o `AzureWebJobsStorage` (content share interno da plataforma) ainda
exige uma **chave** de storage. Isso é *plumbing de plataforma*, não acesso a dado de
aplicação. Todo o acesso de aplicação — leitura do blob, Document Intelligence, Cosmos —
usa **Managed Identity**. Saber separar esses dois mundos é um ótimo ponto de conversa em
entrevista. (Alternativa "100% identity": plano Flex Consumption.)

### RBAC mínimo
A identidade da função recebe só:
- **Storage Blob Data Owner** — ler as faturas.
- **Cognitive Services User** — chamar o analyze do Document Intelligence.
- **Cosmos DB Data Contributor** (RBAC de data-plane próprio do Cosmos) — gravar itens.

Note que a função **não** pode criar containers no Cosmos — isso é responsabilidade do
Bicep. Separação de control-plane e data-plane.

## 4. Os "ilities" (o que faz parecer produção)

- **Segurança:** Managed Identity, `disableLocalAuth` no DI e no Cosmos, sem chave no app.
- **Observabilidade:** App Insights + OpenTelemetry; métricas de confiança média, custo/doc
  e % de automação (Fase 3).
- **Resiliência:** falha de extração é gravada como `Failed` (não some) e a exceção é
  relançada para retry/dead-letter da plataforma.
- **Custo (FinOps):** desenho all-free-tier; narrativa de custo por documento.
- **Governança:** infra versionada em Bicep, reprodutível e destruível.

## 5. Roadmap detalhado

| Fase | Entrega | Status |
|---|---|---|
| 0 | Bicep + identidade + RBAC | ✅ |
| 1 | blob → extração → Cosmos (caminho feliz) | ⏳ |
| 2 | threshold + edição/confirmação na fila de revisão | ⏳ |
| 3 | dashboard de confiança/custo/automação | ⏳ |
| 4 | Azure OpenAI — perguntas em linguagem natural sobre as faturas | ⏳ |

## 6. Pontos em aberto / próximas decisões

- Estratégia de reprocessamento quando o humano corrige (versionar o documento?).
- Dead-letter container para faturas que falham N vezes.
- Mascaramento de dados sensíveis (CPF/CNPJ) antes de logar.
- Flex Consumption para eliminar a última chave (AzureWebJobsStorage).
