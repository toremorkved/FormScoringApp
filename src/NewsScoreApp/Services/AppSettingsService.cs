using Microsoft.Maui.Storage;

namespace NewsScoreApp.Services;

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>Wraps <see cref="Preferences"/> for the two user-facing toggles shown in the
/// prototype's top bar: auto-advance between fields, and light/dark theme override.</summary>
public sealed class AppSettingsService
{
    private const string AutoAdvanceKey = "auto_advance_enabled";
    private const string ThemeKey = "theme_preference";

    public bool AutoAdvanceEnabled
    {
        get => Preferences.Default.Get(AutoAdvanceKey, true);
        set => Preferences.Default.Set(AutoAdvanceKey, value);
    }

    public AppThemePreference Theme
    {
        get => Enum.TryParse<AppThemePreference>(Preferences.Default.Get(ThemeKey, nameof(AppThemePreference.System)), out var t) ? t : AppThemePreference.System;
        set => Preferences.Default.Set(ThemeKey, value.ToString());
    }

    public void ApplyTheme()
    {
        Application.Current!.UserAppTheme = Theme switch
        {
            AppThemePreference.Light => AppTheme.Light,
            AppThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }
}
