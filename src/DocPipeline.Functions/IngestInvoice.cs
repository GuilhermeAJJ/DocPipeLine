using Azure;
using Azure.AI.DocumentIntelligence;
using DocPipeline.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocPipeline.Functions;

/// <summary>
/// The heart of Phases 1 + 2: a blob lands in "invoices-in" → extract with Document
/// Intelligence → route by confidence → persist. Triggered via Event Grid (push, no polling).
/// </summary>
public sealed class IngestInvoice
{
    private readonly DocumentIntelligenceClient _docClient;
    private readonly InvoiceRepository _repository;
    private readonly InvoiceEnricher _enricher;
    private readonly PipelineOptions _options;
    private readonly InvoiceMetrics _metrics;
    private readonly ILogger<IngestInvoice> _logger;

    public IngestInvoice(
        DocumentIntelligenceClient docClient,
        InvoiceRepository repository,
        InvoiceEnricher enricher,
        IOptions<PipelineOptions> options,
        InvoiceMetrics metrics,
        ILogger<IngestInvoice> logger)
    {
        _docClient = docClient;
        _repository = repository;
        _enricher = enricher;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    [Function(nameof(IngestInvoice))]
    public async Task Run(
        [BlobTrigger("invoices-in/{name}", Connection = "StorageConnection", Source = BlobTriggerSource.EventGrid)]
        byte[] content,
        string name,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing blob {BlobName} ({Bytes} bytes)", name, content.Length);

        try
        {
            var operation = await _docClient.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                new AnalyzeDocumentOptions("prebuilt-invoice", BinaryData.FromBytes(content)),
                cancellationToken);

            var invoice = InvoiceMapper.ToInvoiceDocument(operation.Value, name, _options.ConfidenceThreshold);

            // Phase 4: layer LLM understanding on top of the extraction (best-effort).
            invoice.Enrichment = await _enricher.EnrichAsync(invoice, cancellationToken);

            await _repository.UpsertAsync(invoice, cancellationToken);
            _metrics.RecordProcessed(invoice);

            _logger.LogInformation(
                "Blob {BlobName} -> {Status} (minConfidence={Confidence:P0}, vendor={Vendor})",
                name, invoice.Status, invoice.MinConfidence, invoice.VendorName);
        }
        catch (Exception ex)
        {
            // Record the failure so it shows up in the review queue instead of vanishing,
            // then rethrow to let the Functions platform handle retry / dead-lettering.
            _logger.LogError(ex, "Failed to process blob {BlobName}", name);
            _metrics.RecordFailure();
            await _repository.UpsertAsync(new InvoiceDocument
            {
                Id = InvoiceMapper.DeterministicId(name),
                BlobName = name,
                Status = ReviewStatus.Failed,
                ErrorMessage = ex.Message
            }, cancellationToken);
            throw;
        }
    }
}
