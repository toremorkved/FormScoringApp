using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewsScoreApp.Models.FormEngine;
using NewsScoreApp.Services.AI;

namespace NewsScoreApp.ViewModels;

/// <summary>
/// Drives the "live dictation" AI auto-fill screen: one continuous view with the transcript text
/// box and a checklist of every field in the active recipe, side by side. The clinician types or
/// pastes/dictates a note, then explicitly presses "✨ Analyser teksten" whenever they want the
/// checklist to (re)scan the current text - each field flips from "⚪ not mentioned yet" to
/// "🟢/🟡/🔴 filled" the moment it's found. Analysis is manually triggered rather than automatic
/// so the clinician stays in control of exactly when the (on-device, but not free in terms of
/// battery/inference time) model actually runs.
/// Suggestions are staged here only: the real form fields are untouched until the clinician
/// explicitly presses "Fyll inn i skjema" (commit), or discarded entirely with "Avbryt" (cancel) -
/// this gives a clear point-of-no-return instead of silently mutating the real form as you type.
/// Analysis is delegated to <see cref="IAiExtractionEngine"/> (in practice a
/// <see cref="HybridAiExtractionEngine"/>), which uses the real on-device Apple Intelligence model
/// when available and transparently falls back to the instant regex engine otherwise.
/// </summary>
public sealed partial class AiFillViewModel : ObservableObject
{
    private const string EngineModePreferenceKey = "ai_engine_mode";
    private const string AutoAnalyzePreferenceKey = "ai_auto_analyze_enabled";

    /// <summary>How many *new* words (since the last analysis) must accumulate before
    /// auto-analyze fires again. Word-count-based rather than a fixed timer so a burst of
    /// dictation doesn't retrigger on every short pause - this is the main lever for keeping
    /// Azure OpenAI call volume (and cost) down while still feeling "automatic".</summary>
    private const int AutoAnalyzeWordThreshold = 8;

    /// <summary>Extra quiet-period debounce on top of the word threshold - avoids firing
    /// mid-sentence the instant the Nth new word is typed, waiting instead for a brief natural
    /// pause (much shorter than a full sentence pause) so it still feels responsive.</summary>
    private static readonly TimeSpan AutoAnalyzeDebounce = TimeSpan.FromMilliseconds(1200);

    private readonly IAiExtractionEngine _aiEngine;
    private FormRecipe? _recipe;
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _autoAnalyzeDebounce;
    private int _wordCountAtLastAnalysis;

    /// <summary>Field ids the clinician explicitly rejected via the ✕ button - kept out of
    /// re-analysis results until the underlying text changes enough to look like a genuinely new
    /// mention, so rejecting a wrong reading doesn't just get immediately re-applied next tick.</summary>
    private readonly HashSet<string> _rejectedFieldIds = new();

    public AiFillViewModel(IAiExtractionEngine aiEngine)
    {
        _aiEngine = aiEngine;
        if (_aiEngine is HybridAiExtractionEngine hybrid)
        {
            var saved = Preferences.Default.Get(EngineModePreferenceKey, nameof(AiEngineMode.Auto));
            if (Enum.TryParse<AiEngineMode>(saved, out var mode))
                hybrid.PreferredEngine = mode;
        }
        autoAnalyzeEnabled = Preferences.Default.Get(AutoAnalyzePreferenceKey, false);
    }

    /// <summary>Raised when the clinician discards the whole session (top-right "Avbryt") -
    /// nothing staged here is applied to the real form.</summary>
    public event Action? Cancelled;

    /// <summary>Raised once, after the clinician presses "Fyll inn i skjema", once per field that
    /// currently has a staged (non-rejected) value - so the page can apply each to the real form
    /// with a short stagger and then close the modal.</summary>
    public event Action<string, string>? FieldCommitted;

    /// <summary>Raised after every staged field has been committed to the real form.</summary>
    public event Action? CommitCompleted;

    [ObservableProperty] private string inputText = string.Empty;
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private bool isAnalyzing;
    [ObservableProperty] private bool isCommitting;
    [ObservableProperty] private bool autoAnalyzeEnabled;

    partial void OnAutoAnalyzeEnabledChanged(bool value)
    {
        Preferences.Default.Set(AutoAnalyzePreferenceKey, value);
        if (value) MaybeScheduleAutoAnalyze(); // re-check immediately against current text
    }

    /// <summary>Full error text from the last failed analysis attempt (e.g. a raw exception
    /// message from the on-device model call) - kept separate from <see cref="ProgressText"/>
    /// (which is the normal "X av Y felt funnet" status) so the UI can show/style it distinctly
    /// and offer "tap to copy" for reporting back during debugging. Empty when there's no error,
    /// or once a new analysis starts/succeeds.</summary>
    [ObservableProperty] private string lastErrorText = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(LastErrorText);

