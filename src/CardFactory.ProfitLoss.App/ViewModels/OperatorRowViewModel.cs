using System.Globalization;
using CardFactory.ProfitLoss.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardFactory.ProfitLoss.App.ViewModels;

public sealed class OperatorRowViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _salesEntry = string.Empty;
    private string _giftCardValueEntry = string.Empty;
    private string _giftCardQuantityEntry = string.Empty;
    private string _transactionsEntry = string.Empty;
    private string _unitsEntry = string.Empty;
    private decimal _countedSales;
    private int _countedUnits;
    private decimal _abv;
    private decimal _aub;
    private decimal _profitLoss;
    private string _abvState = "None";
    private string _aubState = "None";
    private string _profitLossState = "None";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string SalesEntry
    {
        get => _salesEntry;
        set
        {
            if (SetProperty(ref _salesEntry, value)) OnPropertyChanged(nameof(ShowTransactionsWarning));
        }
    }

    public string GiftCardValueEntry
    {
        get => _giftCardValueEntry;
        set
        {
            if (SetProperty(ref _giftCardValueEntry, value)) OnPropertyChanged(nameof(ShowTransactionsWarning));
        }
    }

    public string GiftCardQuantityEntry
    {
        get => _giftCardQuantityEntry;
        set
        {
            if (SetProperty(ref _giftCardQuantityEntry, value)) OnPropertyChanged(nameof(ShowTransactionsWarning));
        }
    }

    public string TransactionsEntry
    {
        get => _transactionsEntry;
        set
        {
            if (SetProperty(ref _transactionsEntry, value)) OnPropertyChanged(nameof(ShowTransactionsWarning));
        }
    }

    public string UnitsEntry
    {
        get => _unitsEntry;
        set
        {
            if (SetProperty(ref _unitsEntry, value)) OnPropertyChanged(nameof(ShowTransactionsWarning));
        }
    }

    public decimal CountedSales
    {
        get => _countedSales;
        private set => SetProperty(ref _countedSales, value);
    }

    public int CountedUnits
    {
        get => _countedUnits;
        private set => SetProperty(ref _countedUnits, value);
    }

    public decimal Abv
    {
        get => _abv;
        private set => SetProperty(ref _abv, value);
    }

    public decimal Aub
    {
        get => _aub;
        private set => SetProperty(ref _aub, value);
    }

    public decimal ProfitLoss
    {
        get => _profitLoss;
        private set
        {
            if (SetProperty(ref _profitLoss, value))
            {
                OnPropertyChanged(nameof(ProfitLossText));
            }
        }
    }

    public string ProfitLossText => ProfitLoss >= 0m
        ? $"+£{ProfitLoss:N2}"
        : $"-£{Math.Abs(ProfitLoss):N2}";

    public string AbvState
    {
        get => _abvState;
        private set => SetProperty(ref _abvState, value);
    }

    public string AubState
    {
        get => _aubState;
        private set => SetProperty(ref _aubState, value);
    }

    public string ProfitLossState
    {
        get => _profitLossState;
        private set => SetProperty(ref _profitLossState, value);
    }

    public bool ShowTransactionsWarning => ParseCount(TransactionsEntry) == 0
        && (ParseMoney(SalesEntry) > 0m
            || ParseMoney(GiftCardValueEntry) > 0m
            || ParseCount(GiftCardQuantityEntry) > 0
            || ParseCount(UnitsEntry) > 0);

    public OperatorInput ToInput() => new(
        Name?.Trim() ?? string.Empty,
        ParseMoney(SalesEntry),
        ParseMoney(GiftCardValueEntry),
        Math.Min(99, ParseCount(GiftCardQuantityEntry)),
        ParseCount(TransactionsEntry),
        ParseCount(UnitsEntry));

    public void LoadInput(OperatorInput input)
    {
        Name = input.Name ?? string.Empty;
        SalesEntry = FormatEntry(input.Sales);
        GiftCardValueEntry = FormatEntry(input.GiftCardValue);
        GiftCardQuantityEntry = FormatEntry(input.GiftCardQuantity);
        TransactionsEntry = FormatEntry(input.Transactions);
        UnitsEntry = FormatEntry(input.Units);
    }

    public void ApplyPerformance(OperatorPerformance performance, PerformanceTargets targets)
    {
        CountedSales = performance.CountedSales;
        CountedUnits = performance.CountedUnits;
        Abv = performance.Abv;
        Aub = performance.Aub;
        ProfitLoss = performance.ProfitLoss;

        var transactions = ParseCount(TransactionsEntry);

        AbvState = transactions > 0 && targets.AbvTarget > 0m
            ? (performance.Abv >= targets.AbvTarget ? "Good" : "Bad")
            : "None";

        AubState = transactions > 0 && targets.AubTarget > 0m
            ? (performance.Aub >= targets.AubTarget ? "Good" : "Bad")
            : "None";

        ProfitLossState = transactions > 0 && targets.AbvTarget > 0m
            ? (performance.ProfitLoss >= 0m ? "Good" : "Bad")
            : "None";
    }

    public void Reset()
    {
        Name = string.Empty;
        SalesEntry = string.Empty;
        GiftCardValueEntry = string.Empty;
        GiftCardQuantityEntry = string.Empty;
        TransactionsEntry = string.Empty;
        UnitsEntry = string.Empty;
        CountedSales = 0m;
        CountedUnits = 0;
        Abv = 0m;
        Aub = 0m;
        ProfitLoss = 0m;
        AbvState = "None";
        AubState = "None";
        ProfitLossState = "None";
    }

    private static decimal ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0m;
        }

        var clean = text.Trim().Replace("£", string.Empty).Replace(",", string.Empty);
        return decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0m, value)
            : 0m;
    }

    private static int ParseCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var clean = text.Trim().Replace(",", string.Empty);
        return int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value)
            : 0;
    }

    private static string FormatEntry(decimal value) => value <= 0m ? string.Empty : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatEntry(int value) => value <= 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
}
