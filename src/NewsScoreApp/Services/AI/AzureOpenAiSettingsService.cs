using Microsoft.Maui.Storage;

namespace NewsScoreApp.Services.AI;

/// <summary>
/// Holds the Azure OpenAI connection details (endpoint, deployment name, API key) needed by
/// <see cref="AzureOpenAiExtractionEngine"/>. The API key is the one secret in this app, so it
/// goes through <see cref="SecureStorage"/> (iOS Keychain-backed) rather than <see cref="Preferences"/>
/// (a plain plist) - endpoint/deployment name are not sensitive on their own and are stored as
/// plain preferences for simplicity/synchronous read access.
/// </summary>
public sealed class AzureOpenAiSettingsService
{
    private const string EndpointKey = "azure_openai_endpoint";
    private const string DeploymentKey = "azure_openai_deployment";
    private const string ApiKeySecureKey = "azure_openai_api_key";

    private string? _cachedApiKey;
    private bool _apiKeyLoaded;

    public string? Endpoint
    {
        get => Preferences.Default.Get(EndpointKey, string.Empty) is { Length: > 0 } v ? v : null;
        set => Preferences.Default.Set(EndpointKey, value ?? string.Empty);
    }

    public string DeploymentName
    {
        get => Preferences.Default.Get(DeploymentKey, "gpt-4.1-mini");
        set => Preferences.Default.Set(DeploymentKey, value);
    }

    /// <summary>Cached synchronously after first async load so <see cref="IsConfigured"/> and
    /// <see cref="AzureOpenAiExtractionEngine"/> can read it without every call being async -
    /// call <see cref="LoadAsync"/> once at startup (e.g. app launch / settings page load) before
    /// relying on this.</summary>
    public string? ApiKey
    {
        get => _cachedApiKey;
        set
        {
            _cachedApiKey = value;
            _ = SecureStorage.Default.SetAsync(ApiKeySecureKey, value ?? string.Empty);
        }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(DeploymentName) &&
        !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Loads the API key from secure storage into the in-memory cache. Must be awaited
    /// once (e.g. during app startup) before <see cref="ApiKey"/>/<see cref="IsConfigured"/> can
    /// be trusted to reflect a previously-saved key.</summary>
    public async Task LoadAsync()
    {
        if (_apiKeyLoaded) return;
        _cachedApiKey = await SecureStorage.Default.GetAsync(ApiKeySecureKey).ConfigureAwait(false);
        _apiKeyLoaded = true;
    }
}
