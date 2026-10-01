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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0m;
        }

        var clean = text.Trim().Replace("£", string.Empty).Replace(",", string.Empty);
        if (!decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number))
        {
            return Binding.DoNothing;
        }

        number = Math.Max(0m, number);

        // Stage 6A.69: these boxes bind with UpdateSourceTrigger=PropertyChanged, so every
        // keystroke went source -> Convert -> straight back into the TextBox. Typing "1010."
        // parsed to 1010, which Convert formatted as "1010", and WPF replaced the text -
        // deleting the decimal point the instant it was typed. "1010.80" and "4.62" were
        // therefore impossible to enter at all.
        //
        // While the typed text is not yet in its settled form, leave the source alone.
        // Convert then never runs, so nothing overwrites what is being typed. The value
        // commits as soon as the text is a settled number, and NumericTextBox_LostKeyboardFocus
        // normalises anything left mid-edit ("1010.", "1010.80", "1,010"), which commits it.
        if (!string.Equals(Format(number), text, StringComparison.Ordinal))
        {
            return Binding.DoNothing;
        }

        return number;
    }

    private static string Format(decimal number) => number.ToString("0.##", CultureInfo.InvariantCulture);
}
