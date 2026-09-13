using System.Text.Json.Serialization;

namespace NewsScoreApp.Models.FormEngine;

/// <summary>
/// Root of a generic, data-driven form "recipe". One JSON file per clinical scoring/screening
/// instrument (NEWS2, qSOFA, etc). Everything a clinician-facing text says is stored here so
/// forms can be edited or added without touching app code.
/// </summary>
public sealed class FormRecipe
{
    /// <summary>Stable id used to reference this recipe, e.g. "news2".</summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Icon { get; set; } = "📋";

    /// <summary>False for document-style recipes (e.g. a structured discharge summary) that have
    /// no meaningful total/risk band - just a set of required fields to fill in. Drives whether
    /// the score summary card, the "Samlet score"/risk-band banner, the score column in the
    /// confirmation panel and the trend-chart (history) entry point are shown at all. Defaults to
    /// true so every existing scoring-recipe JSON keeps working unchanged.</summary>
    public bool IsScored { get; set; } = true;

    public List<FormSection> Sections { get; set; } = new();

    public ScoringDefinition Scoring { get; set; } = new();
}

public sealed class FormSection
{
    /// <summary>Stable field id, used for scoring lookups, navigation order and bindings.</summary>
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? Hint { get; set; }

    /// <summary>Optional longer-form clinical guidance shown when the user taps the (i) info
    /// button next to the field label (e.g. "SpO2 Skala 2 brukes kun ved kjent hyperkapnisk
    /// respirasjonssvikt..."). Kept separate from Hint, which stays a short inline reference
    /// range shown directly on the field.</summary>
    public string? Info { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FieldKind Kind { get; set; }

    // ----- Numeric fields -----
    public double? Min { get; set; }
    public double? Max { get; set; }
    public bool IsDecimal { get; set; }

    /// <summary>Score thresholds, evaluated top to bottom; first matching band wins.
    /// A band matches when value is within [MinValue, MaxValue] (either may be null = unbounded).</summary>
    public List<ScoreBand> ScoreBands { get; set; } = new();

    // ----- Single-choice fields -----
    public List<ChoiceOption> Options { get; set; } = new();

    // ----- Free-text fields -----
    /// <summary>Greyed-out placeholder shown inside an empty multi-line Text field (e.g.
    /// "Beskriv innleggelsesårsak og hovedfunn..."). Unused by Numeric/SingleChoice fields.</summary>
    public string? Placeholder { get; set; }
}

public enum FieldKind
{
    Numeric,
    SingleChoice,
    /// <summary>Free-form multi-line text - e.g. narrative sections of a structured discharge
    /// summary (epikrise). Never contributes to a recipe's total score.</summary>
    Text
}

public sealed class ScoreBand
{
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public int Score { get; set; }
}

public sealed class ChoiceOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int Score { get; set; }
}

/// <summary>Declarative scoring/risk-band rules, evaluated after all field scores are known.
/// Mirrors DIPS's NEWS2 calc-formula behaviour (sum + escalation override) but expressed as data.</summary>
public sealed class ScoringDefinition
{
    /// <summary>Risk bands ordered from lowest to highest; total score determines the band unless
    /// an override rule below promotes it further.</summary>
    public List<RiskBand> RiskBands { get; set; } = new();

    /// <summary>If true, any single field scoring the max possible band value (3 in NEWS2) forces
    /// promotion to the band named by <see cref="EscalatedBandId"/>, mirroring NEWS2's real rule.</summary>
    public bool EscalateOnAnyMaxScore { get; set; }
    public int MaxScoreValue { get; set; } = 3;
    public string? EscalatedBandId { get; set; }
}

public sealed class RiskBand
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int MinTotal { get; set; }
    public int? MaxTotal { get; set; }
    /// <summary>Hex color used for the risk banner, e.g. "#22C55E".</summary>
    public string Color { get; set; } = "#9CA3AF";
}
