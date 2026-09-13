using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewsScoreApp.Models.FormEngine;
using NewsScoreApp.Models.History;
using NewsScoreApp.Services;
using NewsScoreApp.Services.FormEngine;
using NewsScoreApp.Services.History;

namespace NewsScoreApp.ViewModels;

/// <summary>
/// Generic replacement for the old NEWS2-only MainViewModel: drives whichever <see cref="FormRecipe"/>
/// is currently selected. All field definitions, labels, scoring bands and risk bands come from the
/// recipe JSON, so switching forms (NEWS2, qSOFA, ...) via the CurrentRecipe property re-renders the
/// entire page from different data with zero code changes. Auto-advance, scroll-to-field, keyboard
/// prev/next and the elapsed-time stopwatch behave exactly as in the original NEWS2-only app.
/// </summary>
public sealed partial class GenericFormViewModel : ObservableObject
{
    private readonly FormRecipeRepository _repository;
    private readonly GenericFormScorer _scorer;
    private readonly AppSettingsService _settings;
    private readonly AssessmentHistoryService _history;
    private readonly Dictionary<string, CancellationTokenSource> _debounceTokens = new();

    private System.Timers.Timer? _stopwatch;
    private DateTime _startedAt;
    private bool _timerStarted;
    private bool _submitted;

    public GenericFormViewModel(FormRecipeRepository repository, GenericFormScorer scorer, AppSettingsService settings, AssessmentHistoryService history)
    {
        _repository = repository;
        _scorer = scorer;
        _settings = settings;
        _history = history;
        AutoAdvanceEnabled = settings.AutoAdvanceEnabled;
        IsLightMode = settings.Theme == AppThemePreference.Light;
    }

    /// <summary>Loads every bundled recipe and selects the first one (NEWS2). Must be awaited
    /// once before the page is shown.</summary>
    public async Task InitializeAsync()
    {
        await _repository.LoadAllAsync().ConfigureAwait(false);

        AvailableRecipes.Clear();
        foreach (var recipe in _repository.Recipes)
            AvailableRecipes.Add(recipe);

        if (AvailableRecipes.Count > 0)
            CurrentRecipe = AvailableRecipes[0];
    }

    /// <summary>Raised when the view should scroll a field into view and, for numeric entries,
    /// bring up the keyboard. Null field id after landing on a choice group means "dismiss keyboard".</summary>
    public event Action<string>? ScrollAndFocusRequested;

    /// <summary>Raised when the user taps a field's (i) info button; carries the clinical
    /// guidance text (never null when raised, since fields without Info don't show the button).</summary>
    public event Action<string, string>? InfoRequested;

    // ----- Recipe selection -----

    public ObservableCollection<FormRecipe> AvailableRecipes { get; } = new();

    [ObservableProperty] private FormRecipe? currentRecipe;

    /// <summary>Fires whenever CurrentRecipe changes - whether set from code (InitializeAsync) or
    /// via the Picker's two-way SelectedItem binding - and rebuilds the Fields collection to match
    /// the newly selected recipe. This is the single source of truth for "switching forms";
    /// previously only a dedicated SelectRecipeCommand did this, which the Picker never invoked.</summary>
    partial void OnCurrentRecipeChanged(FormRecipe? value)
    {
        if (value is null) return;

        _submitted = false;
        _timerStarted = false;
        _stopwatch?.Stop();
        _stopwatch = null;
        ElapsedSeconds = 0;
        ElapsedDisplay = string.Empty;
        IsSubmitted = false;

        Fields.Clear();
        foreach (var section in value.Sections)
        {
            var field = new GenericFieldViewModel(section);
            field.ValueChanged += OnFieldValueChanged;
            field.InfoRequested += f => InfoRequested?.Invoke(f.Label, f.Info ?? string.Empty);
            Fields.Add(field);
        }

        for (int i = 0; i < Fields.Count; i++)
        {
            var field = Fields[i];
            field.HasPrevious = i > 0;
            field.HasNext = true; // last field's "next" still works: it jumps to the score summary and dismisses the keyboard
            int index = i;
            field.PreviousCommand = new RelayCommand(() =>
            {
                if (index > 0) ScrollAndFocusRequested?.Invoke(Fields[index - 1].Definition.Id);
            });
            field.NextCommand = new RelayCommand(() =>
            {
                if (index < Fields.Count - 1) ScrollAndFocusRequested?.Invoke(Fields[index + 1].Definition.Id);
                else ScrollAndFocusRequested?.Invoke(ScoreSummaryFieldId);
            });
        }

        UpdateDateTimeLabels();
        Recalculate();
    }

