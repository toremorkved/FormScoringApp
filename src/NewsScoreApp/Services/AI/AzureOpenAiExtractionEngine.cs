using System.Net.Http.Headers;
using System.Text;
using NewsScoreApp.Models.AI;
using NewsScoreApp.Models.FormEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace NewsScoreApp.Services.AI;

/// <summary>
/// Cloud fallback/primary AI engine: calls an Azure OpenAI chat-completions deployment
/// (gpt-4.1-mini) using its native "structured outputs" support
/// (<c>response_format: {"type":"json_schema", ..., "strict": true}</c>). Unlike Apple's
/// on-device Foundation Models runtime, Azure OpenAI's schema validator has no problem with
/// nested arrays/objects, so this reuses the exact same hand-built <see cref="JSchema"/> shape
/// as <see cref="OnDeviceAiExtractionEngine"/> (same field-id enum trick) without needing the
/// "$ref"-avoidance workaround at all - it's kept anyway for consistency between the two engines.
/// "strict": true additionally makes Azure OpenAI guarantee the response satisfies the schema
/// (extra tokens/fields cause the call to fail rather than silently drifting), so - just like the
/// on-device engine - a plain deserialize is all that's needed, no free-text JSON repair.
///
/// This engine exists because Apple's on-device guided generation turned out to reject ordinary
/// clinical vital-signs text as "Detected content likely to be unsafe" specifically when
/// schema-constrained (never observed under free-text generation) - an undocumented guardrail
/// quirk that could not be prompted around. Azure OpenAI's content filtering is configurable
/// per-resource and, empirically, does not reject this kind of neutral clinical extraction text.
/// </summary>
public sealed class AzureOpenAiExtractionEngine : IAiExtractionEngine
{
    private readonly AzureOpenAiSettingsService _settings;
    private readonly HttpClient _httpClient;

    public AzureOpenAiExtractionEngine(AzureOpenAiSettingsService settings)
    {
        _settings = settings;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Available whenever an endpoint, API key and deployment name have all been
    /// configured (see <see cref="AzureOpenAiSettingsService"/>) - this is a cloud engine so there
    /// is no on-device hardware/OS capability to probe, only "do we have credentials".</summary>
    public bool IsAvailable => _settings.IsConfigured;

    public async Task<List<FieldSuggestion>> AnalyzeAsync(string text, FormRecipe recipe, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(text))
            return new List<FieldSuggestion>();

        // No small-context-window constraint here (unlike the on-device model's ~4096 token
        // budget), so the whole recipe can be asked about in a single call instead of batching
        // per-section - cheaper (one request instead of several) and lets the model see the full
        // set of fields at once for better disambiguation between related ones (e.g. spo2 vs o2).
        var schema = BuildSchema(recipe.Sections);
        var requestBody = BuildRequestBody(text, recipe, schema);

        var url = $"{_settings.Endpoint!.TrimEnd('/')}/openai/deployments/{_settings.DeploymentName}/chat/completions?api-version=2024-08-01-preview";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("api-key", _settings.ApiKey);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"===== [AzureAI] Request failed =====\n{ex}");
            throw;
        }

        var responseText = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Log($"===== [AzureAI] Raw HTTP response ({(int)httpResponse.StatusCode}) =====\n{responseText}");

        if (!httpResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure OpenAI call failed ({(int)httpResponse.StatusCode}): {responseText}");

        var content = JObject.Parse(responseText)["choices"]?[0]?["message"]?["content"]?.Value<string>()
            ?? throw new Exception($"No message content in Azure OpenAI response: {responseText}");

        AiExtractionResponse parsed;
        try
        {
            parsed = JsonConvert.DeserializeObject<AiExtractionResponse>(content)
                ?? throw new Exception($"Deserialized to null from: {content}");
        }
        catch (Exception ex)
        {
            Log($"===== [AzureAI] Failed to parse content as {nameof(AiExtractionResponse)} =====\n{ex}");
            throw;
        }

        return MapToSuggestions(parsed, recipe);
    }

    /// <summary>Same hand-built schema shape/enum trick as <see cref="OnDeviceAiExtractionEngine.BuildSchema"/>
    /// so both engines produce identical result shapes for <see cref="MapToSuggestions"/> - listing
    /// every recipe field's id (not batched, since there's no small-context-window reason to
    /// split here).</summary>
    private static JSchema BuildSchema(List<FormSection> sections)
    {
        var fieldIdSchema = new JSchema { Type = JSchemaType.String };
        foreach (var section in sections)
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

    private static string BuildRequestBody(string text, FormRecipe recipe, JSchema schema)
    {
        var instructions =
            "Du er et tekstuttrekkingsverktøy som leser norske sykepleie-/legenotater og finner " +
            "eksisterende tallverdier/valg for et strukturert registreringsskjema " +
            $"({recipe.Title}). Du skal KUN identifisere verdier som allerede er skrevet i " +
            "teksten - du skal ikke tolke kliniske funn, stille diagnoser eller foreslå " +
            "behandling. Feltene som kan fylles ut er:\n" +
            string.Join("\n", recipe.Sections.Select(DescribeField)) +
            "\n\nRegler:\n" +
            "- Ta KUN med felt du finner eksplisitt eller svært tydelig underforstått støtte for i teksten.\n" +
            "- Ikke gjett eller fyll ut felt teksten ikke nevner i det hele tatt.\n" +
            "- \"quote\" skal være et kort, ordrett utdrag fra teksten (maks ca. 12 ord).\n" +
            "- \"reasoning\" skal være maks fem ord på norsk.\n" +
            "- \"confidence\" skal være \"high\" når verdien er eksplisitt tallfestet/nevnt, \"medium\" når den er " +
            "tydelig underforstått, og \"low\" ved usikker tolkning.\n" +
            "- For felt med gitte alternativer skal \"value\" være nøyaktig én av de oppgitte alternativ-verdiene.\n" +
            "- Hvis ingen av feltene finnes i teksten, svar med et tomt \"fields\"-array.";

        var payload = new JObject
        {
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = instructions },
                new JObject { ["role"] = "user", ["content"] = $"Notat:\n\"\"\"\n{text}\n\"\"\"" },
            },
            ["max_tokens"] = 2000,
            ["temperature"] = 0,
            ["response_format"] = new JObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JObject
                {
                    ["name"] = "field_extraction",
                    ["strict"] = true,
                    ["schema"] = JObject.Parse(schema.ToString()),
                },
            },
        };
        return payload.ToString(Formatting.None);
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
        // See OnDeviceAiExtractionEngine.MapToSuggestions for why this dedupe is needed - the
        // model can repeat the same fieldId across separate array entries even though the schema
        // forbids duplicate keys within one object.
        return results
            .GroupBy(r => r.FieldId)
            .Select(g => g.Last())
            .ToList();
    }

    /// <summary>Same debug-log approach as <see cref="OnDeviceAiExtractionEngine.Log"/> - mirrors
    /// to Console/Debug and appends to Documents/ai-debug.log for retrieval from a physical
    /// device via <c>xcrun devicectl device copy from</c>.</summary>
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
}
