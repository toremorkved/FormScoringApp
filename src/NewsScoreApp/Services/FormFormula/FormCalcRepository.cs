using System.Text.Json;

namespace NewsScoreApp.Services.FormFormula;

/// <summary>
/// Reads DIPS's raw "form-description.json" export (bundled as a Raw asset) and extracts
/// every calcId -&gt; calc formula pair anywhere in the openEHR node tree. This is the bridge
/// that lets <see cref="NewsScoreApp.Services.NewsScorer"/> evaluate NEWS2's total score and
/// risk-band logic using DIPS's own formula strings instead of re-implemented business rules.
/// </summary>
public sealed class FormCalcRepository
{
    private readonly Dictionary<string, string> _formulas = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Formulas => _formulas;

    /// <summary>Loads and indexes calc formulas from a form-description.json Raw asset.</summary>
    public static async Task<FormCalcRepository> LoadFromRawAssetAsync(string logicalName)
    {
        var repo = new FormCalcRepository();
        // ConfigureAwait(false) throughout: this is called via GetAwaiter().GetResult() from the
        // MainViewModel constructor (on the UI thread), so continuations must NOT try to resume on
        // the captured UI SynchronizationContext or the block would deadlock the app at startup.
        await using var stream = await FileSystem.OpenAppPackageFileAsync(logicalName).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        Walk(doc.RootElement, repo._formulas);
        return repo;
    }

    private static void Walk(JsonElement node, Dictionary<string, string> formulas)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        if (node.TryGetProperty("viewConfig", out var viewConfig) &&
            viewConfig.TryGetProperty("annotations", out var annotations) &&
            annotations.ValueKind == JsonValueKind.Object)
        {
            string? calcId = annotations.TryGetProperty("calcId", out var cid) && cid.ValueKind == JsonValueKind.String
                ? cid.GetString()
                : null;
            string? calc = annotations.TryGetProperty("calc", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            if (!string.IsNullOrEmpty(calcId) && !string.IsNullOrEmpty(calc))
                formulas[calcId!] = calc!;
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                Walk(child, formulas);
        }
    }
}
