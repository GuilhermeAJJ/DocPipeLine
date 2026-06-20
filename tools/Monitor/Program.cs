using Azure.Identity;
using DocPipeline.Core;
using Microsoft.Azure.Cosmos;

// Live ops dashboard for the recording — run this in its OWN window. Polls Cosmos every few
// seconds and redraws a clean summary: counts by status + the most recent invoices with their
// AI enrichment. Read-only, auth via `az login`.
//
//   dotnet run --project tools/Monitor -- [intervalSeconds]

var intervalSeconds = args.Length > 0 && int.TryParse(args[0], out var s) ? Math.Clamp(s, 1, 60) : 3;

const string endpoint = "https://bancofaturastb.documents.azure.com:443/";
const string dbName = "docpipeline";
const string containerName = "invoices";

var client = new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase }
});
var container = client.GetContainer(dbName, containerName);

Safe(() => Console.CursorVisible = false);
Console.CancelKeyPress += (_, e) => Safe(() => Console.CursorVisible = true);

// Console.CursorVisible/Clear throw when output is redirected (e.g. an IDE terminal).
// Swallow those so the monitor still runs; in a real terminal window it renders fully.
static void Safe(Action action) { try { action(); } catch (IOException) { } }

while (true)
{
    try
    {
        var docs = await QueryAllAsync(container);
        Render(docs);
    }
    catch (Exception ex)
    {
        try { Console.Clear(); } catch (IOException) { }
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Erro ao consultar o Cosmos: {ex.Message}");
        Console.ResetColor();
    }
    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
}

static async Task<List<InvoiceDocument>> QueryAllAsync(Container container)
{
    var results = new List<InvoiceDocument>();
    using var it = container.GetItemQueryIterator<InvoiceDocument>(
        new QueryDefinition("SELECT * FROM c ORDER BY c.processedAt DESC"));
    while (it.HasMoreResults)
    {
        results.AddRange(await it.ReadNextAsync());
    }
    return results;
}

static void Render(List<InvoiceDocument> docs)
{
    try { Console.Clear(); } catch (IOException) { }
    Console.WriteLine("══════════════════════════════════════════════════════════════════════");
    Console.WriteLine($"  DocPipeline — Monitor ao vivo            {DateTime.Now:HH:mm:ss}");
    Console.WriteLine("══════════════════════════════════════════════════════════════════════");

    var byStatus = docs.GroupBy(d => d.Status).ToDictionary(g => g.Key, g => g.Count());
    int Count(ReviewStatus s) => byStatus.TryGetValue(s, out var c) ? c : 0;

    Console.Write("  Total: ");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write($"{docs.Count}".PadRight(6));
    Console.ResetColor();
    WriteChip("Auto-aprovadas", Count(ReviewStatus.AutoApproved), ConsoleColor.Green);
    WriteChip("Em revisão", Count(ReviewStatus.PendingReview), ConsoleColor.Yellow);
    WriteChip("Aprov. humano", Count(ReviewStatus.HumanApproved), ConsoleColor.Cyan);
    WriteChip("Falhas", Count(ReviewStatus.Failed), ConsoleColor.Red);
    Console.WriteLine("\n");

    Console.WriteLine("  Últimas faturas:");
    Console.WriteLine("  ──────────────────────────────────────────────────────────────────");
    Console.WriteLine($"  {"Fornecedor",-26}{"Status",-16}{"Conf.",-8}{"Cat. IA",-12}");
    Console.WriteLine("  ──────────────────────────────────────────────────────────────────");

    foreach (var d in docs.Take(12))
    {
        var vendor = Trunc(d.Enrichment?.NormalizedVendor ?? d.VendorName, 25);
        var cat = d.Enrichment?.Category ?? "—";
        Console.Write($"  {vendor,-26}");
        Console.ForegroundColor = StatusColor(d.Status);
        Console.Write($"{StatusLabel(d.Status),-16}");
        Console.ResetColor();
        Console.Write($"{d.MinConfidence,-8:P0}");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"{cat,-12}");
        Console.ResetColor();
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  Atualiza automaticamente • Ctrl+C para sair");
    Console.ResetColor();
}

static void WriteChip(string label, int n, ConsoleColor color)
{
    Console.Write("   ");
    Console.ForegroundColor = color;
    Console.Write($"● {label}: {n}");
    Console.ResetColor();
}

static ConsoleColor StatusColor(ReviewStatus s) => s switch
{
    ReviewStatus.AutoApproved => ConsoleColor.Green,
    ReviewStatus.PendingReview => ConsoleColor.Yellow,
    ReviewStatus.HumanApproved => ConsoleColor.Cyan,
    ReviewStatus.Failed => ConsoleColor.Red,
    _ => ConsoleColor.Gray
};

static string StatusLabel(ReviewStatus s) => s switch
{
    ReviewStatus.AutoApproved => "Auto-aprovada",
    ReviewStatus.PendingReview => "Em revisão",
    ReviewStatus.HumanApproved => "Aprov. humano",
    ReviewStatus.Failed => "Falha",
    _ => s.ToString()
};

static string Trunc(string v, int max) => v.Length <= max ? v : v[..(max - 1)] + "…";
