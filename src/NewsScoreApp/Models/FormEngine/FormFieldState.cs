using CommunityToolkit.Mvvm.ComponentModel;

namespace NewsScoreApp.Models.FormEngine;

/// <summary>Live per-field state for one <see cref="FormSection"/> instance while a form is
/// being filled in - drives the dynamically generated UI via data binding.</summary>
public sealed partial class FormFieldState : ObservableObject
{
    public FormSection Definition { get; }

    public FormFieldState(FormSection definition) => Definition = definition;

    [ObservableProperty] private string textValue = string.Empty;
    [ObservableProperty] private string? selectedOptionValue;
    [ObservableProperty] private int? score;
    [ObservableProperty] private bool isValid;

    public bool HasValue => Definition.Kind switch
    {
        FieldKind.Numeric or FieldKind.Text => !string.IsNullOrWhiteSpace(TextValue),
        _ => SelectedOptionValue != null
    };
}

/// <summary>Aggregate result for a completed (or in-progress) scoring pass.</summary>
public sealed class FormAssessmentResult
{
    public int? TotalScore { get; set; }
    public RiskBand? RiskBand { get; set; }
    public bool IsComplete { get; set; }
}
