# Contexto da sessão — DocPipeline

> Arquivo de retomada. Última atualização: **2026-06-19**.
> Leia isto primeiro ao voltar ao projeto.

## 🎉 MARCO (2026-06-19): Fase 1 CONCLUÍDA E VALIDADA end-to-end

Pipeline real rodando no Azure. Subi `samples/sample-invoice.pdf` no `invoices-in` e o item
apareceu no Cosmos: `vendorName="CONTOSO LTD."`, `minConfidence=0.723`, `status=1 (PendingReview)`.
Como 0.723 < 0.80, roteou corretamente pra revisão humana — a lógica de confiança funciona.

**Recursos adotados (criados na mão, agora em uso):** RG `ServiçosCognitivos`.
- Storage `faturasportifolitb` (blob container `invoices-in` ✅) — brazilsouth
- Document Intelligence `analisadorfatura` — brazilsouth
- Cosmos `bancofaturastb` (DB `docpipeline` / container `invoices` PK `/vendorName`, free tier) — westus2

**Recursos criados nesta sessão (via `az` CLI, não Bicep ainda):**
- `docpipe-law` (Log Analytics) + `docpipe-ai` (App Insights) — observabilidade
- `docpipe-func-tb` (Function App, Consumption Y1, .NET8 isolated, Windows, system-assigned MI
  `principalId 0f8a2f71-4308-4990-b54f-b462ee79032a`)
- Event Grid subscription `docpipe-invoices-sub` no storage → webhook da função, filtrada
  pra `invoices-in` + `Microsoft.Storage.BlobCreated`.
- Roles concedidas à MI da função E ao usuário (dev local): Storage Blob Data Owner,
  Cognitive Services User, Cosmos DB Data Contributor (data-plane, role 00000000-...-002).
- Providers registrados: `Microsoft.Web`, `Microsoft.EventGrid`, `Microsoft.Insights`.

### ⏳ Dívida / próximos passos a partir daqui
1. ✅ **Reconciliar o Bicep** — RESOLVIDO (2026-06-20, estratégia HÍBRIDA):
   - `infra/main.bicep` agora é o template **reconcile**: referencia os recursos vivos como
     `existing` pelos nomes REAIS (`faturasportifolitb`, `AnalisadorFatura`, `bancofaturastb`,
     `docpipe-func-tb`) e declara só o que é seguro/idempotente: **roles** (RBAC da MI) +
     **eventgrid** (nova `modules/eventgrid.bicep`, adota a sub `docpipe-invoices-sub` via webhook
     do blobs-extension). NÃO recria os recursos — gerencia o que existe.
   - O template "do zero" virou `infra/main.greenfield.bicep` (+ `main.greenfield.parameters.json`),
     preservado como blueprint de DR / artefato de portfólio.
   - Pegadinha do content share CORRIGIDA no `modules/functionapp.bicep` (greenfield):
     `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING` + `WEBSITE_CONTENTSHARE` agora setados.
   - Bug pré-existente corrigido: `roles.bicep` usava `''` (estilo SQL) pra escapar apóstrofo;
     Bicep usa `\'`. Nunca tinha sido pego porque o Bicep nunca fora validado com `az` aqui.
   - Ambos templates compilam limpo: `az bicep build` = exit 0, 0 erros/warnings.
   - ⏭️ PENDENTE (opcional): rodar `az deployment group what-if` pra provar que o reconcile é
     no-op contra os recursos vivos antes de qualquer apply. (Cuidado: nome do RG tem `ç` que
     corrompe no PowerShell — construir com `"Servi$([char]0x00E7)osCognitivos"`.)
2. ✅ **Fase 2** — RESOLVIDA (2026-06-20): `/review` agora é master-detail editável.
   `InvoiceReviewService` (write/command side) grava `HumanApproved` + marca campos
   `HumanVerified`. Trata re-key da partição (`/vendorName` mudou → delete+upsert). Páginas
   demo `Counter`/`Weather` removidas. Build limpo. ⏭️ Falta TESTAR local (`dotnet run` no Web).
   - Pegadinha: o componente gerado de `Review.razor` se chama `Review` → não dá pra injetar
     serviço com o mesmo nome (CS0542). Injetado como `ReviewService`.
