using NewsScoreApp.Models.FormEngine;
using NewsScoreApp.ViewModels;

namespace NewsScoreApp.Views;

/// <summary>
/// Trend-history screen for one recipe: a line chart of total score over time (drawn via
/// <see cref="ScoreTrendDrawable"/> on a <see cref="GraphicsView"/>) plus a plain list of past
/// measurements. Opened modally from <c>GenericFormPage</c>'s 📈 icon, same pattern as
/// <see cref="SettingsPage"/>/<see cref="AiFillPage"/>.
/// </summary>
public partial class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;
    private readonly ScoreTrendDrawable _drawable = new();

    public HistoryPage(HistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        TrendChart.Drawable = _drawable;
        _viewModel.Entries.CollectionChanged += (_, _) =>
        {
            _drawable.Entries = _viewModel.Entries.ToList();
            TrendChart.Invalidate();
        };
    }

    /// <summary>Loads history for the given recipe and refreshes the chart. Called every time the
    /// page is pushed (not just once), so newly submitted assessments show up immediately if the
    /// user re-opens the trend page without restarting the app.</summary>
    public async Task PrepareAsync(FormRecipe recipe)
    {
        await _viewModel.LoadAsync(recipe);
        _drawable.Entries = _viewModel.Entries.ToList();
        TrendChart.Invalidate();
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
