using System.Text.Json;
using NewsScoreApp.Models.History;

namespace NewsScoreApp.Services.History;

/// <summary>
/// Persists completed assessments locally so a trend chart can be shown across sessions/app
/// restarts, without any server/EPJ integration - one plain JSON file per recipe under
/// <see cref="FileSystem.AppDataDirectory"/> (not Documents, which is user-visible via Files app;
/// this is internal app data instead, matching how transient/derived data should be stored).
/// Deliberately not a database (SQLite etc.) to avoid adding a new native dependency for what is,
/// per recipe, expected to be at most a few thousand small entries - trivial to load/save whole.
/// </summary>
public sealed class AssessmentHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Hard cap per recipe so a very long-lived install doesn't grow this file
    /// unboundedly - oldest entries are dropped first once exceeded. Generous enough to cover
    /// months of frequent (e.g. hourly) measurements for a single recipe.</summary>
    private const int MaxEntriesPerRecipe = 2000;

    private string PathFor(string recipeId) =>
        Path.Combine(FileSystem.AppDataDirectory, "history", $"{recipeId}.json");

    public async Task<List<AssessmentHistoryEntry>> LoadAsync(string recipeId)
    {
        var path = PathFor(recipeId);
        if (!File.Exists(path)) return new List<AssessmentHistoryEntry>();

        try
        {
            await using var stream = File.OpenRead(path);
            var file = await JsonSerializer.DeserializeAsync<AssessmentHistoryFile>(stream, JsonOptions).ConfigureAwait(false);
            return file?.Entries ?? new List<AssessmentHistoryEntry>();
        }
        catch
        {
            // Corrupt/unreadable history file should never crash the app or block new
            // submissions from being recorded - treat as "no history yet" and move on.
            return new List<AssessmentHistoryEntry>();
        }
    }

    public async Task AppendAsync(AssessmentHistoryEntry entry)
    {
        var entries = await LoadAsync(entry.RecipeId).ConfigureAwait(false);
        entries.Add(entry);
        entries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        if (entries.Count > MaxEntriesPerRecipe)
            entries.RemoveRange(0, entries.Count - MaxEntriesPerRecipe);

        var path = PathFor(entry.RecipeId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var file = new AssessmentHistoryFile { Entries = entries };
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, file, JsonOptions).ConfigureAwait(false);
    }

    /// <summary>Clears all recorded history for one recipe (e.g. "Tøm historikk" in the UI) -
    /// intentionally per-recipe rather than global, since a clinician may want to reset one
    /// score's trend (new patient/shift) without losing another's.</summary>
    public void Clear(string recipeId)
    {
        var path = PathFor(recipeId);
        if (File.Exists(path)) File.Delete(path);
    }
}
