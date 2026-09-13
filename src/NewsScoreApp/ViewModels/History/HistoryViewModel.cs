using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewsScoreApp.Models.FormEngine;
using NewsScoreApp.Models.History;
using NewsScoreApp.Services.History;

namespace NewsScoreApp.ViewModels;

/// <summary>
/// Drives the trend-history screen for one recipe: loads every locally saved
/// <see cref="AssessmentHistoryEntry"/> for that recipe and exposes them both as a plain list
/// (for the "seneste målinger" table) and as normalized chart points (for the
/// <see cref="Views.ScoreTrendDrawable"/> line chart) - see that class for why total score
/// (not sub-scores) is what gets charted.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly AssessmentHistoryService _history;
    private FormRecipe? _recipe;

    public HistoryViewModel(AssessmentHistoryService history)
    {
        _history = history;
    }

    [ObservableProperty] private string recipeTitle = string.Empty;
    [ObservableProperty] private bool isEmpty = true;

    public ObservableCollection<AssessmentHistoryEntry> Entries { get; } = new();

    /// <summary>Most recent first, for the "seneste målinger" list below the chart.</summary>
    public IEnumerable<AssessmentHistoryEntry> EntriesNewestFirst => Entries.Reverse();

    public async Task LoadAsync(FormRecipe recipe)
    {
        _recipe = recipe;
        RecipeTitle = recipe.Title;

        var loaded = await _history.LoadAsync(recipe.Id).ConfigureAwait(false);
        Entries.Clear();
        foreach (var entry in loaded)
            Entries.Add(entry);

        IsEmpty = Entries.Count == 0;
        OnPropertyChanged(nameof(EntriesNewestFirst));
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        if (_recipe is null) return;
        _history.Clear(_recipe.Id);
        Entries.Clear();
        IsEmpty = true;
        OnPropertyChanged(nameof(EntriesNewestFirst));
        await Task.CompletedTask;
    }
}
