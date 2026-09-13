using System.Globalization;

namespace NewsScoreApp.Converters;

/// <summary>Converts a boolean "is this chip active" flag directly to a semantic color, keyed by
/// the chip's own score value (passed as ConverterParameter). Used by the generic form renderer
/// where chip values come from recipe JSON instead of a fixed NEWS2 enum.</summary>
public sealed class ActiveChipToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isActive = value is true;
        if (!isActive) return ThemeResources.Get("ChipInactiveBackground");

        int chipValue = parameter is int i ? i : int.Parse((string)parameter!, culture);
        return chipValue switch
        {
            0 => Application.Current!.Resources["RiskLowColor"],
            1 => Application.Current!.Resources["ChipScoreOneColor"],
            2 => Application.Current!.Resources["RiskMediumColor"],
            _ => Application.Current!.Resources["RiskHighColor"]
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts an "is selected" boolean to either the accent-highlighted or neutral choice
/// card background - the generic-form equivalent of <see cref="EqualToColorConverter"/>.</summary>
public sealed class SelectedToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool selected = value is true;
        return selected
            ? Application.Current!.Resources["Accent"]
            : ThemeResources.Get("ChipInactiveBackground");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Parses a "#RRGGBB" hex string (as stored in a recipe's RiskBand.Color) into a MAUI Color.
/// Falls back to a neutral gray when the value is null (form not yet complete).</summary>
public sealed class HexColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Colors.Gray;
        return Color.FromArgb(hex);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a field's IsInvalid flag to the input border's stroke color: red when invalid,
/// the normal theme border color otherwise. Used for the "obligatorisk felt" validation markers.</summary>
public sealed class InvalidToStrokeColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool invalid = value is true;
        return invalid ? Colors.Red : ThemeResources.Get("InputBorder");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InvalidToThicknessConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 2.0 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
