using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardFactory.ProfitLoss.App.ViewModels;

public sealed class BranchHourlyRowViewModel : ObservableObject
{
    private string _timeBand;
    private string _salesEntry = string.Empty;
    private string _transactionsEntry = string.Empty;
    private string _unitsEntry = string.Empty;
    private decimal _percentOfSales;
    private bool _isStrongestHour;
    private bool _isReportDriven;
    private decimal _reportedAbv;
    private decimal _reportedAub;
    private bool _loading;

    public BranchHourlyRowViewModel(string timeBand) => _timeBand = timeBand;

    public string TimeBand
    {
        get => _timeBand;
        set => SetProperty(ref _timeBand, value);
    }

    public string SalesEntry
    {
        get => _salesEntry;
        set
        {
            if (!SetProperty(ref _salesEntry, value)) return;
            if (!_loading) IsReportDriven = false;
            OnPropertyChanged(nameof(Sales));
            OnPropertyChanged(nameof(Abv));
            OnPropertyChanged(nameof(HasTransactionsWarning));
        }
    }

    public string TransactionsEntry
    {
        get => _transactionsEntry;
        set
        {
            if (!SetProperty(ref _transactionsEntry, value)) return;
            if (!_loading) IsReportDriven = false;
            OnPropertyChanged(nameof(Transactions));
            OnPropertyChanged(nameof(Abv));
            OnPropertyChanged(nameof(Aub));
            OnPropertyChanged(nameof(HasTransactionsWarning));
        }
    }

    public string UnitsEntry
    {
        get => _unitsEntry;
        set
        {
            if (!SetProperty(ref _unitsEntry, value)) return;
            if (!_loading) IsReportDriven = false;
            OnPropertyChanged(nameof(Units));
            OnPropertyChanged(nameof(Aub));
            OnPropertyChanged(nameof(HasTransactionsWarning));
        }
    }

    public decimal Sales => ParseMoney(SalesEntry);
    public int Transactions => ParseCount(TransactionsEntry);
    public int Units => ParseCount(UnitsEntry);
    public decimal Abv => IsReportDriven ? _reportedAbv : Transactions > 0 ? Sales / Transactions : 0m;
    public decimal Aub => IsReportDriven ? _reportedAub : Transactions > 0 ? (decimal)Units / Transactions : 0m;
    public bool HasTransactionsWarning => Transactions == 0 && (Sales > 0m || Units > 0);

    public decimal PercentOfSales
    {
        get => _percentOfSales;
        internal set => SetProperty(ref _percentOfSales, value);
    }

    public bool IsStrongestHour
    {
        get => _isStrongestHour;
        internal set => SetProperty(ref _isStrongestHour, value);
    }

    public bool IsReportDriven
    {
        get => _isReportDriven;
        private set
        {
            if (!SetProperty(ref _isReportDriven, value)) return;
            OnPropertyChanged(nameof(Abv));
            OnPropertyChanged(nameof(Aub));
        }
    }

    public void Load(decimal sales, int transactions, int units)
    {
        _loading = true;
        try
        {
            IsReportDriven = false;
            SalesEntry = FormatEntry(sales);
            TransactionsEntry = FormatEntry(transactions);
            UnitsEntry = FormatEntry(units);
            PercentOfSales = 0m;
            _reportedAbv = 0m;
            _reportedAub = 0m;
        }
        finally { _loading = false; }
    }

    public void LoadReport(decimal sales, int transactions, int units, decimal abv, decimal aub, decimal percentOfSales)
    {
        _loading = true;
        try
        {
            _reportedAbv = abv;
            _reportedAub = aub;
            SalesEntry = FormatEntry(sales);
            TransactionsEntry = FormatEntry(transactions);
            UnitsEntry = FormatEntry(units);
            PercentOfSales = percentOfSales;
            IsReportDriven = true;
            OnPropertyChanged(nameof(Abv));
            OnPropertyChanged(nameof(Aub));
        }
        finally { _loading = false; }
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

    private static string FormatEntry(decimal value) => value <= 0m ? string.Empty : value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string FormatEntry(int value) => value <= 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
}