3. ✅ **Fase 3** — RESOLVIDA (2026-06-20): métricas customizadas `InvoiceMetrics.cs`
   (`docpipeline.invoices.processed` contador + `min_confidence` histograma) via OpenTelemetry
   → App Insights. Pacote de queries KQL em `docs/observability.md`. Alerta de falhas em
   `infra/modules/alerts.bicep` (scheduledQueryRule), ligado no `main.bicep` reconcile.
   ⏭️ Falta VALIDAR no Azure (deploy + subir fatura + ver métrica/dashboard).
4. ✅ **Fase 4** — RESOLVIDA (2026-06-20): Azure OpenAI enriquecimento AUTOMÁTICO no backend.
   - Infra criada via CLI: conta `docpipe-openai-tb` (eastus2), modelo `gpt-41-mini`
     (gpt-4.1-mini, GlobalStandard 10K TPM), role `Cognitive Services OpenAI User` → MI +
     usuário, app settings `OpenAI__Endpoint`/`OpenAI__Deployment` na função.
   - Código: `InvoiceEnricher` (Functions) chama o chat completions (MI, JSON mode) → resume,
     categoriza e normaliza o fornecedor; best-effort (falha não bloqueia pipeline).
     `InvoiceEnrichment` (Core) anexado ao `InvoiceDocument.Enrichment`. Exibido read-only no
     `/review` (badge "✨ Análise da IA").
   - IaC: `modules/openai.bicep` (greenfield cria), `roles.bicep` ganhou a role OpenAI,
     reconcile referencia `existing`. `local.settings.json` atualizado.
   - DECISÃO: usuário cogitou um chat widget interativo no Web mas preferiu MANTER só o
     enriquecimento automático. NÃO há SDK OpenAI no projeto Web (só na Function).
   - REGRA: LLM só pra COMPREENSÃO (resumir/categorizar/normalizar) — NUNCA extrair campo.
5. ✅ **Faturas fake** + validação E2E da Fase 4 (2026-06-20): `tools/InvoiceGenerator` gerou 5
   PDFs; subidas no `invoices-in`; auto-aprovadas (conf 0.89-0.92). Enriquecimento confirmado
   funcionando (TechNova: summary+category="TI"+normalizedVendor).
   - PEGADINHA: as 5 primeiras ficaram SEM enrichment porque a role OpenAI da MI ainda não
     tinha PROPAGADO (Cognitive Services leva minutos). Re-disparar (overwrite do blob) resolveu.
   - BUG corrigido: `Id` era `Guid.NewGuid()` → re-processar DUPLICAVA. Agora `Id` determinístico
     (SHA-256 do blobName, `InvoiceMapper.DeterministicId`) → upsert idempotente. PRECISA REPUBLICAR.
   - Nova tela `/invoices` (Faturas Processadas): lista todas com filtro de status + categoria/
     resumo da IA. `InvoiceQueryService.GetAllAsync`. NavMenu atualizado. (Web precisa restart.)
   - Cosmos tem DUPLICATAS antigas (ids aleatórios pré-fix). Limpar antes de gravar.
6. ✅ **Polish do front + tools de demo** (2026-06-20):
   - Home "Hello world" REMOVIDA; `/invoices` (Faturas Processadas) virou a home (`@page "/"`).
     Marca do menu: "DocPipeline". Idioma pt-BR. Layout sem o "About" do template.
   - OTIMIZAÇÃO da navegação lenta: `prerender:false` nas páginas Review/Invoices (elimina a
     dupla-consulta do Blazor) + `CosmosWarmup` (IHostedService aquece a conexão cross-region
     Brasil→westus2 no boot). Web precisa restart pra valer.
   - Tools novos em `tools/` (ver `tools/README.md`): `FolderWatcher` (observa pasta local→sobe
     pro invoices-in, simula pasta de rede) e `Monitor` (dashboard ao vivo do Cosmos em janela
     própria, atualiza a cada 3s — a "tela separada" pedida pra parte 5). Ambos build 0/0.
