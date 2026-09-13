using System.Globalization;

namespace NewsScoreApp.Converters;

/// <summary>Generic equality highlighter for the two-option (Romluft/Oksygen) and five-option
/// (ACVPU) choice cards: returns the "selected" resource when the bound enum matches the
/// converter parameter, otherwise the "unselected" resource.</summary>
public sealed class EqualToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool match = value?.ToString() == parameter?.ToString();
        return ThemeResources.Get(match ? "ChoiceSelectedBackground" : "ChoiceUnselectedBackground");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EqualToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IntGreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
