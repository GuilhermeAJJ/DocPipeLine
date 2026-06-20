using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// Synthetic invoice generator for the DocPipeline demo. Produces clean PDF invoices
// that Document Intelligence's prebuilt-invoice reads with high confidence, across
// several vendors/categories so the Phase 4 enrichment has something to categorize.
//
//   dotnet run --project tools/InvoiceGenerator -- [count] [outputDir]
//
// Defaults: one of each sample vendor, written to ./samples/generated.

QuestPDF.Settings.License = LicenseType.Community;

var outputDir = args.Length > 1
    ? args[1]
    : Path.Combine(Directory.GetCurrentDirectory(), "samples", "generated");
Directory.CreateDirectory(outputDir);

var samples = BuildSamples();
var count = args.Length > 0 && int.TryParse(args[0], out var n) ? Math.Clamp(n, 1, samples.Count) : samples.Count;

var ptBr = new CultureInfo("pt-BR");
for (var i = 0; i < count; i++)
{
    var inv = samples[i];
    if (inv.Degraded)
    {
        // Render as a low-resolution JPEG so Document Intelligence reads it with LOWER
        // confidence → routes to the human review queue (below the 0.80 threshold).
        var path = Path.Combine(outputDir, $"{inv.Number}.jpg");
        BuildDocument(inv, ptBr).GenerateImages(
            _ => path,
            new ImageGenerationSettings
            {
                ImageFormat = ImageFormat.Jpeg,
                ImageCompressionQuality = ImageCompressionQuality.VeryLow,
                RasterDpi = 40
            });
        Console.WriteLine($"~ {inv.Vendor,-40} {inv.Total.ToString("C", ptBr),14}  ->  {path}  (degradada → revisão)");
    }
    else
    {
        var path = Path.Combine(outputDir, $"{inv.Number}.pdf");
        BuildDocument(inv, ptBr).GeneratePdf(path);
        Console.WriteLine($"✓ {inv.Vendor,-40} {inv.Total.ToString("C", ptBr),14}  ->  {path}");
    }
}
Console.WriteLine($"\n{count} fatura(s) gerada(s) em {outputDir}");

// ── document layout ────────────────────────────────────────────────────────
static IDocument BuildDocument(Invoice inv, CultureInfo c) => Document.Create(doc =>
{
    doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(40);
        page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken3));

        page.Header().Column(col =>
        {
            col.Item().Text(inv.Vendor).FontSize(20).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().Text(inv.VendorAddress).FontColor(Colors.Grey.Medium);
            col.Item().Text($"CNPJ: {inv.VendorTaxId}").FontColor(Colors.Grey.Medium);
        });

        page.Content().PaddingVertical(20).Column(col =>
        {
            col.Spacing(14);

            col.Item().Text("FATURA").FontSize(16).Bold();

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(meta =>
                {
                    meta.Item().Text(t => { t.Span("Número da fatura: ").SemiBold(); t.Span(inv.Number); });
                    meta.Item().Text(t => { t.Span("Data de emissão: ").SemiBold(); t.Span(inv.IssueDate.ToString("dd/MM/yyyy", c)); });
                    meta.Item().Text(t => { t.Span("Data de vencimento: ").SemiBold(); t.Span(inv.DueDate.ToString("dd/MM/yyyy", c)); });
                });
                row.RelativeItem().Column(cust =>
                {
                    cust.Item().Text("Faturar para:").SemiBold();
                    cust.Item().Text(inv.Customer);
                    cust.Item().Text(inv.CustomerAddress).FontColor(Colors.Grey.Medium);
                });
            });

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(5);
                    c.RelativeColumn(1);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });

                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("Descrição");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Qtd");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Valor unit.");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Total");
                });

                foreach (var li in inv.Items)
                {
                    table.Cell().Element(BodyCell).Text(li.Description);
                    table.Cell().Element(BodyCell).AlignRight().Text(li.Quantity.ToString(c));
                    table.Cell().Element(BodyCell).AlignRight().Text(li.UnitPrice.ToString("C", c));
                    table.Cell().Element(BodyCell).AlignRight().Text((li.Quantity * li.UnitPrice).ToString("C", c));
                }
            });

            col.Item().AlignRight().Column(totals =>
            {
                totals.Item().Text($"Subtotal: {inv.Subtotal.ToString("C", c)}");
                totals.Item().Text($"Impostos (10%): {inv.Tax.ToString("C", c)}");
                totals.Item().Text($"Total: {inv.Total.ToString("C", c)}").FontSize(13).Bold();
            });
        });

        page.Footer().AlignCenter().Text("Documento sintético gerado para demonstração — DocPipeline")
            .FontSize(8).FontColor(Colors.Grey.Medium);
    });

    static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).PaddingVertical(5).PaddingHorizontal(6).DefaultTextStyle(t => t.SemiBold());
    static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(6);
});

