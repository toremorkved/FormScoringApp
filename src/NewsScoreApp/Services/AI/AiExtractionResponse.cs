namespace NewsScoreApp.Services.AI;

/// <summary>Structured-output shape asked of the on-device model. <see cref="Services.AI.OnDeviceAiExtractionEngine"/>
/// hand-builds a matching (and stricter, per-batch field-id-constrained) JSON Schema and passes it
/// to Apple's on-device guided generation, which constrains the model's output to match it at the
/// token level - so a plain deserialize into this shape is all that's needed, no free-text
/// parsing/repair.</summary>
public sealed class AiExtractionResponse
{
    public List<AiExtractedField> Fields { get; set; } = new();
}

public sealed class AiExtractedField
{
    /// <summary>Must exactly match one of the field ids given in the prompt (e.g. "resp", "spo2",
    /// "o2"). The model is instructed to omit fields it found no evidence for entirely, rather
    /// than guessing - so this class doesn't need a "not found" placeholder.</summary>
    public string FieldId { get; set; } = string.Empty;

    /// <summary>Raw value to store: a plain number as text for numeric fields ("18", "38.4") or
    /// one of the field's given option values for choice fields ("air", "oxygen", "alert" etc).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The short verbatim snippet of the source text this was derived from, so the
    /// clinician can verify it themselves.</summary>
    public string Quote { get; set; } = string.Empty;

    /// <summary>One short plain-language sentence explaining how the value was derived.</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>"high" | "medium" | "low" - how confident the model is that this reading is
    /// correct given the text (e.g. an exact number quote is "high"; an inferred/implied value
    /// is "low").</summary>
    public string Confidence { get; set; } = "medium";
}
