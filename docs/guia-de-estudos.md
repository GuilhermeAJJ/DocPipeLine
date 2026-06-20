# Guia de Estudos — DocPipeline

> Guia pessoal de aprendizado ancorado no projeto **DocPipeline**.
> Objetivo: usar este projeto de portfólio como trilha de estudo rumo a **Arquiteto de
> Soluções** + preparação para a **AI-102**.
> Como usar: cada seção tem **o conceito**, **onde ele aparece no nosso código/infra**, e
> **o que você precisa saber explicar numa entrevista ou prova**.

---

## 0. O retrato do projeto em uma frase

> Um pipeline **serverless, event-driven** que recebe faturas, extrai dados com **IA de
> documentos**, decide automaticamente o que confiar e o que mandar pra um humano revisar,
> tudo com **segurança sem segredos** (identidade gerenciada) e **infraestrutura como código**.

Se você consegue defender cada palavra em **negrito** dessa frase, você domina o projeto.
Este guia te leva até lá.

---

## 1. O mapa mental: que "famílias" de conhecimento o projeto cobre

```
                         DocPipeline
                              │
   ┌──────────────┬──────────┼───────────┬───────────────┬──────────────┐
   ▼              ▼          ▼            ▼               ▼              ▼
Arquitetura   Serverless   Segurança   IA Aplicada   Dados/NoSQL   Operações
event-driven  & escala     (Identity   (Document     (Cosmos,      (IaC, obs.,
              a zero        + RBAC)     Intelligence) partição)     custo)
```

São 6 pilares. Um arquiteto não é quem sabe um deles fundo — é quem entende **como eles se
conectam e os trade-offs entre eles**. O resto do guia é cada pilar.

---

## 2. Pilar 1 — Arquitetura Event-Driven (orientada a eventos)

### O conceito
Em vez de um componente **perguntar** repetidamente "tem fatura nova?" (polling), o sistema
**reage** a um evento ("chegou uma fatura"). O produtor do evento (Storage) e o consumidor
(Function) não se conhecem — quem faz a ponte é um **broker** (Event Grid).

### Onde aparece no projeto
- `Storage invoices-in` emite `Microsoft.Storage.BlobCreated`.
- `Event Grid` (subscription `docpipe-invoices-sub`) empurra esse evento.
- A Function `IngestInvoice` tem `[BlobTrigger(..., Source = BlobTriggerSource.EventGrid)]`.

### O que você precisa saber explicar
- **Push vs Poll**: o blob trigger "clássico" faz polling no container (latência de até ~10
  min, custo de scan constante). Com Event Grid é push — latência de segundos, sem desperdício.
- **Desacoplamento**: o Storage não sabe que existe uma Function. Amanhã você pluga um segundo
  consumidor (ex.: notificar um Teams) **sem tocar no produtor**. Isso é extensibilidade.
- **Fan-out**: um evento pode ter vários assinantes.
- **Entrega e resiliência**: Event Grid faz retry com backoff e suporta **dead-lettering**
  (manda o evento que falhou N vezes pra um storage de "cartas mortas"). Pergunta de prova:
  "como você garante que um evento não se perde?"

### Conceitos relacionados pra estudar
- Event Grid **vs** Service Bus **vs** Event Hubs (os três "messaging" do Azure — saber quando
  usar cada um é clássico de entrevista de arquiteto):
  - **Event Grid**: eventos discretos, reativos, "algo aconteceu". (nosso caso)
  - **Service Bus**: mensagens/comandos com garantias fortes, ordenação, transações.
  - **Event Hubs**: streaming de altíssimo volume (telemetria, logs, IoT).
- Padrões: **Competing Consumers**, **Claim-Check**, **Pub/Sub**.

---

## 3. Pilar 2 — Serverless e escala a zero

### O conceito
O código roda só quando há trabalho. Sem requisição, **zero instância, zero custo**. A
plataforma escala horizontalmente sozinha conforme a carga.

### Onde aparece
- Function App no plano **Consumption (Y1 / Dynamic)**.

### O que você precisa saber explicar
- **Cold start**: a primeira execução depois de ocioso tem latência extra (subir o worker).
  No nosso teste, por isso esperamos ~80s. Trade-off: barato porém com latência variável.
  Mitigações: Premium plan (sempre quente), ou aceitar o cold start em cargas assíncronas
  como a nossa (ninguém está esperando em tempo real).
