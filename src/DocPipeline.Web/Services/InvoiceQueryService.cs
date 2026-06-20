using DocPipeline.Core;
using Microsoft.Azure.Cosmos;

namespace DocPipeline.Web.Services;

/// <summary>
/// Read-side access to the invoices container. The review UI lives off these queries.
/// </summary>
public sealed class InvoiceQueryService
{
    private readonly Container _container;

    public InvoiceQueryService(CosmosClient client, IConfiguration config)
    {
        var db = config["Pipeline:CosmosDatabaseName"] ?? "docpipeline";
        var container = config["Pipeline:CosmosContainerName"] ?? "invoices";
        _container = client.GetContainer(db, container);
    }

    /// <summary>
    /// Status is stored as its integer enum value (Cosmos' default serializer), so we
    /// query by the int to stay consistent with how the function writes it.
    /// </summary>
    public async Task<IReadOnlyList<InvoiceDocument>> GetByStatusAsync(ReviewStatus status, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.status = @status ORDER BY c.processedAt DESC")
            .WithParameter("@status", (int)status);

        return await RunAsync(query, ct);
    }

    /// <summary>All invoices, newest first — backs the "processed invoices" dashboard.</summary>
    public async Task<IReadOnlyList<InvoiceDocument>> GetAllAsync(CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.processedAt DESC");
        return await RunAsync(query, ct);
    }

    private async Task<IReadOnlyList<InvoiceDocument>> RunAsync(QueryDefinition query, CancellationToken ct)
    {
        var results = new List<InvoiceDocument>();
        using var iterator = _container.GetItemQueryIterator<InvoiceDocument>(query);
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(ct));
        }
        return results;
    }
}
