using CommunityToolkit.Mvvm.ComponentModel;
using NewsScoreApp.Models.AI;
using NewsScoreApp.Models.FormEngine;

namespace NewsScoreApp.ViewModels;

/// <summary>One row in the live "hva gjenstår" checklist - always one per field in the recipe
/// (regardless of whether the AI has found anything for it yet), so the clinician can see at a
/// glance what's still missing while talking to the patient.</summary>
public sealed partial class ChecklistItemViewModel : ObservableObject
{
    public FormSection Field { get; }

    [ObservableProperty] private bool isFilled;
    [ObservableProperty] private string displayValue = string.Empty;
    [ObservableProperty] private string sourceQuote = string.Empty;
    [ObservableProperty] private string reasoning = string.Empty;
    [ObservableProperty] private AiConfidence confidence = AiConfidence.Medium;
    [ObservableProperty] private bool isReasoningExpanded;

    /// <summary>Set to true for a brief moment right when a field transitions from empty to
    /// filled, so the XAML can trigger a highlight pulse via a trigger/behavior.</summary>
    [ObservableProperty] private bool justFilled;

    public ChecklistItemViewModel(FormSection field)
    {
        Field = field;
    }

    public string Label => Field.Label;

    /// <summary>For single-choice fields: a compact list of the available option labels
    /// (e.g. "Luft / Oksygen") shown under the field label so the clinician knows exactly what
    /// to ask the patient about, even before/without an AI suggestion. For numeric fields: the
    /// valid range and unit (e.g. "12\u201325 /min") so the clinician knows what a normal reading
    /// looks like without having to check elsewhere. "/" is used as the separator (rather than a
    /// comma) since it doubles as a natural "or"/range marker in both cases.</summary>
    public string OptionsHint => Field.Kind switch
    {
        FieldKind.SingleChoice when Field.Options.Count > 0 =>
            string.Join(" / ", Field.Options.Select(o => o.Label)),
        FieldKind.Numeric when Field.Min is not null && Field.Max is not null =>
            $"{FormatNumber(Field.Min.Value)}\u2013{FormatNumber(Field.Max.Value)}{(string.IsNullOrEmpty(Field.Unit) ? string.Empty : $" {Field.Unit}")}",
        _ => string.Empty,
    };

    private static string FormatNumber(double value) =>
        value == Math.Floor(value) ? ((int)value).ToString() : value.ToString("0.#");

    public bool HasOptionsHint => !string.IsNullOrEmpty(OptionsHint);

    /// <summary>The raw value to write into the real form field on commit (e.g. "91" or "oxygen") -
    /// distinct from DisplayValue, which is formatted for reading (e.g. "91 %" or "Oksygen").</summary>
    public string RawValue { get; private set; } = string.Empty;

    public string StatusIcon => IsFilled ? ConfidenceDot : "⚪";

    public string ConfidenceDot => Confidence switch
    {
        AiConfidence.High => "🟢",
        AiConfidence.Medium => "🟡",
        _ => "🔴",
    };

    public void ToggleReasoning() => IsReasoningExpanded = !IsReasoningExpanded;

    public void Apply(FieldSuggestion suggestion)
    {
        RawValue = suggestion.Value;
        DisplayValue = suggestion.DisplayValue;
        SourceQuote = $"\u201c{suggestion.SourceQuote}\u201d";
        Reasoning = suggestion.Reasoning;
        Confidence = suggestion.Confidence;
        IsFilled = true;
        OnPropertyChanged(nameof(StatusIcon));
    }

    public void Clear()
    {
        RawValue = string.Empty;
        IsFilled = false;
        DisplayValue = string.Empty;
        SourceQuote = string.Empty;
        Reasoning = string.Empty;
        IsReasoningExpanded = false;
        OnPropertyChanged(nameof(StatusIcon));
    }

    partial void OnConfidenceChanged(AiConfidence value) => OnPropertyChanged(nameof(StatusIcon));
}
