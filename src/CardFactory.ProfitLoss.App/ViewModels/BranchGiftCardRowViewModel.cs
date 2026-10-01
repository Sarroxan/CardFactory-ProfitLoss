using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardFactory.ProfitLoss.App.ViewModels;

public sealed class BranchGiftCardRowViewModel : ObservableObject
{
    private string _item = string.Empty;
    private string _operatorName = string.Empty;
    private string _quantityEntry = string.Empty;
    private string _salesEntry = string.Empty;

    public string Item
    {
        get => _item;
        set => SetProperty(ref _item, value);
    }

    public string OperatorName
    {
        get => _operatorName;
        set => SetProperty(ref _operatorName, value);
    }

    public string QuantityEntry
    {
        get => _quantityEntry;
        set
        {
            if (!SetProperty(ref _quantityEntry, value)) return;
            OnPropertyChanged(nameof(Quantity));
        }
    }

    public string SalesEntry
    {
        get => _salesEntry;
        set
        {
            if (!SetProperty(ref _salesEntry, value)) return;
            OnPropertyChanged(nameof(Sales));
        }
    }

    public int Quantity => ParseCount(QuantityEntry);
    public decimal Sales => ParseMoney(SalesEntry);

    public void Load(string? item, string? operatorName, int quantity, decimal sales)
    {
        Item = item ?? string.Empty;
        OperatorName = operatorName ?? string.Empty;
        QuantityEntry = quantity <= 0 ? string.Empty : quantity.ToString(CultureInfo.InvariantCulture);
        SalesEntry = sales <= 0m ? string.Empty : sales.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static decimal ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        var clean = text.Trim().Replace("£", string.Empty).Replace(",", string.Empty);
        return decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) ? Math.Max(0m, value) : 0m;
    }

    private static int ParseCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var clean = text.Trim().Replace(",", string.Empty);
        return int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value) : 0;
    }
}
