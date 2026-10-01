using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CardFactory.ProfitLoss.App.Converters;

public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter?.ToString() ?? string.Empty : Binding.DoNothing;
}
