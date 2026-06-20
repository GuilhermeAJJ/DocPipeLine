# Faturas de teste

Coloque aqui arquivos de fatura (PDF, PNG, JPG, TIFF) para testar o pipeline.

Fontes de amostras públicas:
- Amostras oficiais do Document Intelligence Studio (modelo Invoice).
- Gere faturas fictícias — evite documentos com dados reais/sensíveis.

## Gerador de faturas sintéticas

`tools/InvoiceGenerator` produz PDFs realistas (vendors/categorias variados) que o
`prebuilt-invoice` lê com alta confiança. Útil pra demo do pipeline + Fase 4 (a IA
categoriza cada um de forma diferente: TI, Marketing, Logística, Materiais, Serviços).

```bash
dotnet run --project tools/InvoiceGenerator               # gera as 5 amostras em samples/generated/
dotnet run --project tools/InvoiceGenerator -- 3          # só as 3 primeiras
dotnet run --project tools/InvoiceGenerator -- 5 C:\tmp   # 5, em outro diretório
```

> Faturas limpas tendem a ser AUTO-aprovadas (confiança alta). Pra ver o caminho de
> revisão humana, use `samples/sample-invoice.pdf` (deu 0.723 < 0.80).

Para testar end-to-end, suba o arquivo no container `invoices-in` do storage:

```bash
az storage blob upload \
  --account-name <storage> \
  --container-name invoices-in \
  --file samples/minha-fatura.pdf \
  --name minha-fatura.pdf \
  --auth-mode login
```

> Estes arquivos não são versionados como dado sensível — não comite faturas reais.
