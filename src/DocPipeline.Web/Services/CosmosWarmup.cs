using Microsoft.Azure.Cosmos;

namespace DocPipeline.Web.Services;

/// <summary>
/// Warms up the Cosmos connection at startup. The account lives in West US 2 while the app
/// runs in Brazil, so the first query pays a cold cross-region round-trip (TLS + routing
/// discovery). Doing it once on boot makes the first page navigation feel instant.
/// </summary>
public sealed class CosmosWarmup : IHostedService
{
    private readonly CosmosClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<CosmosWarmup> _logger;

    public CosmosWarmup(CosmosClient client, IConfiguration config, ILogger<CosmosWarmup> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var db = _config["Pipeline:CosmosDatabaseName"] ?? "docpipeline";
            var container = _config["Pipeline:CosmosContainerName"] ?? "invoices";
            await _client.GetContainer(db, container).ReadContainerAsync(cancellationToken: cancellationToken);
            _logger.LogInformation("Cosmos connection warmed up.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cosmos warmup failed (non-fatal).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
