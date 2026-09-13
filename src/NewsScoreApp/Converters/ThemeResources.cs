namespace NewsScoreApp.Converters;

/// <summary>Small helper so value converters can resolve a "NameLight"/"NameDark" resource
/// pair based on the effective app theme, without needing MAUI's XAML-only
/// AppThemeBinding markup extension (which can't be used from C# converters).</summary>
internal static class ThemeResources
{
    public static object Get(string baseName)
    {
        bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        string key = baseName + (dark ? "Dark" : "Light");
        return Application.Current!.Resources[key];
    }
}
