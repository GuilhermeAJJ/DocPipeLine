namespace DocPipeline.Core;

/// <summary>
/// LLM-generated context layered on top of the structured extraction (Phase 4).
/// Document Intelligence pulls the FIELDS; the LLM adds UNDERSTANDING — a human-readable
/// summary, an expense category, and a canonical vendor name. Best-effort: if the model
/// call fails, the invoice still flows through with this left null.
/// </summary>
public sealed class InvoiceEnrichment
{
    /// <summary>One-sentence summary (PT-BR) so a reviewer grasps the invoice without opening the PDF.</summary>
    public string? Summary { get; set; }

    /// <summary>Expense category (TI, Marketing, Serviços, Materiais, Logística, Outros).</summary>
    public string? Category { get; set; }

    /// <summary>Canonical vendor name — collapses "CONTOSO LTD." / "Contoso Ltda" into one entity.</summary>
    public string? NormalizedVendor { get; set; }

    /// <summary>When the enrichment ran. Null until the LLM has processed the invoice.</summary>
    public DateTimeOffset? EnrichedAt { get; set; }
}
