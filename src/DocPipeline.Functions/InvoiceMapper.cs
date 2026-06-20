using Azure.AI.DocumentIntelligence;
using DocPipeline.Core;

namespace DocPipeline.Functions;

/// <summary>
/// Maps the raw Document Intelligence result into our domain record and applies the
/// review policy. This is where "the model's confidence" becomes "a human's to-do item".
/// </summary>
internal static class InvoiceMapper
{
    // The fields we refuse to auto-post if the model is unsure about any of them.
    private static readonly string[] CriticalFields =
        { "InvoiceId", "InvoiceTotal", "InvoiceDate", "DueDate", "VendorName" };

    public static InvoiceDocument ToInvoiceDocument(AnalyzeResult result, string blobName, double threshold)
    {
        // Deterministic id from the blob name → re-processing the same file UPSERTS the same
        // document instead of creating a duplicate. Idempotency by design.
        var invoice = new InvoiceDocument { BlobName = blobName, Id = DeterministicId(blobName) };

        var doc = result.Documents.FirstOrDefault();
        if (doc is null)
        {
            invoice.Status = ReviewStatus.Failed;
            invoice.ErrorMessage = "No invoice document detected in the file.";
            return invoice;
        }

        var fields = doc.Fields;

        var vendor = ReadField(fields, "VendorName").Value;
        invoice.VendorName = string.IsNullOrWhiteSpace(vendor) ? "unknown" : vendor!;
        invoice.InvoiceId = ReadField(fields, "InvoiceId");
        invoice.InvoiceTotal = ReadField(fields, "InvoiceTotal");
        invoice.InvoiceDate = ReadField(fields, "InvoiceDate");
        invoice.DueDate = ReadField(fields, "DueDate");
        invoice.LineItems = ReadLineItems(fields);

        // Lowest confidence across the critical fields that were actually present in the doc.
        var confidences = CriticalFields
            .Select(f => fields.TryGetValue(f, out var df) ? df.Confidence : null)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .ToList();

        invoice.MinConfidence = confidences.Count > 0 ? confidences.Min() : 0f;
        invoice.Status = invoice.MinConfidence >= threshold
            ? ReviewStatus.AutoApproved
            : ReviewStatus.PendingReview;

        return invoice;
    }

    /// <summary>Stable GUID derived from the blob name (first 16 bytes of its SHA-256).</summary>
    internal static string DeterministicId(string blobName)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(blobName));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    private static ExtractedField ReadField(IReadOnlyDictionary<string, DocumentField> fields, string name)
    {
        if (!fields.TryGetValue(name, out var field))
            return new ExtractedField();

        return new ExtractedField
        {
            Value = field.Content,
            Confidence = field.Confidence
        };
    }

    // Line items are the least reliable part of any invoice model — best-effort, never throws.
    private static List<InvoiceLineItem> ReadLineItems(IReadOnlyDictionary<string, DocumentField> fields)
    {
        var items = new List<InvoiceLineItem>();
        if (!fields.TryGetValue("Items", out var itemsField) || itemsField.ValueList is null)
            return items;

        foreach (var item in itemsField.ValueList)
        {
            var obj = item.ValueDictionary;
            if (obj is null) continue;

            items.Add(new InvoiceLineItem
            {
                Description = obj.TryGetValue("Description", out var d) ? d.Content : null,
                Quantity = TryDouble(obj, "Quantity"),
                UnitPrice = TryDouble(obj, "UnitPrice"),
                Amount = TryDouble(obj, "Amount")
            });
        }
        return items;
    }

    private static double? TryDouble(IReadOnlyDictionary<string, DocumentField> obj, string key)
    {
        if (!obj.TryGetValue(key, out var f)) return null;
        try { return f.ValueDouble; }
        catch { return double.TryParse(f.Content, out var parsed) ? parsed : null; }
    }
}