- **Modelos de hospedagem do Functions**: Consumption → Flex Consumption → Premium → Dedicated
  (App Service). Saber o eixo **custo × performance × controle**.
- **Isolated worker (.NET 8)**: o nosso modelo. O código roda num processo **separado** do
  host do Functions — desacopla a versão do .NET do runtime, dá controle total do DI e do
  middleware. (O modelo antigo "in-process" está sendo aposentado.)

### Trade-off que tomamos (e por quê)
Escolhemos Consumption porque faturas chegam esporadicamente — pagar por um plano sempre-ligado
seria desperdício. Custo-alvo do projeto: ~R$0/mês. **Decisão guiada pelo padrão de carga.**

---

## 4. Pilar 3 — Segurança: Identidade Gerenciada + RBAC (a joia do projeto)

### O conceito
O app **não guarda nenhuma senha/chave/connection string** de Cosmos ou Document Intelligence.
Ele tem uma **identidade** no Entra ID (Managed Identity) e o acesso é concedido via **papéis
(roles)** RBAC, com o **menor privilégio possível**.

### Onde aparece
- Function App com **system-assigned managed identity** (`principalId 0f8a2f71-...`).
- Código usa `DefaultAzureCredential` — pega um token do Entra ID em runtime.
- 3 role assignments least-privilege:
  | Recurso | Role | Por quê (a permissão mínima) |
  |---|---|---|
  | Storage | Storage Blob Data Owner | ler o blob da fatura |
  | Document Intelligence | Cognitive Services User | chamar a API de análise |
  | Cosmos | Cosmos DB Data Contributor | ler/gravar itens |

### O que você precisa saber explicar (isso impressiona em entrevista)
- **Por que isso é melhor que connection string?** Chave vaza em log, em repositório, em
  config. Identidade não tem segredo pra vazar — o token é efêmero e gerenciado pela plataforma.
- **Least privilege**: cada role é o **mínimo** e no **escopo do recurso específico**, não
  "Contributor na subscription". Se a função for comprometida, o estrago é limitado.
- **System-assigned vs User-assigned MI**: system = vida atrelada ao recurso (some quando o
  recurso some); user = identidade independente, reusável por vários recursos. Saber escolher.
- **A pegadinha do Cosmos (data-plane RBAC)**: o Cosmos tem um **sistema de RBAC próprio,
  separado do RBAC do Azure (ARM)** para acesso a *dados*. Por isso a role do Cosmos foi criada
  com `az cosmosdb sql role assignment`, não `az role assignment`. Role id built-in
  `00000000-...-000000000002` = "Cosmos DB Built-in Data Contributor". **Quase ninguém sabe
  isso — é um diferencial.**
- **A exceção consciente**: o `AzureWebJobsStorage` (plumbing interno do Consumption) ainda
  usa chave. Saber **distinguir "plumbing da plataforma" de "acesso de dados da aplicação"** é
  sinal de maturidade. A alternativa 100% identidade seria o plano **Flex Consumption**.

### Conceitos relacionados
- Entra ID (antigo Azure AD), Service Principals, OAuth2 client-credentials flow.
- Defesa em profundidade, princípio do menor privilégio, Zero Trust.
- (Próximo nível) Private Endpoints + VNet pra tirar tráfego da internet pública.

---

## 5. Pilar 4 — IA Aplicada: Document Intelligence + roteamento por confiança

> **Este é o pilar mais ligado à AI-102.**

### O conceito
Um modelo pré-treinado (`prebuilt-invoice`) lê uma fatura e devolve **campos estruturados**
(fornecedor, total, itens...) **com um score de confiança 0–1 por campo**. O sistema usa essa
confiança pra decidir entre **automatizar** ou **chamar um humano** (human-in-the-loop).

### Onde aparece
- `IngestInvoice` chama `AnalyzeDocumentAsync("prebuilt-invoice", ...)`.
- `InvoiceMapper` transforma o resultado em `InvoiceDocument`, calculando `MinConfidence`.
- `PipelineOptions.ConfidenceThreshold` (0.80) decide: `MinConfidence >= 0.80` →
  `AutoApproved`; senão → `PendingReview`.
