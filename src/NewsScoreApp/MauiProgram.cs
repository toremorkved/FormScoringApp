using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewsScoreApp.Services;

namespace NewsScoreApp;

public static class MauiProgram
{
	public static IServiceProvider Services { get; private set; } = null!;

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<NewsScorer>();
		builder.Services.AddSingleton<AppSettingsService>();
		builder.Services.AddSingleton<ViewModels.MainViewModel>();
		builder.Services.AddTransient<Views.MainPage>();

		builder.Services.AddSingleton<Services.FormEngine.FormRecipeRepository>();
		builder.Services.AddSingleton<Services.FormEngine.GenericFormScorer>();
		builder.Services.AddSingleton<ViewModels.GenericFormViewModel>();
		builder.Services.AddTransient<Views.GenericFormPage>();
		builder.Services.AddTransient<Views.SettingsPage>();

		builder.Services.AddSingleton<Services.AI.FakeAiExtractionService>();
		builder.Services.AddSingleton<Services.AI.OnDeviceAiExtractionEngine>();
		builder.Services.AddSingleton<Services.AI.AzureOpenAiSettingsService>();
		builder.Services.AddSingleton<Services.AI.AzureOpenAiExtractionEngine>();
		builder.Services.AddSingleton<Services.AI.HybridAiExtractionEngine>();
		// Three-tier: on-device Apple Intelligence first (free/private), Azure OpenAI cloud
		// second (reliable structured outputs, needs credentials/network), instant regex engine
		// as last resort - see HybridAiExtractionEngine's doc comment for the full rationale.
		builder.Services.AddSingleton<Services.AI.IAiExtractionEngine>(sp => sp.GetRequiredService<Services.AI.HybridAiExtractionEngine>());
		builder.Services.AddTransient<ViewModels.AiFillViewModel>();
		builder.Services.AddTransient<Views.AiFillPage>();

		builder.Services.AddSingleton<Services.History.AssessmentHistoryService>();
		builder.Services.AddTransient<ViewModels.HistoryViewModel>();
		builder.Services.AddTransient<Views.HistoryPage>();

#if IOS
		Platforms.iOS.Handlers.KeyboardToolbarHandler.Register();
		Platforms.iOS.Handlers.ScrollTouchHandler.Register();
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();
		Services = app.Services;
		return app;
	}
}
