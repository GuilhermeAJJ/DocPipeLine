# Ferramentas de apoio (demo / portfólio)

Três utilitários .NET para a demonstração do DocPipeline. Todos autenticam por `az login`
(Managed Identity / Entra ID) — sem chaves.

## 1. InvoiceGenerator — gera faturas sintéticas (PDF)

```bash
dotnet run --project tools/InvoiceGenerator            # 5 faturas em samples/generated/
dotnet run --project tools/InvoiceGenerator -- 3       # só as 3 primeiras
```

## 2. FolderWatcher — "pasta de rede" que sobe sozinha

Observa uma pasta local e envia qualquer `.pdf` para o container `invoices-in`, disparando
o pipeline. Simula um ERP/scanner gravando numa pasta compartilhada.

```bash
dotnet run --project tools/FolderWatcher                       # observa samples/dropzone
dotnet run --project tools/FolderWatcher -- "C:\faturas-rede"  # outra pasta
```

Durante a gravação: deixe rodando, e arraste um PDF (ex.: de `samples/generated`) para a
pasta — ele sobe e aparece no front em segundos.

## 3. Monitor — dashboard ao vivo (janela separada)

Consulta o Cosmos a cada 3s e redesenha um resumo: contagem por status + últimas faturas
com a categoria da IA. Rode numa janela própria ao lado do navegador na gravação.

```bash
dotnet run --project tools/Monitor          # atualiza a cada 3s
dotnet run --project tools/Monitor -- 5     # a cada 5s
```

> Métricas da Fase 3 (throughput, latência) ficam no App Insights — use o portal
> (blade Metrics) ou as queries KQL em `docs/observability.md` como segunda tela.
