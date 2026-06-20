namespace DocPipeline.Core;

/// <summary>
/// One line of an invoice. Kept loose (nullable) because line-item extraction
/// is the least reliable part of any invoice model.
/// </summary>
public sealed class InvoiceLineItem
{
    public string? Description { get; set; }
    public double? Quantity { get; set; }
    public double? UnitPrice { get; set; }
    public double? Amount { get; set; }
}
