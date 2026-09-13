using NewsScoreApp.Models.FormEngine;
using NewsScoreApp.ViewModels;

namespace NewsScoreApp.Views;

/// <summary>
/// "Live dictation" AI auto-fill screen: a single continuous view combining the transcript text
/// box with a checklist of every field in the recipe, so a clinician can see - while actually
/// talking to a patient - which observations are still missing. Suggestions found in the growing
/// transcript are staged in the checklist only; they are written to the real, live
/// <see cref="GenericFormViewModel"/> underneath only once the clinician presses "Fyll inn i
/// skjema" (commit) - "Avbryt" (cancel) discards everything without touching the real form.
/// </summary>
public partial class AiFillPage : ContentPage
{
    private readonly AiFillViewModel _viewModel;
    private readonly GenericFormViewModel _formViewModel;

    public AiFillPage(AiFillViewModel viewModel, GenericFormViewModel formViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _formViewModel = formViewModel;
        BindingContext = viewModel;
    }

    public void Prepare(FormRecipe recipe)
    {
        _viewModel.Start(recipe);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.FieldCommitted += OnFieldCommitted;
        _viewModel.CommitCompleted += OnCommitCompleted;
        _viewModel.Cancelled += OnCancelled;
    }

    protected override void OnDisappearing()
    {
        _viewModel.FieldCommitted -= OnFieldCommitted;
        _viewModel.CommitCompleted -= OnCommitCompleted;
        _viewModel.Cancelled -= OnCancelled;
        base.OnDisappearing();
    }

    private async void OnEngineLabelTapped(object? sender, TappedEventArgs e)
    {
        var options = _viewModel.EngineModeOptions.ToArray();
        var choice = await DisplayActionSheetAsync("Velg AI-motor", "Avbryt", null, options);
        if (string.IsNullOrEmpty(choice) || choice == "Avbryt") return;
        var index = Array.IndexOf(options, choice);
        if (index >= 0)
            _viewModel.SelectedEngineModeIndex = index;
    }

    private void OnFieldCommitted(string fieldId, string value)
    {
        _formViewModel.ApplyAiSuggestion(fieldId, value);
        try { Microsoft.Maui.Devices.HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch { /* not supported on this device/simulator - safe to ignore */ }
    }

    private async void OnCommitCompleted()
    {
        await Navigation.PopModalAsync();
    }

    private async void OnCancelled()
    {
        await Navigation.PopModalAsync();
    }
}
