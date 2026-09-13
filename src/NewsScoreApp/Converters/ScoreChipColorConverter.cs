using System.Globalization;

namespace NewsScoreApp.Converters;

/// <summary>Highlights a score-legend chip (3/2/1/0) when it equals the currently computed
/// sub-score for that parameter, otherwise renders it as an inactive/neutral chip.</summary>
public sealed class ScoreChipColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var score = value as int?;
        var chipValue = int.Parse((string)parameter!, culture);
        if (score is null || score.Value != chipValue)
            return ThemeResources.Get("ChipInactiveBackground");

        return chipValue switch
        {
            0 => Application.Current!.Resources["RiskLowColor"],
            1 => Application.Current!.Resources["ChipScoreOneColor"],
            2 => Application.Current!.Resources["RiskMediumColor"],
            3 => Application.Current!.Resources["RiskHighColor"],
            _ => Application.Current!.Resources["ChipInactiveBackground"]
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a computed sub-score (0-3) directly to its semantic color, used for the
/// small breakdown chips in the score summary (as opposed to <see cref="ScoreChipColorConverter"/>
/// which only highlights the legend chip matching the current score).</summary>
public sealed class ScoreToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as int?) switch
        {
            0 => Application.Current!.Resources["RiskLowColor"],
            1 => Application.Current!.Resources["ChipScoreOneColor"],
            2 => Application.Current!.Resources["RiskMediumColor"],
            3 => Application.Current!.Resources["RiskHighColor"],
            _ => ThemeResources.Get("ChipInactiveBackground")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