- **Validação real**: a amostra "CONTOSO LTD." teve `MinConfidence = 0.723 < 0.80` → foi pra
  `PendingReview`. A lógica funcionou.

### O que você precisa saber explicar (provável na AI-102)
- **Prebuilt vs Custom models**: usamos o prebuilt-invoice (zero treino). Quando treinar um
  custom? Quando seus documentos têm layout próprio que o prebuilt não cobre.
- **Confiança não é acurácia**: o score diz "quão certo o modelo está", não "quão certo está".
  Por isso o threshold + revisão humana — você desenha o sistema pra **degradar com elegância**.
- **Human-in-the-loop**: automatizar o caso fácil, escalar humano só pra exceção. É o padrão
  de IA responsável e de ROI — você não precisa de 100% de automação pra gerar valor enorme.
- **Onde calibrar o threshold**: 0.80 é um ponto de partida. Subir = mais segurança, mais
  trabalho humano. Descer = mais automação, mais risco de erro. **É uma decisão de negócio**,
  não técnica — e você a deixou **configurável** (`PipelineOptions`), o que é design maduro.
- **Família Azure AI**: Document Intelligence faz parte dos **Azure AI Services** (antigo
  Cognitive Services). Saiba situar: Vision, Language, Speech, OpenAI, Document Intelligence.

### Gancho pra Fase 4 (Azure OpenAI)
A Fase 4 vai **enriquecer** o dado extraído (ex.: resumir, classificar a categoria de despesa,
detectar anomalia). Aí entram conceitos de **prompt engineering**, **grounding** (dar o dado
da fatura como contexto), e a diferença entre **extração determinística** (Document
Intelligence) e **geração** (OpenAI). Ótimo contraste pra estudar.

---

## 6. Pilar 5 — Dados & NoSQL: Cosmos DB e particionamento

### O conceito
Dados semi-estruturados (cada fornecedor, um layout; número variável de itens) pedem um banco
**de documentos (NoSQL)**, não tabelas rígidas. A **partition key** define como os dados se
distribuem fisicamente — escolher errado mata a performance e o custo.

### Onde aparece
- Cosmos DB (API NoSQL), container `invoices`, **partition key `/vendorName`**.
- Free tier (1000 RU/s grátis). Throughput de 400 RU/s no DB.
- Detalhe de serialização: o enum `ReviewStatus` é gravado como **int** (0–3). As queries
  filtram por `(int)status` — consistência importa.

### O que você precisa saber explicar
- **Por que NoSQL e não SQL aqui?** Esquema flexível, leitura por chave rápida, escala
  horizontal nativa. (Se fosse relatório analítico pesado com muitos JOINs, SQL/Synapse seria
  melhor — saber o **quando** é o ponto.)
- **Escolha da partition key** (tema central de Cosmos): `/vendorName` agrupa faturas do mesmo
  fornecedor → query "faturas da empresa X" é **single-partition** (barata e rápida). Riscos a
  saber discutir: **hot partition** (um fornecedor gigante desbalanceia) e o limite de 20GB
  por partição lógica. Alternativas: chave sintética, composta.
- **RU/s (Request Units)**: a "moeda" do Cosmos. Toda operação custa RUs. Provisioned vs
  Serverless vs Autoscale — saber o eixo custo × previsibilidade.
- **Modelos de consistência** (os 5 do Cosmos: Strong → Bounded Staleness → Session → Consistent
  Prefix → Eventual). Usamos **Session** (default) — bom equilíbrio. Saber explicar o trade-off
  consistência × latência × disponibilidade (isso conversa com o **Teorema CAP**).

### Pegadinha que vivemos
Criar o container/DB é responsabilidade da **infra** (control-plane), não do app. Sob RBAC de
data-plane, a identidade do app pode **ler/gravar itens** mas **não pode criar containers**.
Separação limpa de responsabilidades — e foi por isso que tivemos que criar o DB/container à
parte antes de testar.

---

## 7. Pilar 6 — Operações: IaC, Observabilidade e Custo

### Infraestrutura como Código (Bicep)
- **Conceito**: a infra é **declarada em arquivos versionados**, não clicada no portal.
  Reproduzível, revisável (PR), auditável.