// ── sample data ────────────────────────────────────────────────────────────
static List<Invoice> BuildSamples()
{
    var issue = new DateOnly(2026, 6, 1);
    return new List<Invoice>
    {
        new("INV-2026-0001", "TechNova Soluções em TI Ltda", "Av. Paulista, 1000 - São Paulo/SP", "12.345.678/0001-90",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue,
            new[]
            {
                new Line("Licença anual ERP - 10 usuários", 10, 480.00m),
                new Line("Suporte técnico mensal", 12, 350.00m),
            }),
        new("INV-2026-0002", "Mídia Plena Publicidade Ltda", "Rua da Propaganda, 200 - Rio de Janeiro/RJ", "98.765.432/0001-10",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(2),
            new[]
            {
                new Line("Campanha de mídia social - junho", 1, 8500.00m),
                new Line("Produção de vídeo institucional", 1, 6200.00m),
            }),
        new("INV-2026-0003", "TransLog Transportes e Logística Ltda", "Rod. dos Bandeirantes, km 90 - Jundiaí/SP", "11.222.333/0001-44",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(3),
            new[]
            {
                new Line("Frete dedicado - rota SP/MG", 4, 1250.00m),
                new Line("Armazenagem - 30 dias", 1, 2200.00m),
            }),
        new("INV-2026-0004", "Suprimentos Alfa Materiais de Construção", "Av. Industrial, 4500 - Contagem/MG", "44.555.666/0001-77",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(5),
            new[]
            {
                new Line("Cimento CP-II 50kg (saco)", 200, 32.90m),
                new Line("Vergalhão CA-50 12mm (barra)", 150, 58.40m),
                new Line("Areia média (m³)", 30, 95.00m),
            }),
        new("INV-2026-0005", "Consultoria Beta Serviços Empresariais", "Setor Comercial Sul, Bloco B - Brasília/DF", "77.888.999/0001-22",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(7),
            new[]
            {
                new Line("Consultoria tributária - escopo fase 1", 1, 12000.00m),
                new Line("Relatório de compliance", 1, 3800.00m),
            }),
        new("INV-2026-0006", "CloudHost Serviços de Nuvem Ltda", "Av. das Nações, 1500 - São Paulo/SP", "10.101.202/0001-30",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(8),
            new[]
            {
                new Line("Hospedagem em nuvem - plano enterprise", 1, 4500.00m),
                new Line("Backup gerenciado - 1 TB", 1, 900.00m),
            }),
        new("INV-2026-0007", "Papelaria Central Suprimentos", "Rua do Comércio, 320 - Belo Horizonte/MG", "20.202.303/0001-41",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(9),
            new[]
            {
                new Line("Resma de papel A4 (caixa)", 40, 28.50m),
                new Line("Cartucho de toner", 12, 215.00m),
            }),
        new("INV-2026-0008", "Gráfica Veloz Impressão Digital", "Av. Tipografia, 77 - Curitiba/PR", "30.303.404/0001-52",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(10),
            new[]
            {
                new Line("Folder institucional - tiragem 5000", 1, 3200.00m),
                new Line("Banner lona 3x2m", 4, 180.00m),
            }),
        new("INV-2026-0009", "Vigilância Sentinela Segurança Ltda", "Rua da Guarda, 12 - Recife/PE", "40.404.505/0001-63",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(11),
            new[]
            {
                new Line("Vigilância patrimonial - posto 24h", 1, 9800.00m),
            }),
        new("INV-2026-0010", "ElétricaMax Materiais Elétricos", "Av. Energia, 890 - Porto Alegre/RS", "50.505.606/0001-74",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(12),
            new[]
            {
                new Line("Cabo flexível 2,5mm (rolo 100m)", 25, 189.00m),
                new Line("Disjuntor tripolar 63A", 18, 92.50m),
            }),
        new("INV-2026-0011", "AeroFrete Logística Expressa", "Aeroporto de Viracopos - Campinas/SP", "60.606.707/0001-85",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(13),
            new[]
            {
                new Line("Frete aéreo expresso - lote", 3, 2750.00m),
            }),
        new("INV-2026-0012", "InfoData Consultoria em TI", "Rua dos Dados, 404 - Florianópolis/SC", "70.707.808/0001-96",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(14),
            new[]
            {
                new Line("Migração de banco de dados", 1, 15000.00m),
                new Line("Treinamento equipe - 16h", 1, 4200.00m),
            }),
        new("INV-2026-0013", "Construflex Materiais de Construção", "Av. Cimento, 1200 - Goiânia/GO", "80.808.909/0001-07",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(15),
            new[]
            {
                new Line("Bloco de concreto (milheiro)", 5, 1850.00m),
                new Line("Argamassa AC-III (saco 20kg)", 120, 24.90m),
            }),
        new("INV-2026-0014", "Mídia Norte Publicidade e Eventos", "Av. Amazonas, 33 - Manaus/AM", "90.909.010/0001-18",
            "Construtora Horizonte S.A.", "Rua das Obras, 50 - Campinas/SP", issue.AddDays(16),
            new[]
            {
                new Line("Ação promocional - ponto de venda", 1, 7600.00m),
                new Line("Brindes personalizados", 500, 12.80m),
            }),
    };
}

// ── records ────────────────────────────────────────────────────────────────
sealed record Line(string Description, decimal Quantity, decimal UnitPrice);

sealed record Invoice(
    string Number, string Vendor, string VendorAddress, string VendorTaxId,
    string Customer, string CustomerAddress, DateOnly IssueDate, Line[] Items,
    bool Degraded = false)
{
    public DateOnly DueDate => IssueDate.AddDays(30);
    public decimal Subtotal => Items.Sum(i => i.Quantity * i.UnitPrice);
    public decimal Tax => Math.Round(Subtotal * 0.10m, 2);
    public decimal Total => Subtotal + Tax;
}
