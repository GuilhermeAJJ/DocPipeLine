using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Core;
using DocPipeline.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace DocPipeline.Functions;

/// <summary>
/// Phase 4 — enriches an extracted invoice with an LLM (Azure OpenAI, gpt-4.1-mini).
///
/// Architectural rule worth stating in an interview: we DON'T use the LLM to extract
/// fields (Document Intelligence is cheaper, deterministic, and better at that). The LLM
/// is reserved for what needs language UNDERSTANDING — summarizing, categorizing, and
/// normalizing the vendor. Auth is Managed Identity (no keys), same as everything else.
///
/// Enrichment is best-effort: any failure is logged and swallowed so it never blocks the
/// core pipeline. The invoice still lands in Cosmos, just without the LLM layer.
/// </summary>
public sealed class InvoiceEnricher
{
    private readonly ChatClient? _chat;
    private readonly ILogger<InvoiceEnricher> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public InvoiceEnricher(IConfiguration config, TokenCredential credential, ILogger<InvoiceEnricher> logger)
    {
        _logger = logger;

        var endpoint = config["OpenAI:Endpoint"];
        var deployment = config["OpenAI:Deployment"];
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(deployment))
        {
            _chat = new AzureOpenAIClient(new Uri(endpoint), credential).GetChatClient(deployment);
        }
        else
        {
            _logger.LogWarning("OpenAI:Endpoint/Deployment not configured — enrichment disabled.");
        }
    }

    public async Task<InvoiceEnrichment?> EnrichAsync(InvoiceDocument doc, CancellationToken ct = default)
    {
        if (_chat is null) return null; // not configured → skip

        try
        {
            var messages = new ChatMessage[]
            {
                new SystemChatMessage(
                    "Você analisa faturas já extraídas e responde SEMPRE com um JSON válido, " +
                    "sem texto extra, com exatamente estas chaves: " +
                    "\"summary\" (uma frase em português resumindo a fatura), " +
                    "\"category\" (uma de: TI, Marketing, Serviços, Materiais, Logística, Outros), " +
                    "\"normalizedVendor\" (o nome do fornecedor de forma canônica, sem sufixos como LTDA/LTD/ME)."),
                new UserChatMessage(BuildPrompt(doc))
            };

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                Temperature = 0.2f
            };

            var completion = await _chat.CompleteChatAsync(messages, options, ct);
            var json = completion.Value.Content[0].Text;

            var enrichment = JsonSerializer.Deserialize<InvoiceEnrichment>(json, JsonOpts);
            if (enrichment is not null)
            {
                enrichment.EnrichedAt = DateTimeOffset.UtcNow;
            }
            return enrichment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enrichment failed for blob {Blob} — continuing without it.", doc.BlobName);
            return null;
        }
    }

    private static string BuildPrompt(InvoiceDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Dados extraídos da fatura:");
        sb.AppendLine($"- Fornecedor: {doc.VendorName}");
        sb.AppendLine($"- Nº da fatura: {doc.InvoiceId.Value}");
        sb.AppendLine($"- Valor total: {doc.InvoiceTotal.Value}");
        sb.AppendLine($"- Data de emissão: {doc.InvoiceDate.Value}");
        sb.AppendLine($"- Data de vencimento: {doc.DueDate.Value}");
        if (doc.LineItems.Count > 0)
        {
            sb.AppendLine("- Itens:");
            foreach (var li in doc.LineItems.Take(15))
            {
                sb.AppendLine($"    • {li.Description} (qtd {li.Quantity}, valor {li.Amount})");
            }
        }
        return sb.ToString();
    }
}
