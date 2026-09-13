namespace NewsScoreApp.Models.AI;

/// <summary>
/// One extracted value proposed by the (fake, POC) AI engine for a single form field, together
/// with the evidence needed to build user trust: the exact snippet of the source text it was
/// derived from, a short plain-language reasoning sentence, and a confidence tier used to render
/// a green/yellow/red dot in the review UI.
/// </summary>
public sealed class FieldSuggestion
{
    public string FieldId { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;

    /// <summary>The value to write into the field - a numeric string ("18") or a choice option
    /// value ("air"/"oxygen"/"alert" etc), matching whatever <see cref="Models.FormEngine.FormSection"/>
    /// expects for that field kind.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Human-readable version of Value for display in the review card, e.g. "Romluft"
    /// instead of the raw option value "air".</summary>
    public string DisplayValue { get; set; } = string.Empty;

    public string SourceQuote { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public AiConfidence Confidence { get; set; } = AiConfidence.Medium;
}

public enum AiConfidence
{
    High,
    Medium,
    Low
}
