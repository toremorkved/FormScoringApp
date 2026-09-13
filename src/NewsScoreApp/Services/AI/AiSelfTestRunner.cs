using NewsScoreApp.Models.FormEngine;
using NewsScoreApp.Services.FormEngine;

namespace NewsScoreApp.Services.AI;

/// <summary>
/// DEBUG-only self-test: on app startup, automatically runs the real on-device AI extraction
/// engine against every recipe's canned sample transcript and writes a pass/fail summary to the
/// same ai-debug.log file already used for prompt/response logging - so failures across every
/// form can be triaged from one log pull (via `xcrun simctl`/`devicectl`) without a human having
/// to tap through each form and manually copy error text. Enable/trigger by setting
/// <see cref="RunOnStartup"/> to true (kept off by default so normal app usage isn't slowed down
/// by ~6 on-device model calls every launch).
/// </summary>
public static class AiSelfTestRunner
{
    public static bool RunOnStartup = false;

    public static async Task RunAsync(FormRecipeRepository recipes, IAiExtractionEngine engine)
    {
        Log("========== [SELFTEST] Starting AI self-test run across all recipes ==========");

        if (engine is HybridAiExtractionEngine hybrid)
            Log($"[SELFTEST] on-device available={hybrid.IsRealModelAvailable}, cloud available={hybrid.IsCloudModelAvailable}");

        await recipes.LoadAllAsync().ConfigureAwait(false);

        var passCount = 0;
        var failCount = 0;

        foreach (var recipe in recipes.Recipes)
        {
            var sample = SampleTranscripts.ForRecipe(recipe.Id);
            Log($"---------- [SELFTEST] Recipe '{recipe.Id}' ----------");
            try
            {
                // The on-device model session sometimes isn't warmed up yet right after a cold
                // app launch (fails near-instantly with a low-level GenerationError the first
                // time) but works fine moments later - so retry a couple of times with a short
                // backoff before giving up and counting this recipe as a genuine failure.
                List<Models.AI.FieldSuggestion> suggestions = null!;
                Exception? lastError = null;
                var attempts = 0;
                const int maxAttempts = 3;
                while (attempts < maxAttempts)
                {
                    attempts++;
                    try
                    {
                        suggestions = await engine.AnalyzeAsync(sample, recipe, CancellationToken.None).ConfigureAwait(false);
                        lastError = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Log($"[SELFTEST] '{recipe.Id}' attempt {attempts}/{maxAttempts} failed: {ex.Message}");
                        if (attempts < maxAttempts)
                            await Task.Delay(TimeSpan.FromSeconds(3 * attempts)).ConfigureAwait(false);
                    }
                }

                if (lastError is not null)
                    throw lastError;

                Log($"[SELFTEST] '{recipe.Id}' OK - {suggestions.Count}/{recipe.Sections.Count} fields found: " +
                    string.Join(", ", suggestions.Select(s => $"{s.FieldId}={s.Value}")));
                passCount++;
            }
            catch (Exception ex)
            {
                Log($"[SELFTEST] '{recipe.Id}' FAILED after retries: {ex.Message}");
                failCount++;
            }
        }

        Log($"========== [SELFTEST] Done: {passCount} passed, {failCount} failed ==========");
    }

    private static void Log(string message)
    {
        Console.WriteLine(message);
        System.Diagnostics.Debug.WriteLine(message);
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ai-debug.log");
            File.AppendAllText(path, $"\n[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { /* best-effort debug aid only */ }
    }
}
