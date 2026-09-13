using NewsScoreApp.ViewModels;

namespace NewsScoreApp.Views;

/// <summary>
/// Settings screen mirroring the iOS Settings app's grouped-list style: form picker,
/// auto-advance toggle and light/dark mode toggle, previously cluttering the top of
/// GenericFormPage, now live here behind a single gear icon.
/// </summary>
public partial class SettingsPage : ContentPage
{
    public SettingsPage(GenericFormViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
