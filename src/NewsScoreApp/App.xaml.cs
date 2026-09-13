using Microsoft.Extensions.DependencyInjection;
using NewsScoreApp.Services;

namespace NewsScoreApp;

public partial class App : Application
{
	public App(AppSettingsService settings, Services.AI.AzureOpenAiSettingsService azureSettings)
	{
		InitializeComponent();
		settings.ApplyTheme();

		// Blocking wait (not fire-and-forget): the hybrid engine's IsAvailable/IsConfigured
		// checks are synchronous and can run as soon as the first keystroke debounce fires, so
		// the secure-storage-backed API key must already be loaded into memory before that -
		// this is a single fast local Keychain read at startup, not a network call.
		azureSettings.LoadAsync().GetAwaiter().GetResult();

#if DEBUG
		// DEV-ONLY bootstrap: seeds Azure OpenAI dev/test resource credentials into secure
		// storage the first time the app runs on a given simulator/device, so the cloud AI tier
		// works out of the box while testing without needing a settings-screen UI yet. Never
		// overwrites a value the user/tester has already configured.
		//
		// Credentials are read from environment variables the developer sets locally
		// (AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_DEPLOYMENT / AZURE_OPENAI_API_KEY) - NEVER
		// hardcode a real endpoint/key/deployment literal here again. A previous version of this
		// file committed a real API key directly into source control; that key has since been
		// rotated/revoked in Azure and must stay that way. If neither env vars nor a
		// previously-saved SecureStorage value are present, the cloud tier simply stays
		// unconfigured and HybridAiExtractionEngine falls back to the on-device/regex engines.
		if (!azureSettings.IsConfigured)
		{
			var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
			var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
			var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

			if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
			{
				azureSettings.Endpoint = endpoint;
				azureSettings.DeploymentName = string.IsNullOrWhiteSpace(deployment) ? "gpt-4.1-mini" : deployment;
				azureSettings.ApiKey = apiKey;
			}
		}
#endif
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());

#if DEBUG
		if (Services.AI.AiSelfTestRunner.RunOnStartup)
		{
			// Apple's on-device model session appears to require the app to actually be in the
			// foreground/active to run (calls made from the constructor, before the window is
			// even shown, fail near-instantly with a low-level GenerationError). Delay a couple
			// seconds after the window activates so the self-test runs the same way a real user
			// interaction would.
			window.Activated += async (_, _) =>
			{
					if (!Services.AI.AiSelfTestRunner.RunOnStartup) return;
					Services.AI.AiSelfTestRunner.RunOnStartup = false; // only run once per launch
						await Task.Delay(TimeSpan.FromSeconds(10));
					var recipes = MauiProgram.Services.GetRequiredService<Services.FormEngine.FormRecipeRepository>();
					var engine = MauiProgram.Services.GetRequiredService<Services.AI.IAiExtractionEngine>();
					await Services.AI.AiSelfTestRunner.RunAsync(recipes, engine);
				};
		}
#endif

		return window;
	}
}