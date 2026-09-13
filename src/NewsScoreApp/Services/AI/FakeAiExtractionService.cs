using System.Text.RegularExpressions;
using NewsScoreApp.Models.AI;
using NewsScoreApp.Models.FormEngine;

namespace NewsScoreApp.Services.AI;

/// <summary>
/// POC "language model" - takes free clinical text and the currently active <see cref="FormRecipe"/>
/// and returns a best-effort <see cref="FieldSuggestion"/> per field it could find evidence for,
/// using plain regex/keyword matching instead of a real LLM call. Deliberately recipe-agnostic
/// where possible (numeric fields are found via a small "keywords near a number" heuristic), with
/// a few NEWS2-specific keyword lists since that is the only recipe the POC test transcript covers.
/// Doubles as the offline/instant fallback used by <see cref="HybridAiExtractionEngine"/> whenever
/// the real on-device model (<see cref="OnDeviceAiExtractionEngine"/>) is unavailable (older
/// devices, simulators without Apple Intelligence, etc), so the live checklist always works.
/// </summary>
public sealed class FakeAiExtractionService : IAiExtractionEngine
{
    public bool IsAvailable => true;

    public Task<List<FieldSuggestion>> AnalyzeAsync(string text, FormRecipe recipe, CancellationToken cancellationToken = default)
        => Task.FromResult(Analyze(text, recipe));

    /// <summary>Synchronous, no artificial delay - used for live re-analysis on every text
    /// change (debounced by the caller) so the checklist can update as the clinician dictates,
    /// field by field, instead of waiting for one big "done talking" analysis pass.</summary>
    public List<FieldSuggestion> Analyze(string text, FormRecipe recipe)
    {
        var results = new List<FieldSuggestion>();
        foreach (var section in recipe.Sections)
        {
            var suggestion = section.Kind switch
            {
                FieldKind.Numeric => TryExtractNumeric(text, section),
                FieldKind.SingleChoice => TryExtractChoice(text, section),
                // Free-text fields need real language understanding to summarize a paragraph -
                // only the on-device/cloud AI engines can do that meaningfully; the offline regex
                // fallback simply leaves them for the clinician to fill in manually.
                _ => null,
            };
            if (suggestion is not null) results.Add(suggestion);
        }
        return results;
    }

    // ----- Numeric fields: keyword-anchored regex per known field id, falling back to a generic
    // "label word near a number" scan for unrecognised recipes. -----

    private static readonly Dictionary<string, (string[] Keywords, string Unit)> NumericHints = new()
    {
        ["resp"] = (new[] { "respirasjonsfrekvens", "resp\\.?frekvens", "respirasjon", "pust" }, "/min"),
        ["spo2"] = (new[] { "spo2", "spo₂", "oksygenmetning", "metning", "sat\\.?" }, "%"),
        ["sbp"] = (new[] { "systolisk", "blodtrykk", "bt" }, "mmHg"),
        ["pulse"] = (new[] { "puls", "hjertefrekvens", "hf" }, "/min"),
        ["temp"] = (new[] { "temperatur", "temp\\.?" }, "°C"),
    };

    private FieldSuggestion? TryExtractNumeric(string text, FormSection section)
    {
        if (!NumericHints.TryGetValue(section.Id, out var hint))
            return null;

        foreach (var keyword in hint.Keywords)
        {
            // Look for the keyword followed (within ~25 chars) by a number, optionally with a
            // decimal comma/point - covers phrasing like "SpO2 målt til 91 %" or "temp. 38,4".
            var pattern = $@"(?<kw>{keyword})[^\d\n]{{0,25}}?(?<num>\d{{1,3}}(?:[.,]\d)?)";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            var numberText = match.Groups["num"].Value.Replace(',', '.');
            if (!double.TryParse(numberText, System.Globalization.CultureInfo.InvariantCulture, out var value))
                continue;

            var quote = ExtractSentence(text, match.Index, match.Index + match.Length);

            return new FieldSuggestion
            {
                FieldId = section.Id,
                FieldLabel = section.Label,
                Value = numberText,
                DisplayValue = $"{numberText} {section.Unit}".Trim(),
                SourceQuote = quote,
                Reasoning = $"Fant \"{match.Groups["kw"].Value}\" etterfulgt av verdien {numberText} i teksten.",
                Confidence = numberText.Contains('.') || match.Length < 18 ? AiConfidence.High : AiConfidence.Medium,
            };
        }
        return null;
    }

    // ----- Single-choice fields: keyword -> option value map per known field id. -----

    private static readonly Dictionary<string, Dictionary<string, string[]>> ChoiceHints = new()
    {
        ["o2"] = new()
        {
            ["oxygen"] = new[] { "på oksygen", "oksygentilskudd", "nesekateter", "oksygenbriller", "får o2", "liter oksygen" },
            ["air"] = new[] { "romluft", "uten oksygentilskudd", "ingen oksygen" },
        },
        ["acvpu"] = new()
        {
            ["alert"] = new[] { "alert", "våken", "orientert", "klar og orientert" },
            ["confusion"] = new[] { "forvirr", "nyoppstått forvirring", "desorientert" },
            ["voice"] = new[] { "reagerer på tiltale", "reagerer på stemme", "vekkes ved tiltale" },
            ["pain"] = new[] { "reagerer på smerte", "smertestimulering" },
            ["unresponsive"] = new[] { "bevisstløs", "ikke kontaktbar", "reagerer ikke" },
        },
    };

    private FieldSuggestion? TryExtractChoice(string text, FormSection section)
    {
        if (!ChoiceHints.TryGetValue(section.Id, out var optionKeywords))
            return null;

        foreach (var (optionValue, keywords) in optionKeywords)
        {
            foreach (var keyword in keywords)
            {
                var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;

                var option = section.Options.FirstOrDefault(o => o.Value == optionValue);
                var quote = ExtractSentence(text, index, index + keyword.Length);

                return new FieldSuggestion
                {
                    FieldId = section.Id,
                    FieldLabel = section.Label,
                    Value = optionValue,
                    DisplayValue = option?.Label ?? optionValue,
                    SourceQuote = quote,
                    Reasoning = $"Teksten inneholder \"{keyword}\", som tilsvarer \"{option?.Label ?? optionValue}\".",
                    Confidence = AiConfidence.High,
                };
            }
        }
        return null;
    }

    /// <summary>Grows the match outward to the nearest sentence boundaries so the review card can
    /// show a natural-reading quote instead of a mid-sentence fragment.</summary>
    private static string ExtractSentence(string text, int start, int end)
    {
        int from = start;
        while (from > 0 && text[from - 1] is not ('.' or '\n' or '!' or '?')) from--;
        int to = end;
        while (to < text.Length && text[to] is not ('.' or '\n' or '!' or '?')) to++;
        if (to < text.Length) to++; // include the terminator

        var snippet = text[from..Math.Min(to, text.Length)].Trim();
        return snippet.Length > 140 ? snippet[..140].TrimEnd() + "…" : snippet;
    }
}