- **Onde aparece**: `infra/` com Bicep **modular** (um módulo por responsabilidade: storage,
  cosmos, di, functionapp, roles, observability) orquestrado por `main.bicep`.
- **A dívida honesta deste projeto**: alguns recursos (Function App, App Insights, Event Grid)
  foram criados via **`az` CLI** pra destravar o teste, **não pelo Bicep**. Isso gera **drift**
  (o código não reflete a realidade). Reconciliar isso é o próximo passo — e **saber reconhecer
  e nomear esse drift já é pensamento de arquiteto.**
- **A pegadinha real**: o `az functionapp create` configura sozinho o *content share* do plano
  Consumption (`WEBSITE_CONTENTAZUREFILECONNECTIONSTRING`/`WEBSITE_CONTENTSHARE`); o módulo
  Bicep atual não — então um deploy Bicep ingênuo do Function App quebraria. Lição: **IaC exige
  conhecer os detalhes que a CLI esconde de você.**
- **Idempotência**: rodar o mesmo Bicep duas vezes leva ao mesmo estado. Estude isso.

### Observabilidade
- **Onde aparece**: Application Insights (`docpipe-ai`) + Log Analytics (`docpipe-law`).
- **Os três pilares da observabilidade**: **logs**, **métricas**, **traces** (rastros
  distribuídos). O App Insights cobre os três e correlaciona uma requisição ponta a ponta.
- O código loga eventos-chave (`"Blob X -> Status"`), e **falhas viram item `Failed` no Cosmos**
  em vez de sumir → o erro é **visível e acionável** (aparece na fila de revisão).
- Pra estudar: latência de ingestão (logs demoram 1–3 min pra aparecer — vivemos isso),
  Kusto/KQL (a linguagem de query do App Insights/Log Analytics), alertas, dashboards.

### Custo (FinOps, mentalidade de arquiteto)
- Todo o projeto roda em **tiers gratuitos** de propósito. Saber estimar e justificar custo é
  função de arquiteto. Conceitos: free tier, consumo vs provisionado, e o fato de que **a
  arquitetura mais barata costuma ser a serverless quando a carga é intermitente.**

---

## 8. Os "ensinamentos" — o que eu acho que você tem que levar deste projeto

Estes são os insights que separam "sei mexer no Azure" de "penso como arquiteto":

1. **Arquitetura é sobre trade-offs explícitos, não sobre a "melhor" tecnologia.**
   Cada escolha aqui (Consumption, NoSQL, Event Grid, MI) tem um *por quê* ligado a um
   *contexto* (carga intermitente, dado semi-estruturado, reatividade, segurança). Treine
   sempre responder "**por que isso e não a alternativa?**".

2. **Segurança se projeta na fundação, não se adiciona depois.**
   Decidir "zero segredo / identidade desde o dia 0" molda tudo. Retrofit de segurança é caro
   e furado. O pilar de Identity+RBAC aqui é o seu maior diferencial de portfólio.

3. **IA boa é IA que sabe quando NÃO confiar em si mesma.**
   O roteamento por confiança + human-in-the-loop é mais valioso que "acurácia alta". Sistemas
   de IA maduros **degradam com elegância** e mantêm humanos no laço onde importa.

4. **Falhas devem ser visíveis, não silenciosas.**
   Gravar `Failed` em vez de engolir a exceção é uma decisão de **operabilidade**. Pergunte
   sempre: "quando isso quebrar às 3h da manhã, como alguém vai descobrir e agir?".

5. **Desacoplamento compra futuro.**
   Produtor (Storage) que não conhece consumidor (Function), mediado por evento, te dá
   extensibilidade barata. Acoplamento é dívida que você paga com juros depois.

6. **Reconhecer dívida técnica é maturidade, escondê-la é risco.**
   Nós criamos recursos fora do Bicep pra andar rápido — e **nomeamos isso como dívida** a
   pagar. Um arquiteto navega o pragmatismo sem mentir pro futuro-eu.

7. **Os detalhes "chatos" são onde mora a senioridade.**
   Data-plane RBAC do Cosmos, content share do Consumption, latência de ingestão do App
   Insights, `MSYS_NO_PATHCONV`... ninguém te ensina isso em curso. É o que você só aprende
   colocando a mão e quebrando a cara — e é exatamente o que te diferencia.