    partial void OnLastErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>Copies the full last-error text to the clipboard so it can be pasted elsewhere
    /// (Slack, an issue, back to whoever's helping debug) without having to retype/screenshot a
    /// long exception message truncated on-screen.</summary>
    [RelayCommand]
    private async Task CopyError()
    {
        if (string.IsNullOrEmpty(LastErrorText)) return;
        await Clipboard.Default.SetTextAsync(LastErrorText);
        try { Microsoft.Maui.Devices.HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch { /* not supported on this device/simulator - safe to ignore */ }
    }

    public bool CanCommit => !IsCommitting && Checklist.Any(c => c.IsFilled);

    /// <summary>Shown in small print under the transcript box so the clinician always knows
    /// whether the on-device model, the cloud model, or the offline fallback is driving the
    /// checklist right now.</summary>
    public string EngineLabel => _aiEngine switch
    {
        HybridAiExtractionEngine { PreferredEngine: AiEngineMode.Regex } => "⚙️ Enkel tekstgjenkjenning",
        HybridAiExtractionEngine { PreferredEngine: AiEngineMode.OnDevice, IsRealModelAvailable: false } => "⚠️ Apple Intelligence ikke tilgjengelig",
        HybridAiExtractionEngine { PreferredEngine: AiEngineMode.OnDevice } => "✨ Apple Intelligence (on-device)",
        HybridAiExtractionEngine { PreferredEngine: AiEngineMode.Cloud, IsCloudModelAvailable: false } => "⚠️ Azure OpenAI ikke konfigurert",
        HybridAiExtractionEngine { PreferredEngine: AiEngineMode.Cloud } => "☁️ Azure OpenAI (sky)",
        HybridAiExtractionEngine { IsRealModelAvailable: true } => "✨ Apple Intelligence (on-device)",
        HybridAiExtractionEngine { IsCloudModelAvailable: true } => "☁️ Azure OpenAI (sky)",
        _ => "⚙️ Enkel tekstgjenkjenning (frakoblet)",
    };

    /// <summary>Backing list for the compact engine picker shown next to "Analyser teksten" -
    /// only populated (non-empty) when the underlying engine actually is a
    /// <see cref="HybridAiExtractionEngine"/>, since the picker only makes sense when there's a
    /// choice of tiers to pin to.</summary>
    public List<string> EngineModeOptions { get; } = new() { "Auto", "On-device", "Sky", "Regex" };

    private static AiEngineMode ToMode(int index) => index switch
    {
        1 => AiEngineMode.OnDevice,
        2 => AiEngineMode.Cloud,
        3 => AiEngineMode.Regex,
        _ => AiEngineMode.Auto,
    };

    private static int ToIndex(AiEngineMode mode) => mode switch
    {
        AiEngineMode.OnDevice => 1,
        AiEngineMode.Cloud => 2,
        AiEngineMode.Regex => 3,
        _ => 0,
    };

    /// <summary>Selected index into <see cref="EngineModeOptions"/> - bound to the compact
    /// segmented picker in the UI. Changing this immediately re-points the hybrid engine (no
    /// "apply" step) and persists the choice so it survives app restarts.</summary>
    public int SelectedEngineModeIndex
    {
        get => _aiEngine is HybridAiExtractionEngine hybrid ? ToIndex(hybrid.PreferredEngine) : 0;
        set
        {
            if (_aiEngine is not HybridAiExtractionEngine hybrid) return;
            var mode = ToMode(value);
            if (hybrid.PreferredEngine == mode) return;
            hybrid.PreferredEngine = mode;
            Preferences.Default.Set(EngineModePreferenceKey, mode.ToString());
            OnPropertyChanged();
            OnPropertyChanged(nameof(EngineLabel));
        }
    }

    public ObservableCollection<ChecklistItemViewModel> Checklist { get; } = new();

    public void Start(FormRecipe recipe)
    {
        _recipe = recipe;
        InputText = string.Empty;
        _rejectedFieldIds.Clear();
        _wordCountAtLastAnalysis = 0;
        IsCommitting = false;

        Checklist.Clear();
        foreach (var section in recipe.Sections)
        {
            var item = new ChecklistItemViewModel(section);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChecklistItemViewModel.IsFilled))
                    OnPropertyChanged(nameof(CanCommit));
            };
            Checklist.Add(item);
        }

        UpdateProgressText();
        OnPropertyChanged(nameof(EngineLabel));
    }

    /// <summary>Loads the canned sample transcript in one go, then re-analyzes - a quick way to
    /// see the whole flow without having to type/dictate anything yourself.</summary>
    [RelayCommand]
    private void UseSample()
    {
        if (_recipe is null) return;
        InputText = SampleTranscripts.ForRecipe(_recipe.Id);
    }

    /// <summary>True once there's non-empty text to analyze and no analysis is already
    /// running - used to enable/disable the manual "Analyser"/"✨ Fyll ut nå" button.</summary>
    public bool CanAnalyze => !IsAnalyzing && !string.IsNullOrWhiteSpace(InputText);

    partial void OnInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanAnalyze));
        AnalyzeCommand.NotifyCanExecuteChanged();
        if (AutoAnalyzeEnabled) MaybeScheduleAutoAnalyze();
    }

    /// <summary>Word-count-gated auto-analyze: only schedules a (debounced) analysis once at
    /// least <see cref="AutoAnalyzeWordThreshold"/> new words have appeared since the last
    /// analysis ran - not on every keystroke/pause. This is deliberately coarser than a plain
    /// typing-pause debounce specifically to bound how often the cloud engine gets called (and
    /// therefore billed) while dictation is flowing continuously: a 30-second monologue is one or
    /// two calls instead of a dozen. Manually pressing "Analyser" always resets the counter
    /// immediately regardless of this gate.</summary>
    private void MaybeScheduleAutoAnalyze()
    {
        var wordCount = CountWords(InputText);
        if (wordCount - _wordCountAtLastAnalysis < AutoAnalyzeWordThreshold) return;

        _autoAnalyzeDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _autoAnalyzeDebounce = cts;
        var token = cts.Token;
        Task.Run(async () =>
        {
            try { await Task.Delay(AutoAnalyzeDebounce, token).ConfigureAwait(false); }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!token.IsCancellationRequested && CanAnalyze) Analyze();
            });
        }, token);
    }

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Manually triggered by the clinician (button press) rather than automatically on
    /// every keystroke/pause - this is what keeps the "sensible frequency" requirement trivially
    /// satisfied: the on-device model only ever runs when explicitly asked for, never in the
    /// background while typing/dictating.</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private void Analyze()
    {
        _debounce?.Cancel();
        _autoAnalyzeDebounce?.Cancel();
        if (_recipe is null || string.IsNullOrWhiteSpace(InputText)) return;

        _wordCountAtLastAnalysis = CountWords(InputText);

        var cts = new CancellationTokenSource();
        _debounce = cts;
        var token = cts.Token;
        var recipe = _recipe;
        var text = InputText;

        IsAnalyzing = true;
        LastErrorText = string.Empty;
        OnPropertyChanged(nameof(CanAnalyze));
        AnalyzeCommand.NotifyCanExecuteChanged();
        Task.Run(async () =>
        {
            try
            {
                var found = await _aiEngine.AnalyzeAsync(text, recipe, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                MainThread.BeginInvokeOnMainThread(() => ApplyResults(found));
            }
            catch (TaskCanceledException) { /* superseded by a newer analysis request */ }
            catch (OperationCanceledException) { /* superseded by a newer analysis request */ }
            catch (Exception ex)
            {
                // TEMP (debugging): with the regex fallback bypassed, a real model failure
                // (e.g. schema-parse error) now surfaces here instead of being silently masked -
                // show the full error (wrappable, copyable) so it's obvious analysis genuinely
                // failed rather than just "found nothing", and so it can be copied for reporting.
                if (token.IsCancellationRequested) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsAnalyzing = false;
                    OnPropertyChanged(nameof(CanAnalyze));
                    AnalyzeCommand.NotifyCanExecuteChanged();
                    LastErrorText = $"⚠️ AI-feil: {ex.Message}";
                });
            }
        }, token);
    }

    private void ApplyResults(List<Models.AI.FieldSuggestion> found)
    {
        IsAnalyzing = false;
        OnPropertyChanged(nameof(CanAnalyze));
        AnalyzeCommand.NotifyCanExecuteChanged();
        var byId = found.ToDictionary(f => f.FieldId);

        foreach (var item in Checklist)
        {
            if (_rejectedFieldIds.Contains(item.Field.Id))
                continue; // clinician explicitly dismissed this one - leave it empty until reset

            if (byId.TryGetValue(item.Field.Id, out var suggestion))
                item.Apply(suggestion); // staged only - not written to the real form yet
        }

        UpdateProgressText();
    }

    private void UpdateProgressText()
    {
        int total = Checklist.Count;
        int filled = Checklist.Count(c => c.IsFilled);
        ProgressText = total == 0 ? string.Empty : $"{filled} av {total} felt funnet";
    }

    [RelayCommand]
    private void ToggleReasoning(ChecklistItemViewModel? item) => item?.ToggleReasoning();

    /// <summary>Rejects/clears a single staged suggestion - e.g. because the AI misheard
    /// something in the transcript. The field stays excluded from future re-analysis of the
    /// current text until Start() is called again (new recipe/session).</summary>
    [RelayCommand]
    private void RejectField(ChecklistItemViewModel? item)
    {
        if (item is null) return;
        _rejectedFieldIds.Add(item.Field.Id);
        item.Clear();
        UpdateProgressText();
    }

    /// <summary>Writes every currently staged (non-rejected) field into the real form, one at a
    /// time with a short stagger so the clinician can watch the form fill in, then closes the
    /// modal. This is the only point where the AI's suggestions actually touch the real form.</summary>
    [RelayCommand]
    private async Task CommitAsync()
    {
        if (IsCommitting) return;
        IsCommitting = true;
        OnPropertyChanged(nameof(CanCommit));

        foreach (var item in Checklist.Where(c => c.IsFilled))
        {
            FieldCommitted?.Invoke(item.Field.Id, item.RawValue);
            await Task.Delay(140);
        }

        CommitCompleted?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
