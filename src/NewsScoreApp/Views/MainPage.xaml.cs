using Microsoft.Extensions.DependencyInjection;
using NewsScoreApp.Models;
using NewsScoreApp.ViewModels;

namespace NewsScoreApp.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<FormField, VisualElement> _sections;
    private readonly Dictionary<FormField, Entry> _entries;
    private Entry? _lastFocusedEntry;

    public MainPage() : this(MauiProgram.Services.GetRequiredService<MainViewModel>())
    {
    }

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        _sections = new Dictionary<FormField, VisualElement>
        {
            [FormField.RespiratoryRate] = SecRr,
            [FormField.OxygenSaturation] = SecSpo2,
            [FormField.OxygenSupplement] = SecO2,
            [FormField.SystolicBloodPressure] = SecBp,
            [FormField.PulseRate] = SecPulse,
            [FormField.Consciousness] = SecAcvpu,
            [FormField.Temperature] = SecTemp,
        };

        _entries = new Dictionary<FormField, Entry>
        {
            [FormField.RespiratoryRate] = EntryRr,
            [FormField.OxygenSaturation] = EntrySpo2,
            [FormField.SystolicBloodPressure] = EntryBp,
            [FormField.PulseRate] = EntryPulse,
            [FormField.Temperature] = EntryTemp,
        };

        foreach (var entry in _entries.Values)
            entry.Focused += (_, _) => _lastFocusedEntry = entry;

        viewModel.ScrollAndFocusRequested += OnScrollAndFocusRequested;
    }

    protected override void OnDisappearing()
    {
        _viewModel.ScrollAndFocusRequested -= OnScrollAndFocusRequested;
        base.OnDisappearing();
    }

    private void OnScrollAndFocusRequested(FormField field)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_sections.TryGetValue(field, out var section))
                return;

            await RootScroll.ScrollToAsync(section, ScrollToPosition.Center, animated: true);

            if (_entries.TryGetValue(field, out var entry))
            {
                entry.Focus();
            }
            else
            {
                // Landing on a choice-card group (no keyboard) - dismiss whatever was focused.
                _lastFocusedEntry?.Unfocus();
            }
        });
    }
}
