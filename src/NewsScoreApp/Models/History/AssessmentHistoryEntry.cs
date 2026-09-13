using System.Text.Json.Serialization;

namespace NewsScoreApp.Models.History;

/// <summary>One completed (submitted) assessment for a given recipe, kept purely locally on the
/// device for trend charting - never synced/uploaded anywhere. This is intentionally a flat,
/// small record (not the full form snapshot) so history stays cheap to load/render even after
/// months of frequent measurements.</summary>
public sealed class AssessmentHistoryEntry
{
    public string RecipeId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int TotalScore { get; set; }
    public string RiskBandLabel { get; set; } = string.Empty;
    public string RiskBandColor { get; set; } = "#9CA3AF";

    /// <summary>Per-field raw values at submit time (fieldId -> display value), so a future
    /// "vis detaljer" on a single history point could show what was actually entered - not
    /// currently surfaced in the chart itself, which only plots <see cref="TotalScore"/>.</summary>
    public Dictionary<string, string> FieldValues { get; set; } = new();
}

/// <summary>Serialization root - one file per recipe under AppDataDirectory/history/{recipeId}.json.</summary>
public sealed class AssessmentHistoryFile
{
    public List<AssessmentHistoryEntry> Entries { get; set; } = new();
}
