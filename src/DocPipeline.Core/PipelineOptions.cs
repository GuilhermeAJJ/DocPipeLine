namespace DocPipeline.Core;

/// <summary>
/// Tunable knobs for the ingestion pipeline, bound from configuration.
/// Exposing the threshold as config (not a magic number) is the whole point:
/// the business can trade automation rate against risk without a redeploy.
/// </summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>
    /// Critical fields scoring below this confidence route the invoice to human review.
    /// 0.80 is a sensible starting point for prebuilt-invoice; tune from telemetry.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.80;

    public string CosmosDatabaseName { get; set; } = "docpipeline";
    public string CosmosContainerName { get; set; } = "invoices";
}
