using Azure.Identity;
using Azure.Storage.Blobs;
using DocPipeline.Web.Components;
using DocPipeline.Web.Services;
using Microsoft.Azure.Cosmos;

namespace DocPipeline.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Cosmos client — Managed Identity in Azure, `az login` locally. No keys.
        builder.Services.AddSingleton(_ =>
        {
            var endpoint = builder.Configuration["Cosmos:Endpoint"]
                ?? throw new InvalidOperationException("Missing config: Cosmos:Endpoint");
            return new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });
        });
        // Blob container client — used to stream the original invoice file to the reviewer.
        builder.Services.AddSingleton(_ =>
        {
            var blobUri = builder.Configuration["Storage:BlobServiceUri"]
                ?? throw new InvalidOperationException("Missing config: Storage:BlobServiceUri");
            var container = builder.Configuration["Storage:IncomingContainer"] ?? "invoices-in";
            return new BlobServiceClient(new Uri(blobUri), new DefaultAzureCredential())
                .GetBlobContainerClient(container);
        });

        builder.Services.AddScoped<InvoiceQueryService>();
        builder.Services.AddScoped<InvoiceReviewService>();
        builder.Services.AddHostedService<CosmosWarmup>(); // warm the cross-region connection on boot

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Streams an invoice file from blob storage so the review UI can display it
        // (identity-based access, no SAS/keys). Scoped to the incoming container.
        app.MapGet("/files/{name}", async (string name, BlobContainerClient container, CancellationToken ct) =>
        {
            var blob = container.GetBlobClient(name);
            if (!await blob.ExistsAsync(ct))
            {
                return Results.NotFound();
            }
            var props = await blob.GetPropertiesAsync(cancellationToken: ct);
            var contentType = string.IsNullOrEmpty(props.Value.ContentType)
                ? "application/octet-stream"
                : props.Value.ContentType;
            var stream = await blob.OpenReadAsync(cancellationToken: ct);
            return Results.File(stream, contentType);
        });

        app.Run();
    }
}
