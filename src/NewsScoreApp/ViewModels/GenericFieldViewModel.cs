using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewsScoreApp.Models.FormEngine;
using NewsScoreApp.Services.FormEngine;

namespace NewsScoreApp.ViewModels;

/// <summary>
/// One entry in the dynamically rendered form - wraps a <see cref="FormSection"/> definition plus
/// its live <see cref="FormFieldState"/>, and exposes everything the generic XAML DataTemplates
/// need (label, unit, hint, score chips, choice buttons) purely from data. Adding a brand new
/// scoring form only requires a new JSON recipe - no new C# or XAML is needed here.
/// </summary>
public sealed partial class GenericFieldViewModel : ObservableObject
{
    public FormSection Definition { get; }
    public FormFieldState State { get; }
    public bool IsNumeric => Definition.Kind == FieldKind.Numeric;
    public bool IsChoice => Definition.Kind == FieldKind.SingleChoice;
    public bool IsText => Definition.Kind == FieldKind.Text;

    public string Label => Definition.Label;
    public string? Unit => Definition.Unit;
    public string? Hint => Definition.Hint;
    public string? Placeholder => Definition.Placeholder;
    public string? Info => Definition.Info;
    public bool HasInfo => !string.IsNullOrWhiteSpace(Definition.Info);

    public ObservableCollection<ScoreChipViewModel> ScoreChips { get; } = new();
    public ObservableCollection<OptionItemViewModel> Options { get; } = new();

    [ObservableProperty] private string textValue = string.Empty;
    [ObservableProperty] private bool isInvalid;
    [ObservableProperty] private string? validationMessage;

    public IRelayCommand<string> SelectOptionCommand { get; }
    public IRelayCommand ShowInfoCommand { get; }

    /// <summary>Raised when the user taps the (i) info button next to the field label, so the
    /// page can show the clinical guidance text as a native alert.</summary>
    public event Action<GenericFieldViewModel>? InfoRequested;

    /// <summary>Wired up by the owning <see cref="GenericFormViewModel"/> after construction so the
    /// generic XAML template can bind KeyboardNav.PreviousCommand/NextCommand per field.</summary>
    [ObservableProperty] private System.Windows.Input.ICommand? previousCommand;
    [ObservableProperty] private System.Windows.Input.ICommand? nextCommand;
    public bool HasPrevious { get; set; }
    public bool HasNext { get; set; }

    /// <summary>Raised whenever this field's value changes, so the owning form view model can
    /// recompute the total score and evaluate auto-advance.</summary>
    public event Action<GenericFieldViewModel>? ValueChanged;

    public GenericFieldViewModel(FormSection definition)
    {
        Definition = definition;
        State = new FormFieldState(definition);
        SelectOptionCommand = new RelayCommand<string>(OnOptionSelected);
        ShowInfoCommand = new RelayCommand(() => InfoRequested?.Invoke(this));

        // Distinct score values, highest first (e.g. 3,2,1,0) so chips read left-to-right like NEWS2.
        foreach (var value in definition.ScoreBands.Select(b => b.Score)
                     .Concat(definition.Options.Select(o => o.Score))
                     .Distinct()
                     .OrderByDescending(v => v))
        {
            ScoreChips.Add(new ScoreChipViewModel(value));
        }

        foreach (var option in definition.Options)
        {
            Options.Add(new OptionItemViewModel(option, State, SelectOptionCommand));
        }
    }

    partial void OnTextValueChanged(string value)
    {
        State.TextValue = value;
        if (IsNumeric) RecomputeNumericScore();
        else if (IsText) State.Score = string.IsNullOrWhiteSpace(value) ? null : 0; // never scored, just tracks "has a value"
        ClearValidation();
        ValueChanged?.Invoke(this);
    }

    /// <summary>Applies a value coming from the AI auto-fill review screen, exactly as if the
    /// user had typed it / tapped the matching choice button - reuses the same paths so scoring,
    /// validation and (if enabled) auto-advance all behave identically to manual entry.</summary>
    public void ApplyAiSuggestion(string value)
    {
        if (IsChoice) OnOptionSelected(value);
        else TextValue = value;
    }

    private void OnOptionSelected(string? value)
    {
        if (value is null) return;
        State.SelectedOptionValue = value;
        foreach (var option in Options)
            option.IsSelected = option.Option.Value == value;

        var option2 = Definition.Options.FirstOrDefault(o => o.Value == value);
        State.Score = option2?.Score;
        UpdateChips();
        ClearValidation();
        ValueChanged?.Invoke(this);
    }

    private void RecomputeNumericScore()
    {
        var normalized = TextValue.Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            foreach (var band in Definition.ScoreBands)
            {
                bool aboveMin = band.MinValue is null || value >= band.MinValue;
                bool belowMax = band.MaxValue is null || value <= band.MaxValue;
                if (aboveMin && belowMax)
                {
                    State.Score = band.Score;
                    UpdateChips();
                    return;
                }
            }
        }

        State.Score = null;
        UpdateChips();
    }

    private void UpdateChips()
    {
        foreach (var chip in ScoreChips)
            chip.IsActive = State.Score == chip.Value;
    }

    public bool IsCurrentValueValid()
    {
        if (IsChoice) return State.SelectedOptionValue != null;
        if (IsText) return false; // free-text fields never auto-advance; navigate manually via Neste/keyboard toolbar

        var normalized = TextValue.Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;
        if (Definition.Min is double min && value < min) return false;
        if (Definition.Max is double max && value > max) return false;

        // Decimal fields (e.g. body temperature) must not auto-advance until the user has
        // actually typed a decimal point plus at least one digit (e.g. "36.5"), otherwise a
        // value like "36" would jump away before the clinician can enter the decimal part.
        if (Definition.IsDecimal)
        {
            int dot = normalized.IndexOf('.');
            if (dot < 0 || dot == normalized.Length - 1) return false;
        }

        return true;
    }

    /// <summary>Runs full validation for this field (called when the user presses "Godkjenn") and
    /// sets <see cref="IsInvalid"/>/<see cref="ValidationMessage"/> so the card can render a red
    /// border and error text. Returns true when the field is valid.</summary>
    public bool Validate()
    {
        if (IsChoice)
        {
            bool ok = State.SelectedOptionValue != null;
            IsInvalid = !ok;
            ValidationMessage = ok ? null : "Velg et alternativ";
            return ok;
        }

        if (string.IsNullOrWhiteSpace(TextValue))
        {
            IsInvalid = true;
            ValidationMessage = "Dette feltet er obligatorisk";
            return false;
        }

        if (IsText)
        {
            IsInvalid = false;
            ValidationMessage = null;
            return true;
        }

        var normalized = TextValue.Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            IsInvalid = true;
            ValidationMessage = "Ugyldig verdi";
            return false;
        }

        if ((Definition.Min is double min && value < min) || (Definition.Max is double max && value > max))
        {
            IsInvalid = true;
            ValidationMessage = Definition.Min is not null && Definition.Max is not null
                ? $"Verdi må være mellom {Definition.Min} og {Definition.Max}"
                : "Verdi utenfor gyldig område";
            return false;
        }

        IsInvalid = false;
        ValidationMessage = null;
        return true;
    }

    private void ClearValidation()
    {
        if (!IsInvalid) return;
        IsInvalid = false;
        ValidationMessage = null;
    }

    public void Reset()
    {
        TextValue = string.Empty;
        State.SelectedOptionValue = null;
        State.Score = null;
        foreach (var option in Options) option.IsSelected = false;
        UpdateChips();
        IsInvalid = false;
        ValidationMessage = null;
    }
}
