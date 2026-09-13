using NewsScoreApp.Models.AI;
using NewsScoreApp.Models.FormEngine;

namespace NewsScoreApp.Services.AI;

/// <summary>
/// Abstraction over "something that reads free text and proposes field values for a recipe" -
/// implemented by <see cref="FakeAiExtractionService"/> (regex/keyword POC, always available,
/// instant) and <see cref="OnDeviceAiExtractionEngine"/> (real Apple Intelligence on-device LLM
/// via CrossIntelligence, only available on iOS 26+ devices that support it). Keeping this as an
/// interface means the view model and UI never need to know which one actually ran.
/// </summary>
public interface IAiExtractionEngine
{
    /// <summary>True once this engine has determined whether it can actually run (the real
    /// on-device engine needs an async capability check first; the fake one is always ready).</summary>
    bool IsAvailable { get; }

    Task<List<FieldSuggestion>> AnalyzeAsync(string text, FormRecipe recipe, CancellationToken cancellationToken = default);
}