    public ObservableCollection<GenericFieldViewModel> Fields { get; } = new();

    // ----- Header toggles -----

    [ObservableProperty] private bool autoAdvanceEnabled;
    [ObservableProperty] private bool isLightMode;

    partial void OnAutoAdvanceEnabledChanged(bool value) => _settings.AutoAdvanceEnabled = value;

    partial void OnIsLightModeChanged(bool value)
    {
        _settings.Theme = value ? AppThemePreference.Light : AppThemePreference.Dark;
        _settings.ApplyTheme();
    }

    // ----- Date / time -----

    [ObservableProperty] private DateTime registrationDate = DateTime.Now.Date;
    [ObservableProperty] private TimeSpan registrationTime = DateTime.Now.TimeOfDay;

    private void UpdateDateTimeLabels()
    {
        RegistrationDate = DateTime.Now.Date;
        RegistrationTime = DateTime.Now.TimeOfDay;
    }

    // ----- Field change / auto-advance -----

    private void OnFieldValueChanged(GenericFieldViewModel field)
    {
        StartTimerIfNeeded();
        Recalculate();

        bool isChoice = field.IsChoice;
        bool valid = field.IsCurrentValueValid();

        if (isChoice)
        {
            // Choice cards advance immediately (mirrors the prototype behaviour) instead of
            // debouncing, since there is no ongoing typing to wait out.
            if (valid) AdvanceFrom(field);
            return;
        }

        DebounceAutoAdvance(field, valid);
    }

