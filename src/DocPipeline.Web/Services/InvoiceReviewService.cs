using DocPipeline.Core;
using Microsoft.Azure.Cosmos;

namespace DocPipeline.Web.Services;

/// <summary>
/// Write-side (command) access to the invoices container — the human-in-the-loop
/// half of the pipeline. Reads live in <see cref="InvoiceQueryService"/>.
/// </summary>
public sealed class InvoiceReviewService
{
    private readonly Container _container;

    public InvoiceReviewService(CosmosClient client, IConfiguration config)
    {
        var db = config["Pipeline:CosmosDatabaseName"] ?? "docpipeline";
        var container = config["Pipeline:CosmosContainerName"] ?? "invoices";
        _container = client.GetContainer(db, container);
    }

    /// <summary>
    /// Persist a reviewer's corrections and flip the invoice to
    /// <see cref="ReviewStatus.HumanApproved"/>, marking the critical fields as human-verified.
    /// </summary>
    /// <param name="originalVendorName">
    /// The vendor name the document had when it was loaded. VendorName is the Cosmos
    /// partition key, which cannot be mutated in place — if the reviewer corrected it we
    /// delete the stale item from the old partition and recreate it under the new one.
    /// </param>
    public async Task ApproveAsync(InvoiceDocument doc, string originalVendorName, CancellationToken ct = default)
    {
        doc.Status = ReviewStatus.HumanApproved;
        doc.InvoiceId.HumanVerified = true;
        doc.InvoiceTotal.HumanVerified = true;
        doc.InvoiceDate.HumanVerified = true;
        doc.DueDate.HumanVerified = true;
        await SaveAsync(doc, originalVendorName, ct);
    }

    /// <summary>Reject an invoice (out of scope, missing data, duplicate…) with an optional reason.</summary>
    public async Task RejectAsync(InvoiceDocument doc, string originalVendorName, string? reason, CancellationToken ct = default)
    {
        doc.Status = ReviewStatus.Rejected;
        doc.ReviewNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await SaveAsync(doc, originalVendorName, ct);
    }

    private async Task SaveAsync(InvoiceDocument doc, string originalVendorName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doc.VendorName))
        {
            doc.VendorName = "unknown"; // never write an empty partition key
        }

        var vendorChanged = !string.Equals(originalVendorName, doc.VendorName, StringComparison.Ordinal);
        if (vendorChanged && !string.IsNullOrWhiteSpace(originalVendorName))
        {
            // Partition key changed → the old item must be removed from its old partition.
            await _container.DeleteItemAsync<InvoiceDocument>(
                doc.Id, new PartitionKey(originalVendorName), cancellationToken: ct);
        }

        await _container.UpsertItemAsync(doc, new PartitionKey(doc.VendorName), cancellationToken: ct);
    }
}