7. ✅ **Revisão: visualizador + reprovar** (2026-06-20):
   - `/review` agora mostra o DOCUMENTO original (iframe pra PDF, img pra JPG) ao lado dos campos.
     Endpoint `/files/{name}` no Web faz stream do blob via identidade (sem SAS/chave); novo
     `BlobContainerClient` no DI + config `Storage:BlobServiceUri`/`IncomingContainer`.
   - Botão **Reprovar** (exige motivo) → novo status `ReviewStatus.Rejected` (=4) + campo
     `InvoiceDocument.ReviewNote`. `InvoiceReviewService.RejectAsync`. Página Faturas Processadas
     ganhou filtro/badge "Reprovada". Build 0/0 (Web rodando trava só a cópia; validado em temp).
8. (Opcional) Commitar — tudo ainda untracked no git.
8. **Portfólio** (próximo): gravar o processo; site do usuário é Vite + TS + React (vai precisar
   de ajuda pra integrar/exibir).

### Pegadinhas novas desta sessão
- **Git Bash mutila paths** tipo `/vendorName` → `C:/Program Files/Git/vendorName`. Usar
  `MSYS_NO_PATHCONV=1` em comandos `az` com paths/escopos que começam com `/`.
- **Python não existe** nesta máquina. Pra query REST no Cosmos com AAD, o header é
  `Authorization: type%3Daad%26ver%3D1.0%26sig%3D<TOKEN>` (token JWT já é URL-safe, não precisa
  encodar). Headers: `x-ms-version: 2018-12-31`, `x-ms-documentdb-isquery: true`,
  `Content-Type: application/query+json`, `x-ms-documentdb-query-enablecrosspartition: true`.
- **Subscription de treino** vem com vários resource providers NÃO registrados — registrar sob demanda.
- Cosmos manual tem `disableLocalAuth` provavelmente FALSE (o Bicep setava true). Não bloqueia
  o fluxo identity, mas pra hardening vale desligar as chaves depois.

---
> Histórico anterior (2026-06-18) abaixo. ⚠️ vários itens já resolvidos acima.

## O que é o projeto

**DocPipeline** — pipeline serverless event-driven no Azure que processa **faturas** com
**Azure AI Document Intelligence** (`prebuilt-invoice`), roteia por **confiança**
(auto-aprovado vs revisão humana) e expõe uma UI de validação em Blazor.

Objetivo: projeto de portfólio mirando cargo de **arquiteto de soluções** + prática para a
certificação **AI-102**. Dono do projeto: dev C#/.NET, comunicação em PT-BR, tom informal.

## Decisões já travadas (não rediscutir)

- Cloud **real** no Azure (free tier). Custo alvo ≈ R$ 0/mês (tudo em tier gratuito).
- Caso de uso: **faturas** (não currículos) — `prebuilt-invoice` já entrega campos estruturados.
- Stack: **C# / .NET 8**. Functions **isolated worker**. UI **Blazor Server**.
- Persistência: **Cosmos DB** (NoSQL), particionado por `/vendorName`.
- Auth: **Managed Identity** + `DefaultAzureCredential` — zero segredo no app.
  (Exceção consciente: `AzureWebJobsStorage` usa chave — é plumbing do plano Consumption.)
- Infra: **Bicep** modular em `infra/`.
- SDK: **`Azure.AI.DocumentIntelligence` 1.0 (GA)** — NÃO usar o `Azure.AI.FormRecognizer` (deprecado).

## Estado atual (✅ feito / ⏳ pendente)

### Código — ✅ scaffold completo e COMPILANDO (`dotnet build` = 0 erros, 0 warnings)
- `src/DocPipeline.Core/` — modelos: `InvoiceDocument`, `ExtractedField`, `ReviewStatus`,
  `InvoiceLineItem`, `PipelineOptions` (threshold configurável, default 0.80).