    private void DebounceAutoAdvance(GenericFieldViewModel field, bool isValid)
    {
        if (_debounceTokens.TryGetValue(field.Definition.Id, out var existing))
            existing.Cancel();

        if (!AutoAdvanceEnabled || !isValid)
            return;

        var cts = new CancellationTokenSource();
        _debounceTokens[field.Definition.Id] = cts;
        var token = cts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, token);
                if (token.IsCancellationRequested) return;
                MainThread.BeginInvokeOnMainThread(() => AdvanceFrom(field));
            }
            catch (TaskCanceledException) { /* user kept typing */ }
        });
    }

    private void AdvanceFrom(GenericFieldViewModel field)
    {
        int idx = Fields.IndexOf(field);
        if (idx < 0) return;
        if (idx == Fields.Count - 1)
        {
            // Last field on the form (e.g. temperature on NEWS2) - there is no next field to
            // jump to, so land on the score summary instead and dismiss the keyboard.
            ScrollAndFocusRequested?.Invoke(ScoreSummaryFieldId);
            return;
        }
        var next = Fields[idx + 1];
        ScrollAndFocusRequested?.Invoke(next.Definition.Id);
    }

    /// <summary>Sentinel id (never a real field id) used to tell the view "scroll to the score
    /// summary card and dismiss the keyboard" after the last field on the form is completed.</summary>
    public const string ScoreSummaryFieldId = "__score_summary__";

    // ----- Keyboard toolbar prev/next are wired per-field in SelectRecipe (see PreviousCommand /
    // NextCommand on GenericFieldViewModel) since each field needs to know its own index. -----

    // ----- Timer -----

    private void StartTimerIfNeeded()
    {
        if (_timerStarted) return;
        _timerStarted = true;
        _startedAt = DateTime.Now;
        ElapsedSeconds = 0;
        ElapsedDisplay = "0 sek";
        _stopwatch = new System.Timers.Timer(1000) { AutoReset = true };
        _stopwatch.Elapsed += (_, _) =>
        {
            var seconds = (int)(DateTime.Now - _startedAt).TotalSeconds;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ElapsedSeconds = seconds;
                ElapsedDisplay = $"{seconds} sek";
            });
        };
        _stopwatch.Start();
    }

    private void StopTimer() => _stopwatch?.Stop();

    [ObservableProperty] private int elapsedSeconds;
    [ObservableProperty] private string elapsedDisplay = string.Empty;
    [ObservableProperty] private bool isTimerRunning;

    partial void OnElapsedSecondsChanged(int value) => IsTimerRunning = _timerStarted && !_submitted;

    // ----- Score -----

    [ObservableProperty] private FormAssessmentResult result = new();
    [ObservableProperty] private int missingFieldCount;
    [ObservableProperty] private bool canSubmit;
    [ObservableProperty] private string submitButtonText = "Godkjenn";

    private void Recalculate()
    {
        if (CurrentRecipe is null) return;

        Result = _scorer.Evaluate(CurrentRecipe, Fields.Select(f => f.State).ToList());
        MissingFieldCount = Fields.Count(f => !f.State.HasValue);
        CanSubmit = Result.IsComplete && !_submitted;
        SubmitButtonText = MissingFieldCount == 0 ? "Godkjenn" : $"Godkjenn ({MissingFieldCount} felt mangler)";

        // Utfyllingstiden måler tiden det tar å fylle ut skjemaet, ikke tiden frem til man trykker
        // Godkjenn - stopp klokken så snart alle obligatoriske felt er gyldig utfylt.
        if (Result.IsComplete && _timerStarted && !_submitted)
        {
            StopTimer();
            IsTimerRunning = false;
        }
    }

    // ----- Submit / reset -----

    [ObservableProperty] private bool isSubmitted;
    [ObservableProperty] private string submittedTimestamp = string.Empty;

    [RelayCommand]
    private void Submit()
    {
        GenericFieldViewModel? firstInvalid = null;
        foreach (var field in Fields)
        {
            bool ok = field.Validate();
            if (!ok && firstInvalid is null) firstInvalid = field;
        }

        if (firstInvalid is not null)
        {
            ScrollAndFocusRequested?.Invoke(firstInvalid.Definition.Id);
            return;
        }

        if (!Result.IsComplete) return;
        _submitted = true;
        StopTimer();
        IsSubmitted = true;
        IsTimerRunning = false;
        CanSubmit = false;
        SubmittedTimestamp = DateTime.Now.ToString("d. MMMM yyyy 'kl.' HH:mm", new CultureInfo("nb-NO"));
        ScrollAndFocusRequested?.Invoke(ConfirmationFieldId);

        RecordHistoryEntry();
    }

    /// <summary>Saves this completed assessment to local history (see
    /// <see cref="AssessmentHistoryService"/>) so it shows up in the trend chart - fire-and-forget
    /// since this is a best-effort local write that must never block or fail the submit flow the
    /// clinician is already looking at a confirmed result for.</summary>
    private void RecordHistoryEntry()
    {
        if (CurrentRecipe is null || Result.TotalScore is null) return;

        var entry = new AssessmentHistoryEntry
        {
            RecipeId = CurrentRecipe.Id,
            Timestamp = DateTime.Now,
            TotalScore = Result.TotalScore.Value,
            RiskBandLabel = Result.RiskBand?.Label ?? string.Empty,
            RiskBandColor = Result.RiskBand?.Color ?? "#9CA3AF",
            FieldValues = Fields.ToDictionary(
                f => f.Definition.Id,
                f => f.Definition.Kind == FieldKind.SingleChoice
                    ? (f.State.SelectedOptionValue ?? string.Empty)
                    : f.State.TextValue),
        };

        _ = _history.AppendAsync(entry);
    }

    /// <summary>Sentinel id used to tell the view "scroll to the 'Lagret i journal' confirmation
    /// card" after a successful submit, so the user sees the full result without manual scrolling.</summary>
    public const string ConfirmationFieldId = "__confirmation__";

    // ----- AI auto-fill (POC) -----

    /// <summary>Writes an AI-suggested value into the given field, exactly as manual entry would,
    /// but without triggering auto-advance scrolling (the AI review flow fills many fields in a
    /// staggered animation, not one at a time by user intent, so jumping the scroll position for
    /// every fill would fight the animation instead of supporting it).</summary>
    public void ApplyAiSuggestion(string fieldId, string value)
    {
        var field = Fields.FirstOrDefault(f => f.Definition.Id == fieldId);
        field?.ApplyAiSuggestion(value);
    }

    /// <summary>Clears a single field back to empty - used when the clinician rejects an AI
    /// suggestion in the live checklist (e.g. it misheard/misread the transcript) before it ever
    /// reaches the "Godkjenn" step.</summary>
    public void ClearAiSuggestion(string fieldId)
    {
        var field = Fields.FirstOrDefault(f => f.Definition.Id == fieldId);
        field?.Reset();
    }

    [RelayCommand]
    private void NewRegistration()
    {
        _submitted = false;
        _timerStarted = false;
        _stopwatch?.Stop();
        _stopwatch = null;

        foreach (var field in Fields) field.Reset();

        ElapsedSeconds = 0;
        ElapsedDisplay = string.Empty;
        IsSubmitted = false;

        UpdateDateTimeLabels();
        Recalculate();

        if (Fields.Count > 0)
            ScrollAndFocusRequested?.Invoke(Fields[0].Definition.Id);
    }
}
