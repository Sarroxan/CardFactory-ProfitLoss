using System.Globalization;
using System.Windows.Data;

namespace CardFactory.ProfitLoss.App.Converters;

public sealed class ZeroBlankDecimalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not decimal number || number <= 0m)
        {
            return string.Empty;
        }

        return Format(number);
    }

    // Owner, 19 Oct 2026: the value commits as soon as the box holds a number in any form
    // ("1,450", "£1,450.00", "1450."), so the ▲/▼ beside it follows the typing. What is on
    // screen is left as typed: App turns off KeepTextBoxDisplaySynchronizedWithTextProperty,
    // which is what used to replace "1010." with "1010" mid-typing (Stage 6A.69) and made the
    // earlier version hold everything back until the box lost focus.
    // NumericTextBox_LostKeyboardFocus still tidies the text when you leave the box.
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        CardFactory.ProfitLoss.Core.Services.TargetEntry.TryRead(value?.ToString(), out var number)
            ? Math.Max(0m, number)
            : Binding.DoNothing;

    private static string Format(decimal number) => number.ToString("0.##", CultureInfo.InvariantCulture);
}
