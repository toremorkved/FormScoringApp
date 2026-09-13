using CrossIntelligence;
using NewsScoreApp.Models.AI;
using NewsScoreApp.Models.FormEngine;
using Newtonsoft.Json.Schema;

namespace NewsScoreApp.Services.AI;

/// <summary>
/// Real on-device AI engine: uses Apple's on-device system language model (Apple Intelligence) via
/// the CrossIntelligence NuGet package, with genuine "guided generation" - Apple's Foundation
/// Models runtime is given a JSON Schema up front and its *token sampler* is constrained so it can
/// only ever emit tokens that keep the output valid against that schema. That means well-formed
/// JSON is a runtime guarantee, not something we hope for and repair after the fact.
///
/// We deliberately do NOT use CrossIntelligence's own <c>IntelligenceSession.RespondAsync&lt;T&gt;()</c>
/// convenience API for this: it builds its schema via plain reflection
/// (<c>Newtonsoft.Json.Schema.Generation.JSchemaGenerator</c> with default settings), which for a
/// type containing a nested list - exactly our shape, "a list of extracted fields" - emits a
/// schema using "$ref"/"definitions" to avoid repeating the nested object's schema inline. Apple's
/// on-device schema parser does not support "$ref" and rejects the whole schema instantly (in
/// ~25ms, before the model ever runs) with "Failed to parse schema". This was the root cause of
/// this engine's entire history of malformed-JSON parsing bugs: because guided generation was
/// silently unusable for our shape, we were falling back to asking for free-text JSON in the
/// prompt and parsing/repairing whatever prose-adjacent JSON the small on-device model felt like
/// producing - which is exactly the wrong tool for structured output and why it kept finding new
/// ways to mangle punctuation.
///
/// The actual fix: build the JSON Schema ourselves with <see cref="JSchemaGenerator.SchemaReferenceHandling"/>
/// set to <see cref="SchemaReferenceHandling.None"/> so everything is inlined (verified: this
/// produces zero "$ref"/"definitions" in the output), and call Apple's native
/// <c>respond:jsonSchema:includeSchemaInPrompt:</c> binding directly instead of going through
/// <c>RespondAsync&lt;T&gt;()</c>. On top of that we constrain "fieldId" to an enum of exactly the
/// field ids in the current batch (so the model can't invent one) - something reflection-only
/// schema generation couldn't express at all, since it doesn't know our recipe's field ids.
/// Runs entirely on-device - no network call, no per-call cost - which is what makes it safe to
/// call repeatedly while the clinician is still dictating (unlike a cloud LLM, where every
/// keystroke-triggered re-analysis would be a billed request).
/// </summary>
public sealed class OnDeviceAiExtractionEngine : IAiExtractionEngine
{
    /// <summary>Apple's on-device Foundation Models session has a small total context window
    /// (instructions + prompt + the model's own reply all share it - observed to be roughly 4096
    /// tokens for the ~3B on-device model, and CrossIntelligence doesn't expose a way to raise
    /// it). A form with many fields (e.g. NEWS2's 7) asked for in one go can still produce a long
    /// reply, so we split the recipe's fields into small batches and make one (fast, on-device,
    /// free) call per batch - each reply then only ever needs to describe a handful of fields.
    /// Guided generation makes the *shape* reliable regardless, but keeping batches small still
    /// helps quality/latency of the model's actual reasoning.</summary>
    private const int MaxFieldsPerBatch = 3;

    /// <summary>Apple Intelligence availability is a static, synchronous check on-device (no
    /// network round-trip) - true only on eligible hardware/region with the feature enabled.</summary>
    public bool IsAvailable => IntelligenceSession.IsAppleIntelligenceAvailable;

    /// <summary>Hard ceiling on how long we wait for a single on-device call before giving up and
    /// treating it as failed. Necessary safety net: switching <c>includeSchemaInPrompt</c> to
    /// <c>false</c> (tried to dodge the "unsafe content" guardrail below) made a real call from the
    /// physical device simply never return - no response, no error, no timeout of its own. Guided
    /// generation without the schema's fields also present as prompt text seems to be able to wedge
    /// the on-device decoder indefinitely. Without this, that hang would surface in the UI as "AI
    /// laster i det uendelige" with the clinician stuck waiting forever instead of instantly
    /// falling back to the regex engine like every other failure mode already does.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(25);

