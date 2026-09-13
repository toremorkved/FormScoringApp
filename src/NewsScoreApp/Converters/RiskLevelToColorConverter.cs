using System.Globalization;
using NewsScoreApp.Models;

namespace NewsScoreApp.Converters;

public sealed class RiskLevelToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = (RiskLevel)(value ?? RiskLevel.Incomplete);
        string key = level switch
        {
            RiskLevel.Low => "RiskLowColor",
            RiskLevel.LowMedium => "RiskLowMediumColor",
            RiskLevel.Medium => "RiskMediumColor",
            RiskLevel.High => "RiskHighColor",
            _ => "RiskNeutralColor"
        };
        return Application.Current!.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Same mapping as <see cref="RiskLevelToColorConverter"/> but for a soft translucent
/// card background instead of the solid badge color.</summary>
public sealed class RiskLevelToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = (RiskLevel)(value ?? RiskLevel.Incomplete);
        string key = level switch
        {
            RiskLevel.Low => "RiskLowBackground",
            RiskLevel.LowMedium => "RiskLowMediumBackground",
            RiskLevel.Medium => "RiskMediumBackground",
            RiskLevel.High => "RiskHighBackground",
            _ => "CardBackground"
        };
        if (key == "CardBackground")
            return ThemeResources.Get("CardBackground");
        return Application.Current!.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
