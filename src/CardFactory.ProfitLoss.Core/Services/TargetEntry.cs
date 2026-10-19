using System.Globalization;

namespace CardFactory.ProfitLoss.Core.Services;

/// <summary>
/// What a target box holds, as a number, while it is still being typed (owner, 19 Oct 2026:
/// the Sales ▲/▼ was slow to appear). "1450", "1,450", "£1,450.00", "1450." and ".5" all count
/// at once; before, only text already in its tidy form ("1450") did, and anything else waited
/// for the box to lose focus - which clicking an empty part of the window doesn't do.
/// </summary>
public static class TargetEntry
{
    /// <returns>False when the text isn't a number yet (or at all); blank is 0.</returns>
    public static bool TryRead(string? text, out decimal value)
    {
        value = 0m;
        var clean = (text ?? string.Empty).Replace("£", string.Empty).Replace(",", string.Empty).Trim();
        if (clean.Length == 0) return true;
        if (!decimal.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            return false;
        value = number;
        return true;
    }
}
