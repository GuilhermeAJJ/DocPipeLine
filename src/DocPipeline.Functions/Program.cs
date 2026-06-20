using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using DocPipeline.Core;
using DocPipeline.Functions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.AI.DocumentIntelligence;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Observability (Phase 3): only wires up when the connection string is present,
// so local runs stay quiet but Azure runs export traces/metrics to App Insights.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .WithMetrics(m => m.AddMeter(InvoiceMetrics.MeterName)) // export our custom pipeline metrics
        .UseAzureMonitorExporter();
}

// IMeterFactory + custom metrics (Phase 3 dashboards). AddMetrics is safe to call
// even when the exporter above is off — locally the meters just have no consumer.
builder.Services.AddMetrics();
builder.Services.AddSingleton<InvoiceMetrics>();

var config = builder.Configuration;

// Strongly-typed pipeline knobs (threshold + db/container names) bound from config.
builder.Services.Configure<PipelineOptions>(config.GetSection(PipelineOptions.SectionName));

// One shared credential: Managed Identity in Azure, your `az login` locally. No keys, ever.
var credential = new DefaultAzureCredential();
builder.Services.AddSingleton<Azure.Core.TokenCredential>(credential); // reused by the enricher

// Document Intelligence — endpoint only, auth via Entra ID.
builder.Services.AddSingleton(_ =>
{
    var endpoint = config["DocumentIntelligence:Endpoint"]
        ?? throw new InvalidOperationException("Missing config: DocumentIntelligence:Endpoint");
    return new DocumentIntelligenceClient(new Uri(endpoint), credential);
});

// Cosmos — data-plane RBAC. The account/db/container are provisioned by Bicep, never created in code.
builder.Services.AddSingleton(_ =>
{
    var endpoint = config["Cosmos:Endpoint"]
        ?? throw new InvalidOperationException("Missing config: Cosmos:Endpoint");
    return new CosmosClient(endpoint, credential, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });
});

builder.Services.AddSingleton<InvoiceRepository>();
builder.Services.AddSingleton<InvoiceEnricher>(); // Phase 4 — Azure OpenAI enrichment

builder.Build().Run();
