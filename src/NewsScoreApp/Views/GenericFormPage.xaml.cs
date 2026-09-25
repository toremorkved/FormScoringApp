using Microsoft.Extensions.DependencyInjection;
using NewsScoreApp.ViewModels;
#if IOS
using Foundation;
using UIKit;
#endif

namespace NewsScoreApp.Views;

public partial class GenericFormPage : ContentPage
{
    private readonly GenericFormViewModel _viewModel;

    // Populated as the BindableLayout materializes each field's DataTemplate (see
    // OnFieldCardLoaded/OnFieldEntryFocused below) - keyed by field id since the generic renderer
    // has no static x:Name per field like the old NEWS2-only MainPage did.
    private readonly Dictionary<string, VisualElement> _cards = new();
    private readonly Dictionary<string, VisualElement> _entries = new();
    private VisualElement? _lastFocusedEntry;
    private bool _initialized;

    public GenericFormPage() : this(MauiProgram.Services.GetRequiredService<GenericFormViewModel>())
    {
    }

    public GenericFormPage(GenericFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
#if IOS
        MenuButton.Loaded += OnMenuButtonLoaded;
#endif
    }

#if IOS
    private void OnMenuButtonLoaded(object? sender, EventArgs e)
    {
        ConfigureNativeMenu();
    }

    private void ConfigureNativeMenu()
    {
        if (MenuButton.Handler?.PlatformView is not UIButton button) return;

        var actions = new List<UIAction>();
        if (_viewModel.CurrentRecipe?.IsScored == true)
        {
            actions.Add(UIAction.Create("Historikk", null, "news.history", OnNativeHistorySelected));
        }

        actions.Add(UIAction.Create("Innstillinger", null, "news.settings", OnNativeSettingsSelected));
        button.Menu = UIMenu.Create("Meny", null, new NSString("news.menu"), UIMenuOptions.DisplayInline, actions.ToArray());
        button.ShowsMenuAsPrimaryAction = true;
    }

    private void OnNativeHistorySelected(UIAction action) => _ = OpenHistoryAsync();

    private void OnNativeSettingsSelected(UIAction action) => _ = OpenSettingsAsync();
#endif

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Re-subscribe every time the page appears (not just the first time) - it disappears
        // and reappears whenever the modal SettingsPage is pushed/popped (e.g. to switch the
        // active recipe), and OnDisappearing always unsubscribes. Without this, auto-advance's
        // scroll-and-focus silently stops working after the user visits Settings once.
        _viewModel.ScrollAndFocusRequested -= OnScrollAndFocusRequested;
        _viewModel.ScrollAndFocusRequested += OnScrollAndFocusRequested;
        _viewModel.InfoRequested -= OnInfoRequested;
        _viewModel.InfoRequested += OnInfoRequested;

    #if IOS
        ConfigureNativeMenu();
    #endif

        if (_initialized) return;
        _initialized = true;
        await _viewModel.InitializeAsync();
    #if IOS
        ConfigureNativeMenu();
    #endif
    }

    protected override void OnDisappearing()
    {
        _viewModel.ScrollAndFocusRequested -= OnScrollAndFocusRequested;
        _viewModel.InfoRequested -= OnInfoRequested;
        base.OnDisappearing();
    }

    // Every time the recipe changes, BindableLayout tears down and rebuilds the field templates,
    // so the lookup dictionaries must be cleared and re-populated as the new cards load.
    private void OnFieldCardLoaded(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: GenericFieldViewModel field } border) return;
        _cards[field.Definition.Id] = border;
    }

    private void OnFieldEntryLoaded(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: GenericFieldViewModel field } el) return;
        _entries[field.Definition.Id] = el;
    }

    private void OnFieldEntryFocused(object? sender, FocusEventArgs e)
    {
        if (sender is not VisualElement { BindingContext: GenericFieldViewModel field } el) return;
        _entries[field.Definition.Id] = el;
        _lastFocusedEntry = el;
    }

    private void OnScrollAndFocusRequested(string fieldId)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (fieldId == GenericFormViewModel.ScoreSummaryFieldId)
            {
                // Last field on the form was just completed - land on the score summary card
                // and dismiss the keyboard instead of trying to focus a (non-existent) next field.
                await RootScroll.ScrollToAsync(ScoreSummaryCard, ScrollToPosition.Center, animated: true);
                _lastFocusedEntry?.Unfocus();
                return;
            }

            if (fieldId == GenericFormViewModel.ConfirmationFieldId)
            {
                // Submit just succeeded - the confirmation card only becomes visible now (its
                // IsVisible binding flips in the same tick), so give layout a beat to size it
                // before scrolling, otherwise ScrollToAsync targets its old (collapsed) bounds.
                _lastFocusedEntry?.Unfocus();
                await Task.Delay(80);
                await RootScroll.ScrollToAsync(ConfirmationCard, ScrollToPosition.End, animated: true);
                return;
            }

            if (!_cards.TryGetValue(fieldId, out var card))
                return;

            await RootScroll.ScrollToAsync(card, ScrollToPosition.Center, animated: true);

            if (_entries.TryGetValue(fieldId, out var entry))
            {
                entry.Focus();
            }
            else
            {
                // Landing on a choice-card group (no keyboard) - dismiss whatever was focused.
                _lastFocusedEntry?.Unfocus();
            }
        });
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        var settingsPage = MauiProgram.Services.GetRequiredService<SettingsPage>();
        var nav = new NavigationPage(settingsPage);
#if IOS
        Platforms.iOS.Handlers.SheetPresentationHelper.PrepareHalfSheet(nav);
#endif
        await Navigation.PushModalAsync(nav);
#if IOS
        Platforms.iOS.Handlers.SheetPresentationHelper.ApplyHalfSheetDetents();
#endif
    }

    private async void OnAiFillClicked(object? sender, EventArgs e)
    {
        if (_viewModel.CurrentRecipe is null) return;
        var aiPage = MauiProgram.Services.GetRequiredService<AiFillPage>();
        aiPage.Prepare(_viewModel.CurrentRecipe);
        await Navigation.PushModalAsync(new NavigationPage(aiPage));
    }

    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        var options = new List<string>();
        if (_viewModel.CurrentRecipe?.IsScored == true)
            options.Add("Historikk");
        options.Add("Innstillinger");

        var choice = await DisplayActionSheetAsync("Meny", "Avbryt", null, options.ToArray());
        switch (choice)
        {
            case "Historikk":
                await OpenHistoryAsync();
                break;
            case "Innstillinger":
                await OpenSettingsAsync();
                break;
        }
    }

    private async Task OpenSettingsAsync()
    {
        var settingsPage = MauiProgram.Services.GetRequiredService<SettingsPage>();
        var nav = new NavigationPage(settingsPage);
#if IOS
        Platforms.iOS.Handlers.SheetPresentationHelper.PrepareHalfSheet(nav);
#endif
        await Navigation.PushModalAsync(nav);
#if IOS
        Platforms.iOS.Handlers.SheetPresentationHelper.ApplyHalfSheetDetents();
#endif
    }

    private async Task OpenHistoryAsync()
    {
        if (_viewModel.CurrentRecipe is null) return;
        var historyPage = MauiProgram.Services.GetRequiredService<HistoryPage>();
        await historyPage.PrepareAsync(_viewModel.CurrentRecipe);
        await Navigation.PushModalAsync(new NavigationPage(historyPage));
    }

    private async void OnHistoryClicked(object? sender, EventArgs e)
    {
        await OpenHistoryAsync();
    }

    private void OnInfoRequested(string title, string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await DisplayAlertAsync(title, message, "Lukk");
        });
    }
}
