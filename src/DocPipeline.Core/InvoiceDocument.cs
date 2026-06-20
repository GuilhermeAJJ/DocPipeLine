using System.Text.Json.Serialization;

namespace DocPipeline.Core;

/// <summary>
/// The canonical record for one processed invoice — this is what lands in Cosmos DB.
/// Partitioned by <see cref="VendorName"/> so "all invoices from supplier X" stays a cheap,
/// single-partition query (the exact question the OpenAI layer in Phase 4 will ask).
/// </summary>
public sealed class InvoiceDocument
{
    /// <summary>Cosmos item id. Lowercase "id" because that's the property Cosmos expects.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Cosmos partition key. Falls back to "unknown" when the vendor wasn't extracted.</summary>
    public string VendorName { get; set; } = "unknown";

    /// <summary>Original blob name in storage — the trail back to the source file.</summary>
    public string BlobName { get; set; } = string.Empty;

    public ReviewStatus Status { get; set; }

    /// <summary>Lowest confidence across the critical fields — this is what drives the review decision.</summary>
    public float MinConfidence { get; set; }

    // Critical fields. Each one carries its own confidence so the UI can highlight the weak spots.
    public ExtractedField InvoiceId { get; set; } = new();
    public ExtractedField InvoiceTotal { get; set; } = new();
    public ExtractedField InvoiceDate { get; set; } = new();
    public ExtractedField DueDate { get; set; } = new();

    public List<InvoiceLineItem> LineItems { get; set; } = new();

    /// <summary>LLM-generated summary/category/normalized vendor (Phase 4). Null until enriched.</summary>
    public InvoiceEnrichment? Enrichment { get; set; }

    /// <summary>If extraction failed, why — surfaced in the review queue.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Human reviewer's note — e.g. the reason an invoice was rejected.</summary>
    public string? ReviewNote { get; set; }

    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
