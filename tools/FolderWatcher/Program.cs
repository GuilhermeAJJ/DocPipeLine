using Azure.Identity;
using Azure.Storage.Blobs;

// Watches a local folder and uploads any PDF dropped into it to the "invoices-in" container,
// simulating a network share that an ERP / scanner writes to. Each upload fires the Event
// Grid blob trigger → the DocPipeline ingest function. Auth is your `az login` (no keys).
//
//   dotnet run --project tools/FolderWatcher -- [watchFolder] [storageAccount] [container]
//
// Defaults: ./samples/dropzone, faturasportifolitb, invoices-in.

var watchFolder = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "samples", "dropzone");
var account = args.Length > 1 ? args[1] : "faturasportifolitb";
var container = args.Length > 2 ? args[2] : "invoices-in";

Directory.CreateDirectory(watchFolder);

var containerClient = new BlobContainerClient(
    new Uri($"https://{account}.blob.core.windows.net/{container}"),
    new DefaultAzureCredential());

Console.WriteLine("┌──────────────────────────────────────────────────────────────");
Console.WriteLine("│  DocPipeline — Watcher de pasta (simula pasta de rede)");
Console.WriteLine($"│  Observando : {watchFolder}");
Console.WriteLine($"│  Destino    : {account}/{container}");
Console.WriteLine("│  Solte arquivos .pdf na pasta para enviá-los ao pipeline.");
Console.WriteLine("│  Ctrl+C para sair.");
Console.WriteLine("└──────────────────────────────────────────────────────────────");

// Process anything already sitting in the folder at startup.
foreach (var existing in Directory.GetFiles(watchFolder, "*.pdf"))
{
    await UploadAsync(existing);
}

using var watcher = new FileSystemWatcher(watchFolder, "*.pdf")
{
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
    EnableRaisingEvents = true
};
watcher.Created += async (_, e) => await UploadAsync(e.FullPath);
watcher.Renamed += async (_, e) => await UploadAsync(e.FullPath);

await Task.Delay(Timeout.Infinite);

async Task UploadAsync(string path)
{
    var name = Path.GetFileName(path);
    try
    {
        await WaitUntilReadableAsync(path);
        await using var stream = File.OpenRead(path);
        await containerClient.GetBlobClient(name).UploadAsync(stream, overwrite: true);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] ↑ enviado: {name}");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] ✗ falha em {name}: {ex.Message}");
        Console.ResetColor();
    }
}

// FileSystemWatcher fires before the writer finishes — wait until the file is no longer locked.
static async Task WaitUntilReadableAsync(string path)
{
    for (var attempt = 0; attempt < 10; attempt++)
    {
        try
        {
            await using var s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return;
        }
        catch (IOException)
        {
            await Task.Delay(300);
        }
    }
}
