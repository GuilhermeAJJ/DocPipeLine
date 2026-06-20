namespace DocPipeline.Core;

/// <summary>
/// Lifecycle state of an invoice as it flows through the pipeline.
/// The whole human-in-the-loop story hinges on this enum.
/// </summary>
public enum ReviewStatus
{
    /// <summary>All critical fields scored above the threshold — posted automatically, no human needed.</summary>
    AutoApproved,

    /// <summary>At least one critical field scored below the threshold — parked in the review queue.</summary>
    PendingReview,

    /// <summary>A human validated/corrected the data and confirmed it.</summary>
    HumanApproved,

    /// <summary>Extraction failed (unreadable file, model error) — needs attention.</summary>
    Failed,

    /// <summary>A human rejected the invoice (out of scope, missing data, duplicate, etc.).</summary>
    Rejected
}
