using DocPipeline.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace DocPipeline.Functions;

/// <summary>
/// Thin wrapper over the Cosmos container. The database and container themselves are
/// provisioned by Bicep — under data-plane RBAC the app's identity can read/write items
/// but cannot create containers, which is exactly the separation of concerns we want.
/// </summary>
public sealed class InvoiceRepository
{
    private readonly Container _container;

    public InvoiceRepository(CosmosClient client, IOptions<PipelineOptions> options)
    {
        var o = options.Value;
        _container = client.GetContainer(o.CosmosDatabaseName, o.CosmosContainerName);
    }

    public Task UpsertAsync(InvoiceDocument doc, CancellationToken ct = default) =>
        _container.UpsertItemAsync(doc, new PartitionKey(doc.VendorName), cancellationToken: ct);
}
