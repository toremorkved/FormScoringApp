using Microsoft.Extensions.DependencyInjection;
using NewsScoreApp.Services;
using NewsScoreApp.Services.AI;

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

		// For the controlled POC release, credentials are injected at build time from local
		// environment variables and persisted only in device SecureStorage. Never commit them.
		if (!azureSettings.IsConfigured)
		{
			var endpoint = AzureOpenAiBuildSettings.Endpoint;
			var deployment = AzureOpenAiBuildSettings.Deployment;
			var apiKey = AzureOpenAiBuildSettings.ApiKey;

			if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
			{
				azureSettings.Endpoint = endpoint;
				azureSettings.DeploymentName = string.IsNullOrWhiteSpace(deployment) ? "gpt-4.1-mini" : deployment;
				azureSettings.ApiKey = apiKey;
			}
		}
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