- `src/DocPipeline.Functions/` — `IngestInvoice` (blob trigger via Event Grid → Document
  Intelligence → roteia por confiança → Cosmos), `InvoiceMapper`, `InvoiceRepository`, `Program.cs` (DI).
- `src/DocPipeline.Web/` — Blazor Server, página `/review` (stub read-only), `InvoiceQueryService`.
- `infra/` — Bicep: storage, documentintelligence (F0), cosmos (free tier), functionapp
  (Consumption), observability (App Insights), roles (RBAC least-privilege). + `main.parameters.json`.
- Docs: `README.md`, `docs/architecture.md`.

### Infra Azure — ⏳ parcialmente provisionada MANUALMENTE pelo usuário
O usuário **criou na mão** (fora do Bicep): **Cosmos DB**, **Document Intelligence**, **Blob Storage**.
> ⚠️ ATENÇÃO ao voltar: decidir se vamos (a) adotar esses recursos manuais e ajustar o Bicep/
> config para apontar pra eles, ou (b) deixar o Bicep criar tudo e descartar os manuais.
> Ainda **faltam**: Function App, App Insights, Event Grid subscription, e as **role assignments**
> (Managed Identity → Storage Blob Data Owner, Cognitive Services User, Cosmos Data Contributor).

### Ferramentas — ✅ instaladas nesta sessão
- .NET SDK 8.0.420 (já tinha), git 2.54 (já tinha).
- **Azure CLI (`az`)** e **Azure Functions Core Tools (`func`)** — instalados pelo usuário.
  (Pode precisar reabrir o terminal pro PATH pegar.)

## Próximos passos (ordem sugerida para amanhã)

1. **Reconciliar recursos manuais x Bicep** (ver atenção acima). Pegar os endpoints reais do
   Cosmos / Document Intelligence / Storage que o usuário criou.
2. Preencher os endpoints em:
   - `src/DocPipeline.Functions/local.settings.json` (`DocumentIntelligence__Endpoint`,
     `Cosmos__Endpoint`, `StorageConnection__blobServiceUri`).
   - `src/DocPipeline.Web/appsettings.json` (`Cosmos:Endpoint`).
3. Garantir o container blob `invoices-in` e o DB `docpipeline` / container `invoices` (PK `/vendorName`).
4. Conceder as **role assignments** à identidade (local: ao próprio usuário via `az login`).
5. **Fase 1** — testar caminho feliz: subir fatura no `invoices-in` → ver item no Cosmos.
   (Local: rodar a Function com `func start`; ou publicar a Function e ligar o Event Grid.)
6. Depois: **Fase 2** (edição/confirmação na fila), **Fase 3** (observabilidade), **Fase 4** (Azure OpenAI).

## Comandos úteis

```bash
# build
dotnet build src/DocPipeline.sln

# validar bicep (ainda NÃO validado com az nesta máquina)
az bicep build -f infra/main.bicep

# rodar a UI
cd src/DocPipeline.Web && dotnet run   # /review

# rodar a function local
cd src/DocPipeline.Functions && func start
```

## Pegadinhas conhecidas

- **Hooks do GSD**: estavam quebrados (prefixo `&` de PowerShell rodando em bash). Corrigido em
  `C:\Users\sixty\.claude\settings.json` (backup `.bak`). Se reinstalar o GSD, pode voltar.
- **Cosmos SDK 3.x** exige referência explícita a `Newtonsoft.Json` (já adicionada).
- **Status enum** é gravado como **int** no Cosmos (serializer default) — as queries filtram por
  `(int)status`, não pela string. Manter consistente.
- **Páginas demo** `Counter.razor` / `Weather.razor` ainda existem no Web (inofensivas, remover na Fase 2).
- **AzureWebJobsStorage** no plano Consumption ainda usa chave de storage (plumbing) — só o acesso
  de aplicação é por identidade. (Alternativa 100% identity: Flex Consumption.)