    public async Task<List<FieldSuggestion>> AnalyzeAsync(string text, FormRecipe recipe, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(text))
            return new List<FieldSuggestion>();

        var batches = recipe.Sections
            .Select((section, index) => (section, index))
            .GroupBy(x => x.index / MaxFieldsPerBatch)
            .Select(g => g.Select(x => x.section).ToList())
            .ToList();

        var results = new List<FieldSuggestion>();
        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchResults = await AnalyzeBatchAsync(text, recipe, batch, cancellationToken).ConfigureAwait(false);
            results.AddRange(batchResults);
        }
        return results;
    }

    private async Task<List<FieldSuggestion>> AnalyzeBatchAsync(
        string text, FormRecipe recipe, List<FormSection> batchSections, CancellationToken cancellationToken)
    {
        // A fresh, short-lived session per batch (instead of one long-lived session reused across
        // batches/calls) - the native session keeps growing its own transcript across calls, which
        // would otherwise eat back into the same limited context window we're trying to protect by
        // batching in the first place. Each batch is a small, self-contained request.
        using var session = new AppleIntelligenceSessionNative(
            instructions: BuildInstructions(recipe, batchSections), tools: Array.Empty<Foundation.NSObject>());
        try
        {
            return await RunSessionAsync(session, text, recipe, batchSections).ConfigureAwait(false);
        }
        finally
        {
            session.FreeTools();
        }
    }

    private async Task<List<FieldSuggestion>> RunSessionAsync(
        AppleIntelligenceSessionNative session, string text, FormRecipe recipe, List<FormSection> batchSections)
    {
        var prompt = BuildPrompt(text);
        var schema = BuildSchema(batchSections).ToString();

        Log($"===== [AI] Sending prompt (recipe={recipe.Id}, fields=[{string.Join(",", batchSections.Select(s => s.Id))}]) =====\n{prompt}\n----- schema -----\n{schema}");

        string raw;
        try
        {
            // includeSchemaInPrompt:true - the model needs the schema's field/enum descriptions
            // visible as prompt text, not just as a token-level grammar constraint: with this set
            // to false (tried, to dodge both the guardrail and the slowness below) a real device
            // call simply never returned at all, even past a 12s timeout - worse than the known
            // failure modes of true. So this stays true. Responses with the schema inlined take
            // noticeably longer than the old free-text-only prompt (observed 20-40s+ vs 3-5s) and
            // the on-device guardrail is more prone to rejecting them as "unsafe" once combined
            // with clinical text - both are being tracked/mitigated separately (neutral wording in
            // BuildInstructions/BuildPrompt, generous CallTimeout here), but flipping this flag is
            // not a safe fix on its own.
            var respondTask = session.RespondAsync(prompt, schema, includeSchemaInPrompt: true);
            var completed = await Task.WhenAny(respondTask, Task.Delay(CallTimeout)).ConfigureAwait(false);
            if (completed != respondTask)
                throw new TimeoutException($"On-device AI call timed out after {CallTimeout.TotalSeconds:0}s (recipe={recipe.Id}, fields=[{string.Join(",", batchSections.Select(s => s.Id))}]).");
            raw = await respondTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"===== [AI] Call failed =====\n{ex}");
            throw;
        }

        Log($"===== [AI] Raw response =====\n{raw}");

        AiExtractionResponse response;
        try
        {
            // Guided generation guarantees the response is valid JSON matching our schema - so a
            // plain deserialize is all that's needed here, no brace-scanning or glitch-repair.
            response = Newtonsoft.Json.JsonConvert.DeserializeObject<AiExtractionResponse>(raw)
                ?? throw new Exception($"Deserialized to null from: {raw}");
            Log($"===== [AI] Parsed response =====\n{Newtonsoft.Json.JsonConvert.SerializeObject(response, Newtonsoft.Json.Formatting.Indented)}");
        }
        catch (Exception ex)
        {
            // Model replied with something that isn't valid JSON matching our shape (chit-chat, a
            // refusal, etc - should be rare under guided generation, but not impossible).
            // Rethrow (rather than silently returning an empty list) so HybridAiExtractionEngine
            // can tell "genuinely found nothing" apart from "the real model call itself failed"
            // and fall back to the regex engine for *this* request instead of just showing "0 felt
            // funnet".
            Log($"===== [AI] Failed to parse raw response as {nameof(AiExtractionResponse)} =====\n{ex}");
            throw;
        }

        return MapToSuggestions(response, recipe);
    }

    /// <summary>Hand-builds the JSON Schema for this batch's response shape instead of generating
    /// it from a .NET type via reflection. Two things reflection-based generation can't give us:
    /// 1) Full control over "$ref"/"definitions" - Apple's on-device schema parser rejects "$ref"
    ///    outright, so every nested object must be inlined. (Newtonsoft's <see cref="JSchemaGenerator"/>
    ///    can also be told to do this via <see cref="SchemaReferenceHandling.None"/>, which is what
    ///    we'd use if we went through it - but hand-building keeps the shape and the per-batch
    ///    enum constraint below in one obvious place.)
    /// 2) A per-request enum constraint on "fieldId" listing exactly this batch's field ids, so the
    ///    model is structurally prevented from inventing an id that doesn't exist in the recipe -
    ///    something a type-driven schema has no way to express since the type doesn't know about
    ///    recipe-specific ids at compile time.</summary>
    private static JSchema BuildSchema(List<FormSection> batchSections)
    {
        var fieldIdSchema = new JSchema { Type = JSchemaType.String };
        foreach (var section in batchSections)
            fieldIdSchema.Enum.Add(section.Id);

        var confidenceSchema = new JSchema { Type = JSchemaType.String };
        foreach (var confidence in new[] { "high", "medium", "low" })
            confidenceSchema.Enum.Add(confidence);

        var fieldSchema = new JSchema { Type = JSchemaType.Object, AllowAdditionalProperties = false };
        fieldSchema.Properties.Add("fieldId", fieldIdSchema);
        fieldSchema.Properties.Add("value", new JSchema { Type = JSchemaType.String });
        fieldSchema.Properties.Add("quote", new JSchema { Type = JSchemaType.String });
        fieldSchema.Properties.Add("reasoning", new JSchema { Type = JSchemaType.String });
        fieldSchema.Properties.Add("confidence", confidenceSchema);
        foreach (var required in new[] { "fieldId", "value", "quote", "reasoning", "confidence" })
            fieldSchema.Required.Add(required);

        var fieldsArraySchema = new JSchema { Type = JSchemaType.Array };
        fieldsArraySchema.Items.Add(fieldSchema);

        var rootSchema = new JSchema { Type = JSchemaType.Object, AllowAdditionalProperties = false };
        rootSchema.Properties.Add("fields", fieldsArraySchema);
        rootSchema.Required.Add("fields");
        return rootSchema;
    }

    /// <summary>TEMP (debugging): mirrors every prompt/response to Console/Debug (useful when a
    /// debugger is attached) *and* appends to a plain-text file in the app's Documents folder,
    /// since os_log/NSLog truncates long multi-line messages (~1024 bytes) making prompts/schemas
    /// unreadable in `simctl log show`. Tail the file directly instead:
    /// xcrun simctl get_app_container &lt;udid&gt; com.dips.newsscoreapp data
    /// then cat "$THAT_PATH/Documents/ai-debug.log"</summary>
    private static void Log(string message)
    {
        Console.WriteLine(message);
        System.Diagnostics.Debug.WriteLine(message);
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ai-debug.log");
            File.AppendAllText(path, $"\n[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { /* best-effort debug aid only - never let logging itself break analysis */ }
    }

    private static string BuildInstructions(FormRecipe recipe, List<FormSection> batchSections)
    {
        var fieldDescriptions = batchSections.Select(DescribeField);
        return
            "Du er et tekstuttrekkingsverktøy som leser norske sykepleie-/legenotater og finner " +
            "eksisterende tallverdier/valg for et strukturert registreringsskjema " +
            $"({recipe.Title}). Du skal KUN identifisere verdier som allerede er skrevet i " +
            "teksten - du skal ikke tolke kliniske funn, stille diagnoser eller foreslå " +
            "behandling. Se KUN etter disse feltene (andre felt i skjemaet håndteres i et " +
            "annet kall og skal ignoreres her):\n" +
            string.Join("\n", fieldDescriptions) +
            "\n\nRegler:\n" +
            "- Ta KUN med felt du finner eksplisitt eller svært tydelig underforstått støtte for i teksten.\n" +
            "- Ikke gjett eller fyll ut felt teksten ikke nevner i det hele tatt.\n" +
            "- \"quote\" skal være et kort, ordrett utdrag fra teksten (maks ca. 12 ord).\n" +
            "- \"reasoning\" skal være maks fem ord på norsk.\n" +
            "- \"confidence\" skal være \"high\" når verdien er eksplisitt tallfestet/nevnt, \"medium\" når den er " +
            "tydelig underforstått, og \"low\" ved usikker tolkning.\n" +
            "- For felt med gitte alternativer skal \"value\" være nøyaktig én av de oppgitte alternativ-verdiene.\n" +
            "- Hvis ingen av feltene over finnes i teksten, svar med et tomt \"fields\"-array.";
    }

    private static string DescribeField(FormSection section)
    {
        if (section.Kind == FieldKind.SingleChoice)
        {
            var options = string.Join(", ", section.Options.Select(o => $"\"{o.Value}\" ({o.Label})"));
            return $"- id=\"{section.Id}\" \"{section.Label}\": velg én verdi blant [{options}]";
        }

        if (section.Kind == FieldKind.Text)
            return $"- id=\"{section.Id}\" \"{section.Label}\": fritekst - gjengi/oppsummer kun det som allerede står i notatet om dette temaet, ikke finn på innhold";

        var range = section.Min is not null && section.Max is not null
            ? $", gyldig område {section.Min}-{section.Max}"
            : string.Empty;
        return $"- id=\"{section.Id}\" \"{section.Label}\" ({section.Unit}): tallverdi{range}";
    }

    private static string BuildPrompt(string text)
        => $"Du er et tekstuttrekkingsverktøy for strukturerte skjemaer. Les notatet under og trekk ut tallverdier/valg for feltene definert over - du skal IKKE tolke, vurdere eller foreslå noen form for medisinsk behandling, kun identifisere hvilke tall/valg som allerede er nevnt i teksten.\n\nNotat:\n\"\"\"\n{text}\n\"\"\"";

    private static List<FieldSuggestion> MapToSuggestions(AiExtractionResponse response, FormRecipe recipe)
    {
        var results = new List<FieldSuggestion>();
        foreach (var field in response.Fields)
        {
            var section = recipe.Sections.FirstOrDefault(s => s.Id == field.FieldId);
            if (section is null || string.IsNullOrWhiteSpace(field.Value)) continue;

            var displayValue = section.Kind == FieldKind.SingleChoice
                ? section.Options.FirstOrDefault(o => o.Value == field.Value)?.Label ?? field.Value
                : $"{field.Value} {section.Unit}".Trim();

            results.Add(new FieldSuggestion
            {
                FieldId = section.Id,
                FieldLabel = section.Label,
                Value = field.Value,
                DisplayValue = displayValue,
                SourceQuote = field.Quote,
                Reasoning = field.Reasoning,
                Confidence = field.Confidence.Trim().ToLowerInvariant() switch
                {
                    "high" => AiConfidence.High,
                    "low" => AiConfidence.Low,
                    _ => AiConfidence.Medium,
                },
            });
        }
        // Occasionally the model emits the same fieldId twice in one response (observed with the
        // Azure cloud engine on the nrs2002 form despite a schema that already forbids duplicate
        // *keys* within one object - nothing stops it repeating the same fieldId across separate
        // array entries). Keep only the last occurrence per field so the checklist/UI never shows
        // a field twice or double-counts it.
        return results
            .GroupBy(r => r.FieldId)
            .Select(g => g.Last())
            .ToList();
    }
}
