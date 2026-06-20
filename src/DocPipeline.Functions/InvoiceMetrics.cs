using System.Diagnostics.Metrics;
using DocPipeline.Core;

namespace DocPipeline.Functions;

/// <summary>
/// Custom metrics for the ingest pipeline, exported to Application Insights via
/// OpenTelemetry. These power the Phase 3 dashboards: throughput, the auto-approve
/// vs human-review split, and the confidence distribution that justifies the threshold.
/// </summary>
public sealed class InvoiceMetrics
{
    /// <summary>Meter name — must be registered with the OpenTelemetry MeterProvider.</summary>
    public const string MeterName = "DocPipeline.Ingest";

    private readonly Counter<long> _processed;
    private readonly Histogram<double> _minConfidence;

    public InvoiceMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _processed = meter.CreateCounter<long>(
            "docpipeline.invoices.processed",
            unit: "{invoice}",
            description: "Invoices processed, tagged by routing outcome (status).");
        _minConfidence = meter.CreateHistogram<double>(
            "docpipeline.invoices.min_confidence",
            unit: "{ratio}",
            description: "Lowest field confidence per invoice — the value that drives the routing decision.");
    }

    /// <summary>Record a successfully-processed invoice and its confidence.</summary>
    public void RecordProcessed(InvoiceDocument doc)
    {
        var status = new KeyValuePair<string, object?>("status", doc.Status.ToString());
        _processed.Add(1, status);
        if (doc.Status != ReviewStatus.Failed)
        {
            _minConfidence.Record(doc.MinConfidence, status);
        }
    }

    /// <summary>Record a pipeline failure (extraction or persistence threw).</summary>
    public void RecordFailure() =>
        _processed.Add(1, new KeyValuePair<string, object?>("status", nameof(ReviewStatus.Failed)));
}
