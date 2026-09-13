using NewsScoreApp.Models.AI;
using NewsScoreApp.Models.FormEngine;

namespace NewsScoreApp.Services.AI;

/// <summary>
/// Three-tier engine: tries the real on-device model first (free, private, but occasionally
/// rejects clinical text as "unsafe" under guided generation or times out - see
/// <see cref="OnDeviceAiExtractionEngine"/>), then falls back to the Azure OpenAI cloud engine
/// (reliable structured outputs, small per-call cost, needs network + configured credentials -
/// see <see cref="AzureOpenAiExtractionEngine"/>), and only falls back to the instant regex
/// engine if both real engines are unavailable or fail. Applies the "sensible frequency" rate
/// limiting that live re-analysis while typing needs to the on-device tier specifically, since
/// each on-device call still borrows the Neural Engine even though it has no monetary cost;
/// the cloud tier has its own natural throttle (only invoked when on-device isn't available/fails,
/// which is already infrequent).
///
/// <see cref="PreferredEngine"/> lets the UI pin this to a single tier instead of the automatic
/// fallback chain - handy for comparing/debugging the three engines side by side (e.g. forcing
/// "Cloud" on the simulator, where on-device is never available anyway, or forcing "Regex" to see
/// the instant offline behaviour even when the real models would otherwise run).
/// </summary>
public sealed class HybridAiExtractionEngine : IAiExtractionEngine
{
    /// <summary>Floor on how often the real on-device model is actually invoked, regardless of
    /// how fast the clinician is typing/dictating - keeps this "sensible" even though on-device
    /// calls are free, since each one still costs real inference time/battery.</summary>
    private static readonly TimeSpan MinIntervalBetweenRealCalls = TimeSpan.FromSeconds(2);

    private readonly OnDeviceAiExtractionEngine _onDeviceEngine;
    private readonly AzureOpenAiExtractionEngine _cloudEngine;
    private readonly FakeAiExtractionService _fallbackEngine;
    private DateTime _lastRealCallAt = DateTime.MinValue;
    private bool _realCallInFlight;

    public HybridAiExtractionEngine(
        OnDeviceAiExtractionEngine onDeviceEngine,
        AzureOpenAiExtractionEngine cloudEngine,
        FakeAiExtractionService fallbackEngine)
    {
        _onDeviceEngine = onDeviceEngine;
        _cloudEngine = cloudEngine;
        _fallbackEngine = fallbackEngine;
    }

    /// <summary>Which tier to use. <see cref="AiEngineMode.Auto"/> (default) runs the normal
    /// on-device → cloud → regex fallback chain; the other values pin to exactly one engine
    /// (still falls through to regex if the pinned real engine throws, so a manual pick never
    /// just produces a hard error in the UI).</summary>
    public AiEngineMode PreferredEngine { get; set; } = AiEngineMode.Auto;

    /// <summary>True when the real on-device model is usable on this device right now - exposed
    /// so the UI can show the clinician which engine is actually driving the checklist.</summary>
    public bool IsRealModelAvailable => _onDeviceEngine.IsAvailable;

    /// <summary>True when the Azure OpenAI cloud engine has been configured with credentials.</summary>
    public bool IsCloudModelAvailable => _cloudEngine.IsAvailable;

    public bool IsAvailable => true; // always available - falls back to the regex engine if needed

    public async Task<List<FieldSuggestion>> AnalyzeAsync(string text, FormRecipe recipe, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<FieldSuggestion>();

        if (PreferredEngine == AiEngineMode.Regex)
            return await _fallbackEngine.AnalyzeAsync(text, recipe, cancellationToken).ConfigureAwait(false);

        var tryOnDevice = PreferredEngine is AiEngineMode.Auto or AiEngineMode.OnDevice;
        var tryCloud = PreferredEngine is AiEngineMode.Auto or AiEngineMode.Cloud;

        if (tryOnDevice && _onDeviceEngine.IsAvailable && (PreferredEngine == AiEngineMode.OnDevice || CanUseOnDeviceNow()))
        {
            _realCallInFlight = true;
            _lastRealCallAt = DateTime.UtcNow;
            try
            {
                // An empty result from the real model on non-trivial text likely means it
                // genuinely found nothing yet (short/early transcript) rather than a failure -
                // trust it as-is rather than silently masking it with fallback guesses.
                return await _onDeviceEngine.AnalyzeAsync(text, recipe, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken is { IsCancellationRequested: false })
            {
                // The on-device call itself failed (unsafe-content rejection, timeout, schema
                // parse error, etc) rather than genuinely finding nothing - try the cloud engine
                // next for this same request instead of immediately giving up to regex.
            }
            finally
            {
                _realCallInFlight = false;
            }
        }

        if (tryCloud && _cloudEngine.IsAvailable)
        {
            try
            {
                return await _cloudEngine.AnalyzeAsync(text, recipe, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken is { IsCancellationRequested: false })
            {
                // Cloud call failed too (network error, quota, bad credentials, etc) - fall
                // through to the regex engine so a single tap always produces a usable result.
            }
        }

        return await _fallbackEngine.AnalyzeAsync(text, recipe, cancellationToken).ConfigureAwait(false);
    }

    private bool CanUseOnDeviceNow()
    {
        var sinceLastCall = DateTime.UtcNow - _lastRealCallAt;
        return !_realCallInFlight && sinceLastCall >= MinIntervalBetweenRealCalls;
    }
}

/// <summary>The four choices shown in the compact engine picker on the AI-fill screen.</summary>
public enum AiEngineMode
{
    /// <summary>Normal on-device → cloud → regex fallback chain (recommended default).</summary>
    Auto,
    OnDevice,
    Cloud,
    Regex,
}
