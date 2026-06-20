namespace DocPipeline.Core;

/// <summary>
/// A single field pulled from Document Intelligence, carrying the model's confidence
/// so the pipeline can decide whether a human needs to look at it.
/// </summary>
public sealed class ExtractedField
{
    /// <summary>The extracted text/value as surfaced by the model.</summary>
    public string? Value { get; set; }

    /// <summary>Model confidence in [0,1]. Null when the field was absent from the document.</summary>
    public float? Confidence { get; set; }

    /// <summary>True once a human has edited or confirmed this value in the review UI.</summary>
    public bool HumanVerified { get; set; }
}
