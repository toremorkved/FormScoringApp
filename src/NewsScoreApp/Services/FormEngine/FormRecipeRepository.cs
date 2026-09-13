using System.Text.Json;
using NewsScoreApp.Models.FormEngine;

namespace NewsScoreApp.Services.FormEngine;

/// <summary>Loads <see cref="FormRecipe"/> JSON files bundled as Raw assets under
/// Resources/Raw/FormRecipes/*.json - one file per clinical scoring form.</summary>
public sealed class FormRecipeRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Every recipe file bundled with the app. Add a new entry here (and drop the JSON file into
    // Resources/Raw/FormRecipes/) to make a new scoring form selectable in the app.
    private static readonly string[] RecipeFileNames =
    {
        "FormRecipes/news2.json",
        "FormRecipes/qsofa.json",
        "FormRecipes/gcs.json",
        "FormRecipes/braden.json",
        "FormRecipes/nrs2002.json",
        "FormRecipes/downton.json",
        "FormRecipes/esas.json",
        "FormRecipes/nihss.json",
        "FormRecipes/epikrise.json",
    };

    private readonly List<FormRecipe> _recipes = new();

    public IReadOnlyList<FormRecipe> Recipes => _recipes;

    public async Task LoadAllAsync()
    {
        if (_recipes.Count > 0) return;

        foreach (var fileName in RecipeFileNames)
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName).ConfigureAwait(false);
            var recipe = await JsonSerializer.DeserializeAsync<FormRecipe>(stream, JsonOptions).ConfigureAwait(false);
            if (recipe != null) _recipes.Add(recipe);
        }
    }
}
