using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewsScoreApp.Models;
using NewsScoreApp.Services;

namespace NewsScoreApp.ViewModels;

/// <summary>
/// Drives the entire registration form. Recomputes the NEWS2 score live as fields change,
/// manages the elapsed-time stopwatch, and raises <see cref="ScrollAndFocusRequested"/> so the
/// view can auto-scroll + auto-focus the next control (Entry keyboard or choice-card section).
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly FormField[] LinearOrder =
    {
        FormField.RespiratoryRate,
        FormField.OxygenSaturation,
        FormField.OxygenSupplement,
        FormField.SystolicBloodPressure,
        FormField.PulseRate,
        FormField.Consciousness,
        FormField.Temperature
    };

    private readonly NewsScorer _scorer;
    private readonly AppSettingsService _settings;
    private readonly VitalSignsInput _input = new();
    private readonly Dictionary<FormField, CancellationTokenSource> _debounceTokens = new();

    private System.Timers.Timer? _stopwatch;
    private DateTime _startedAt;
    private bool _timerStarted;
    private bool _submitted;

    public MainViewModel(NewsScorer scorer, AppSettingsService settings)
    {
        _scorer = scorer;
        _settings = settings;
        AutoAdvanceEnabled = settings.AutoAdvanceEnabled;
        IsLightMode = settings.Theme == AppThemePreference.Light;
        // Loads DIPS's calc formulas (form-description.json) once, synchronously, before the
        // first score calculation - the file is tiny (~90KB) so this is effectively instant.
        _scorer.InitializeAsync().GetAwaiter().GetResult();
        RecalculateScore();
        UpdateDateTimeLabels();
    }

    /// <summary>Raised when the view should scroll a field into view and, for text entries,
    /// bring up the keyboard. Null means "dismiss keyboard only" (used before showing a
    /// choice-card group that has no keyboard).</summary>
    public event Action<FormField>? ScrollAndFocusRequested;

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

    partial void OnRegistrationDateChanged(DateTime value) => _input.Timestamp = value.Date + RegistrationTime;
    partial void OnRegistrationTimeChanged(TimeSpan value) => _input.Timestamp = RegistrationDate.Date + value;

    private void UpdateDateTimeLabels()
    {
        RegistrationDate = _input.Timestamp.Date;
        RegistrationTime = _input.Timestamp.TimeOfDay;
    }

    // ----- Vital sign text fields -----

    [ObservableProperty] private string respiratoryRateText = string.Empty;
    [ObservableProperty] private string oxygenSaturationText = string.Empty;
    [ObservableProperty] private string systolicBpText = string.Empty;
    [ObservableProperty] private string pulseText = string.Empty;
    [ObservableProperty] private string temperatureText = string.Empty;

    [ObservableProperty] private OxygenSupplement? selectedOxygenSupplement;
    [ObservableProperty] private Consciousness? selectedConsciousness;

    partial void OnRespiratoryRateTextChanged(string value) =>
        HandleNumericChange(FormField.RespiratoryRate, value, v => _input.RespiratoryRate = v, 8, 60);

    partial void OnOxygenSaturationTextChanged(string value) =>
        HandleNumericChange(FormField.OxygenSaturation, value, v => _input.OxygenSaturation = v, 50, 100);

    partial void OnSystolicBpTextChanged(string value) =>
        HandleNumericChange(FormField.SystolicBloodPressure, value, v => _input.SystolicBloodPressure = v, 40, 300);

    partial void OnPulseTextChanged(string value) =>
        HandleNumericChange(FormField.PulseRate, value, v => _input.PulseRate = v, 20, 250);

    partial void OnTemperatureTextChanged(string value)
    {
        StartTimerIfNeeded(value);
        _input.Temperature = double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        RecalculateScore();
        DebounceAutoAdvance(FormField.Temperature, value, isValid: _input.Temperature is >= 25 and <= 43);
    }

    private void HandleNumericChange(FormField field, string text, Action<int?> assign, int min, int max)
    {
        StartTimerIfNeeded(text);
        bool ok = int.TryParse(text, out var n);
        assign(ok ? n : null);
        RecalculateScore();
        DebounceAutoAdvance(field, text, isValid: ok && n >= min && n <= max);
    }

    private void StartTimerIfNeeded(string newText)
    {
        if (_timerStarted || string.IsNullOrEmpty(newText)) return;
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

    // ----- Debounced auto-advance (mirrors the prototype: pause briefly after a valid value,
    // then jump to the next field and scroll it into view) -----

    private void DebounceAutoAdvance(FormField field, string currentText, bool isValid)
    {
        if (_debounceTokens.TryGetValue(field, out var existing))
            existing.Cancel();

        if (!AutoAdvanceEnabled || !isValid)
            return;

        var cts = new CancellationTokenSource();
        _debounceTokens[field] = cts;
        var token = cts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(550, token);
                if (token.IsCancellationRequested) return;
                MainThread.BeginInvokeOnMainThread(() => AdvanceFrom(field));
            }
            catch (TaskCanceledException) { /* user kept typing */ }
        });
    }

    private void AdvanceFrom(FormField field)
    {
        int idx = Array.IndexOf(LinearOrder, field);
        if (idx < 0 || idx == LinearOrder.Length - 1) return;
        var next = LinearOrder[idx + 1];
        ScrollAndFocusRequested?.Invoke(next);
    }

    // ----- Single-choice cards -----

    [RelayCommand]
    private void SelectOxygenSupplement(string value)
    {
        SelectedOxygenSupplement = value == "oxygen" ? OxygenSupplement.Oxygen : OxygenSupplement.None;
        _input.OxygenSupplement = SelectedOxygenSupplement;
        StartTimerIfNeeded("x");
        RecalculateScore();
        AdvanceFrom(FormField.OxygenSupplement);
    }

    [RelayCommand]
    private void SelectConsciousness(string value)
    {
        SelectedConsciousness = Enum.Parse<Consciousness>(value);
        _input.Consciousness = SelectedConsciousness;
        StartTimerIfNeeded("x");
        RecalculateScore();
        AdvanceFrom(FormField.Consciousness);
    }

    // ----- Keyboard toolbar prev/next (Entry fields only) -----

    [RelayCommand] private void FocusRr() => ScrollAndFocusRequested?.Invoke(FormField.RespiratoryRate);
    [RelayCommand] private void FocusSpo2() => ScrollAndFocusRequested?.Invoke(FormField.OxygenSaturation);
    [RelayCommand] private void FocusBp() => ScrollAndFocusRequested?.Invoke(FormField.SystolicBloodPressure);
    [RelayCommand] private void FocusPulse() => ScrollAndFocusRequested?.Invoke(FormField.PulseRate);
    [RelayCommand] private void FocusTemp() => ScrollAndFocusRequested?.Invoke(FormField.Temperature);

    // ----- Score -----

    [ObservableProperty] private NewsAssessmentResult result = new();
    [ObservableProperty] private int missingFieldCount = 7;
    [ObservableProperty] private bool canSubmit;
    [ObservableProperty] private string submitButtonText = "Godkjenn";

    [ObservableProperty] private int? rrScore;
    [ObservableProperty] private int? spo2Score;
    [ObservableProperty] private int? o2Score;
    [ObservableProperty] private int? bpScore;
    [ObservableProperty] private int? pulseScore;
    [ObservableProperty] private int? acvpuScore;
    [ObservableProperty] private int? tempScore;

    private void RecalculateScore()
    {
        Result = _scorer.Evaluate(_input);
        MissingFieldCount = _input.MissingFieldCount();
        CanSubmit = _input.IsComplete && !_submitted;
        SubmitButtonText = MissingFieldCount == 0 ? "Godkjenn" : $"Godkjenn ({MissingFieldCount} felt mangler)";

        int? Find(string key) => Result.Parameters.FirstOrDefault(p => p.Key == key)?.Score;
        RrScore = Find("resp");
        Spo2Score = Find("spo2");
        O2Score = Find("o2");
        BpScore = Find("bt");
        PulseScore = Find("puls");
        AcvpuScore = Find("acvpu");
        TempScore = Find("temp");
    }

    // ----- Submit / reset -----

    [ObservableProperty] private bool isSubmitted;
    [ObservableProperty] private string submittedTimestamp = string.Empty;

    [RelayCommand]
    private void Submit()
    {
        if (!_input.IsComplete) return;
        _submitted = true;
        StopTimer();
        IsSubmitted = true;
        IsTimerRunning = false;
        CanSubmit = false;
        SubmittedTimestamp = DateTime.Now.ToString("d. MMMM yyyy 'kl.' HH:mm", new CultureInfo("nb-NO"));
    }

    [RelayCommand]
    private void NewRegistration()
    {
        _submitted = false;
        _timerStarted = false;
        _stopwatch?.Stop();
        _stopwatch = null;

        _input.RespiratoryRate = null;
        _input.OxygenSaturation = null;
        _input.OxygenSupplement = null;
        _input.SystolicBloodPressure = null;
        _input.PulseRate = null;
        _input.Consciousness = null;
        _input.Temperature = null;
        _input.Timestamp = DateTime.Now;

        RespiratoryRateText = string.Empty;
        OxygenSaturationText = string.Empty;
        SystolicBpText = string.Empty;
        PulseText = string.Empty;
        TemperatureText = string.Empty;
        SelectedOxygenSupplement = null;
        SelectedConsciousness = null;

        ElapsedSeconds = 0;
        ElapsedDisplay = string.Empty;
        IsSubmitted = false;

        UpdateDateTimeLabels();
        RecalculateScore();
        ScrollAndFocusRequested?.Invoke(FormField.RespiratoryRate);
    }
}