---

## 9. Roteiro de estudo sugerido (ordem)

Estude **na ordem do fluxo do dado** — fixa melhor que estudar tópicos soltos:

1. **Storage + Event Grid** → entenda o evento `BlobCreated` e o modelo push.
   (Bônus: compare Event Grid × Service Bus × Event Hubs — desenhe quando usar cada um.)
2. **Azure Functions** → triggers/bindings, Consumption × Premium × Flex, isolated worker,
   cold start.
3. **Identidade & RBAC** → Managed Identity, `DefaultAzureCredential`, least privilege,
   system vs user-assigned, e o caso especial do data-plane do Cosmos.
4. **Document Intelligence** → prebuilt vs custom, confiança × acurácia, human-in-the-loop.
   (Núcleo da AI-102.)
5. **Cosmos DB** → partition key, RU/s, níveis de consistência, control-plane vs data-plane.
6. **Bicep / IaC** → módulos, idempotência, parâmetros, `existing`, e como reconciliar drift.
7. **Observabilidade** → App Insights, KQL, os 3 pilares, alertas.
8. (Depois) **Azure OpenAI** → prep da Fase 4.

Para cada item: leia a doc oficial da Microsoft Learn correspondente (a trilha AI-102 cobre
4 e parte de 8) **e** volte ao nosso código/infra pra ver o conceito encarnado. Teoria +
projeto real = retenção.

---

## 10. "Defenda seu projeto" — perguntas de entrevista que ele te prepara pra responder

Se você consegue responder estas, o projeto cumpriu o papel:

- Por que **event-driven** e não um job agendado (cron) varrendo o storage?
- Por que **Consumption** e não Premium? Qual o custo do cold start no seu caso?
- Como você **autentica** a função no Cosmos **sem connection string**? E por que o Cosmos
  precisou de uma role "diferente"?
- Por que **NoSQL**? Como você escolheu a **partition key** e quais os riscos dela?
- O que acontece quando o **Document Intelligence erra**? E quando ele tem **baixa confiança**?
- Onde você definiria o **threshold de confiança** e por quê isso é uma decisão de negócio?
- Como você **observa** esse sistema em produção? Como descobre uma falha?
- Qual a **dívida técnica** do projeto hoje e como você a pagaria?
- Como você **escalaria** isso pra 10.000 faturas/min? O que mudaria?

---

## 11. Glossário rápido

| Termo | Em uma linha |
|---|---|
| **Event-driven** | Reagir a eventos em vez de perguntar (polling). |
| **Broker** | Intermediário que entrega eventos (aqui, Event Grid). |
| **Serverless** | Você roda código sem gerenciar servidor; escala a zero. |
| **Cold start** | Latência da 1ª execução após ociosidade. |
| **Isolated worker** | Function rodando em processo separado do host. |
| **Managed Identity** | Identidade da plataforma para o recurso, sem segredo. |
| **RBAC** | Controle de acesso por papéis (roles). |
| **Least privilege** | Conceder só o mínimo necessário. |
| **Data-plane vs control-plane** | Acessar/gravar dados vs criar/gerenciar o recurso. |
| **Human-in-the-loop** | Automatizar o fácil, escalar humano na exceção. |
| **Confiança (confidence)** | Quão "certo" o modelo está de um campo (0–1). |
| **Partition key** | Campo que define a distribuição física no Cosmos. |
| **RU/s** | Request Units: a "moeda" de throughput do Cosmos. |
| **Consistência** | Trade-off entre dado fresco × latência × disponibilidade. |
| **IaC** | Infraestrutura declarada em código versionado. |
| **Idempotência** | Aplicar N vezes = mesmo resultado de 1 vez. |
| **Drift** | Divergência entre o IaC e a realidade provisionada. |
| **Observabilidade** | Logs + métricas + traces para entender o sistema. |
| **Dead-letter** | Destino de mensagens/eventos que falharam N vezes. |

---

> **Amanhã:** finalizamos — sugiro começar pagando a **dívida do Bicep** (Pilar 6), porque é
> onde teoria de IaC vira prática e fecha o projeto como um exemplo *honesto* de
> infraestrutura como código. Depois, Fase 2 (UI de revisão). Bons estudos! 🚀
