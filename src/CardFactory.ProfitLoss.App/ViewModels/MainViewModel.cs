using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CardFactory.ProfitLoss.Core.Models;
using CardFactory.ProfitLoss.Core.Services;
using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.Storage.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardFactory.ProfitLoss.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly TeamPerformanceCalculator _teamCalculator = new();
    private bool _suspendAutoCalculate;

    private string _storeLocation = string.Empty;
    private DateTime? _selectedDate = DateTime.Today;
    private DateTime? _rangeStartDate = DateTime.Today.AddDays(-6);
    private string _historicalTargetMode = "Daily";
    private decimal _salesTarget;
    private decimal _abvTarget;
    private decimal _aubTarget;
    private decimal _totalSales;
    private decimal _actualAbv;
    private decimal _actualAub;
    private decimal _totalProfitLoss;
    private string _totalProfitLossText = "+£0.00";
    private string _totalProfitLossState = "None";
    private decimal _totalGiftCards;
    private int _totalGiftCardQuantity;
    private int _totalTransactions;
    private int _totalCountedUnits;
    private string _salesVarianceText = "—";
    private string _salesVarianceState = "None";
    private string _salesSummaryText = "—";
    private string _abvSummaryText = "—";
    private string _aubSummaryText = "—";
    private string _abvSummaryState = "None";
    private string _aubSummaryState = "None";
    private string _statusText = "Ready";
    private string _flooidStatusText = "Not signed in";
    private string _flooidStatusState = "None";
    private string _flooidLastUpdatedText = "Not retrieved yet";
    private string _branchDataMessage = string.Empty;
    private bool _branchReportDriven;
    private decimal _branchReportedAbv;
    private decimal _branchReportedAub;
    private decimal _branchTotalSales;
    private int _branchTotalTransactions;
    private int _branchTotalUnits;
    private decimal _branchActualAbv;
    private decimal _branchActualAub;
    private decimal _branchGiftCardSales;
    private int _branchGiftCardQuantity;
    private string _branchBestHourText = "—";

    public MainViewModel()
    {
        HistoricalTargetModes = new[] { "Daily", "Date Range" };
        AddOperatorCommand = new RelayCommand(AddOperator);
        RemoveOperatorCommand = new RelayCommand<OperatorRowViewModel>(RemoveOperator);
        AddBranchGiftCardCommand = new RelayCommand(AddBranchGiftCard);
        RemoveBranchGiftCardCommand = new RelayCommand<BranchGiftCardRowViewModel>(RemoveBranchGiftCard);
        Operators.CollectionChanged += Operators_CollectionChanged;
        BranchHours.CollectionChanged += BranchHours_CollectionChanged;
        BranchGiftCards.CollectionChanged += BranchGiftCards_CollectionChanged;

        Operators.Add(new OperatorRowViewModel());
        BranchGiftCards.Add(new BranchGiftCardRowViewModel());
        Calculate();
        RecalculateBranch();
    }

    public ObservableCollection<OperatorRowViewModel> Operators { get; } = new();
    public ObservableCollection<BranchHourlyRowViewModel> BranchHours { get; } = new();
    public ObservableCollection<BranchGiftCardRowViewModel> BranchGiftCards { get; } = new();
    public IReadOnlyList<string> HistoricalTargetModes { get; }
    public IRelayCommand AddOperatorCommand { get; }
    public IRelayCommand<OperatorRowViewModel> RemoveOperatorCommand { get; }
    public IRelayCommand AddBranchGiftCardCommand { get; }
    public IRelayCommand<BranchGiftCardRowViewModel> RemoveBranchGiftCardCommand { get; }

    public string StoreLocation
    {
        get => _storeLocation;
        set { if (SetProperty(ref _storeLocation, value)) OnPropertyChanged(nameof(HeaderStoreText)); }
    }

    // Stage 6B.21: what the header shows for the store. Blank until Flooid says, rather
    // than a placeholder that could be mistaken for a real outlet.
    public string HeaderStoreText => string.IsNullOrWhiteSpace(_storeLocation) ? string.Empty : _storeLocation;

    private string _headerUpdatedText = string.Empty;

    // Stage 6B.21: replaces what the section labels used to say. Since 6B.11 removed them
    // the time of the last retrieval has had nowhere to live, and the overlay only exists
    // while a retrieval is running.
    public string HeaderUpdatedText { get => _headerUpdatedText; set => SetProperty(ref _headerUpdatedText, value); }

    // Freshness (design 1A): when the figures were last retrieved. The header reads
    // "updated 12 min ago"; after an hour the dot and text turn amber (IsStale).
    private DateTime? _lastUpdatedAt;
    private bool _isStale;

    public DateTime? LastUpdatedAt
    {
        get => _lastUpdatedAt;
        set { if (SetProperty(ref _lastUpdatedAt, value)) { OnPropertyChanged(nameof(HasUpdateTime)); RefreshFreshness(); } }
    }

    public bool HasUpdateTime => LastUpdatedAt.HasValue;
    public bool IsStale { get => _isStale; private set => SetProperty(ref _isStale, value); }

    /// <summary>Re-words "updated … ago". Called on a timer by the window, and when the time changes.</summary>
    public void RefreshFreshness()
    {
        if (LastUpdatedAt is not { } at) { HeaderUpdatedText = string.Empty; IsStale = false; return; }
        var age = DateTime.Now - at;
        var minutes = (int)Math.Max(0, age.TotalMinutes);
        HeaderUpdatedText = minutes < 1 ? "· updated just now"
            : minutes < 60 ? "· updated " + minutes + " min ago"
            : "· updated " + (minutes / 60) + " hr " + (minutes % 60) + " min ago";
        IsStale = minutes >= 60;
    }

    /// <summary>Copy summary (design 8A): today's figures as plain text to paste anywhere.</summary>
    public string BuildSummaryText()
    {
        var lines = new List<string>();
        var date = HistoricalTargetMode == "Date Range" && RangeStartDate is { } from && SelectedDate is { } to
            ? from.ToString("ddd d MMM") + " to " + to.ToString("ddd d MMM yyyy")
            : (SelectedDate ?? DateTime.Today).ToString("ddd d MMM yyyy");
        lines.Add((string.IsNullOrWhiteSpace(HeaderStoreText) ? "Profit & Loss" : HeaderStoreText) + " · " + date);
        lines.Add("Sales: £" + TotalSales.ToString("N2") + (SalesTarget > 0m ? " (target £" + SalesTarget.ToString("N2") + ")" : string.Empty));
        lines.Add("ABV: £" + ActualAbv.ToString("N2") + (AbvTarget > 0m ? " (target £" + AbvTarget.ToString("N2") + ")" : string.Empty));
        lines.Add("AUB: " + ActualAub.ToString("N2") + (AubTarget > 0m ? " (target " + AubTarget.ToString("N2") + ")" : string.Empty));
        if (AbvTarget > 0m) lines.Add("P&L: " + TotalProfitLossText);

        if (TopPerformerChips.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Top performers");
            foreach (var chip in TopPerformerChips)
                lines.Add("★ " + chip.Label.Replace("BEST ", string.Empty) + ": " + chip.Names + " " + chip.Value);
        }

        var busiest = BranchHours.FirstOrDefault(row => row.IsStrongestHour);
        if (busiest is not null)
        {
            lines.Add(string.Empty);
            lines.Add("Busiest hour: " + busiest.TimeBand + " (" + busiest.PercentOfSales.ToString("N1") + "% of sales)");
        }
        return string.Join(Environment.NewLine, lines);
    }

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set { if (SetProperty(ref _selectedDate, value)) OnPropertyChanged(nameof(RangeDayCountText)); }
    }

    // Stage 6A.75: in Date Range mode the PERIOD control is an explicit range rather
    // than a single week-ending date. SelectedDate is the LAST day of that range;
    // RangeStartDate is the first. Daily mode ignores RangeStartDate entirely.
    public DateTime? RangeStartDate
    {
        get => _rangeStartDate;
        set { if (SetProperty(ref _rangeStartDate, value)) OnPropertyChanged(nameof(RangeDayCountText)); }
    }

    // Shown beside the range so a long pull is never silent.
    // Stage 6A.89: keep the range coherent. The two date buttons already refuse an
    // inverted range, but a restored state can produce one without going through
    // them: 6A.73 forces a fresh session onto today's date, so a start saved in the
    // future ends up later than the end. Called after the startup restore and again
    // before every pull, so no route reaches Flooid with "from" after "to".
    public void NormaliseDateRange()
    {
        var end = (SelectedDate ?? DateTime.Today).Date;
        var start = (RangeStartDate ?? end.AddDays(-6)).Date;
        if (start > end) RangeStartDate = end.AddDays(-6);
    }

    public string RangeDayCountText
    {
        get
        {
            var start = (_rangeStartDate ?? DateTime.Today).Date;
            var end = (_selectedDate ?? DateTime.Today).Date;
            if (end < start) return "\u2014";
            var days = (end - start).Days + 1;
            return days == 1 ? "1 day" : days + " days";
        }
    }

    public string HistoricalTargetMode
    {
        get => _historicalTargetMode;
        set
        {
            if (!SetProperty(ref _historicalTargetMode, value)) return;
            // Switching into Date Range seeds it as the seven days ending on the
            // currently selected date, so it behaves exactly as the old week-ending
            // control did until one end is deliberately changed.
            if (string.Equals(value, "Date Range", StringComparison.OrdinalIgnoreCase))
                RangeStartDate = (_selectedDate ?? DateTime.Today).Date.AddDays(-6);
        }
    }
    public decimal SalesTarget
    {
        get => _salesTarget;
        set { if (SetProperty(ref _salesTarget, Math.Max(0m, value))) CalculateIfReady(); }
    }
    public decimal AbvTarget
    {
        get => _abvTarget;
        set { if (SetProperty(ref _abvTarget, Math.Max(0m, value))) CalculateIfReady(); }
    }
    public decimal AubTarget
    {
        get => _aubTarget;
        set { if (SetProperty(ref _aubTarget, Math.Max(0m, value))) CalculateIfReady(); }
    }
    public decimal TotalSales { get => _totalSales; private set => SetProperty(ref _totalSales, value); }
    public decimal ActualAbv { get => _actualAbv; private set => SetProperty(ref _actualAbv, value); }
    public decimal ActualAub { get => _actualAub; private set => SetProperty(ref _actualAub, value); }
    public decimal TotalProfitLoss { get => _totalProfitLoss; private set => SetProperty(ref _totalProfitLoss, value); }
    public string TotalProfitLossText { get => _totalProfitLossText; private set => SetProperty(ref _totalProfitLossText, value); }
    public string TotalProfitLossState { get => _totalProfitLossState; private set => SetProperty(ref _totalProfitLossState, value); }
    public decimal TotalGiftCards { get => _totalGiftCards; private set => SetProperty(ref _totalGiftCards, value); }
    public int TotalGiftCardQuantity { get => _totalGiftCardQuantity; private set => SetProperty(ref _totalGiftCardQuantity, value); }
    public int TotalTransactions { get => _totalTransactions; private set => SetProperty(ref _totalTransactions, value); }
    public int TotalCountedUnits { get => _totalCountedUnits; private set => SetProperty(ref _totalCountedUnits, value); }
    public string SalesVarianceText { get => _salesVarianceText; private set => SetProperty(ref _salesVarianceText, value); }
    public string SalesVarianceState { get => _salesVarianceState; private set => SetProperty(ref _salesVarianceState, value); }
    public string SalesSummaryText { get => _salesSummaryText; private set => SetProperty(ref _salesSummaryText, value); }
    public string AbvSummaryText { get => _abvSummaryText; private set => SetProperty(ref _abvSummaryText, value); }
    public string AubSummaryText { get => _aubSummaryText; private set => SetProperty(ref _aubSummaryText, value); }
    public string AbvSummaryState { get => _abvSummaryState; private set => SetProperty(ref _abvSummaryState, value); }
    public string AubSummaryState { get => _aubSummaryState; private set => SetProperty(ref _aubSummaryState, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string FlooidStatusText { get => _flooidStatusText; private set => SetProperty(ref _flooidStatusText, value); }
    public string FlooidStatusState { get => _flooidStatusState; private set => SetProperty(ref _flooidStatusState, value); }
    public string FlooidLastUpdatedText { get => _flooidLastUpdatedText; private set => SetProperty(ref _flooidLastUpdatedText, value); }
    public string BranchDataMessage { get => _branchDataMessage; private set => SetProperty(ref _branchDataMessage, value); }
    public decimal BranchTotalSales { get => _branchTotalSales; private set => SetProperty(ref _branchTotalSales, value); }
    public int BranchTotalTransactions { get => _branchTotalTransactions; private set => SetProperty(ref _branchTotalTransactions, value); }
    public int BranchTotalUnits { get => _branchTotalUnits; private set => SetProperty(ref _branchTotalUnits, value); }
    public decimal BranchActualAbv { get => _branchActualAbv; private set => SetProperty(ref _branchActualAbv, value); }
    public decimal BranchActualAub { get => _branchActualAub; private set => SetProperty(ref _branchActualAub, value); }
    public decimal BranchGiftCardSales { get => _branchGiftCardSales; private set => SetProperty(ref _branchGiftCardSales, value); }
    public int BranchGiftCardQuantity { get => _branchGiftCardQuantity; private set => SetProperty(ref _branchGiftCardQuantity, value); }
    public string BranchBestHourText { get => _branchBestHourText; private set => SetProperty(ref _branchBestHourText, value); }

    public void SetFlooidStatus(string statusText, string state)
    {
        FlooidStatusText = string.IsNullOrWhiteSpace(statusText) ? "Flooid" : statusText;
        FlooidStatusState = string.IsNullOrWhiteSpace(state) ? "None" : state;
    }

    public void SetFlooidLastUpdated(DateTime timestamp)
    {
        FlooidLastUpdatedText = $"Updated {timestamp:dd/MM/yyyy HH:mm}";
    }

    public void SetBranchDataMessage(string? message) => BranchDataMessage = message?.Trim() ?? string.Empty;

    public void ApplyFlooidTeamReport(IReadOnlyList<OperatorInput> operators)
    {
        ArgumentNullException.ThrowIfNull(operators);
        _suspendAutoCalculate = true;
        try
        {
            Operators.Clear();
            foreach (var input in operators)
            {
                var row = new OperatorRowViewModel();
                row.LoadInput(input);
                Operators.Add(row);
            }

            if (Operators.Count == 0)
                Operators.Add(new OperatorRowViewModel());
        }
        finally
        {
            _suspendAutoCalculate = false;
        }

        Calculate();
        StatusText = "Team Performance updated from Flooid";
    }

    // Stage 6A.77: Branch Performance is a single day's hourly breakdown and
    // nothing else. Since 6A.75 it is always pulled for one day, so the period
    // branch this used to carry could never run; it has been removed rather
    // than left as a path that looks live.
    public void ApplyBranchPerformance(BranchPerformanceReportParseResult report)
    {
        _suspendAutoCalculate = true;
        try
        {
            var wanted = SelectedDate?.Date;
            var day = report.Days.FirstOrDefault(d => wanted is not null && d.Date.Date == wanted.Value)
                ?? report.Days.FirstOrDefault();

            // A report with no per-day breakdown still has its period totals, which
            // for a single-day pull are that day's figures.
            var hours = day?.Hours ?? report.PeriodSummary;
            var total = day?.Total ?? report.PeriodTotal;

            BranchHours.Clear();
            foreach (var item in hours)
            {
                if (item.Sales == 0m) continue;

                var row = new BranchHourlyRowViewModel(item.TimeBand);
                row.LoadReport(item.Sales, item.Transactions, item.Units, item.Abv, item.Aub, item.PercentOfSales);
                BranchHours.Add(row);
            }

            _branchReportDriven = true;
            _branchReportedAbv = total.Abv;
            _branchReportedAub = total.Aub;
            BranchDataMessage = BranchHours.Count == 0
                ? "No Branch Performance data found for this period"
                : string.Empty;
        }
        finally
        {
            _suspendAutoCalculate = false;
        }

        RecalculateBranch();
        StatusText = "Branch Performance updated from Flooid";
    }

    public void ApplyGiftCardReport(GiftCardReportParseResult report, bool updateTeam = true)
    {
        _suspendAutoCalculate = true;
        try
        {
            BranchGiftCards.Clear();
            if (report.Items.Count > 0)
            {
                foreach (var item in report.Items)
                {
                    var row = new BranchGiftCardRowViewModel();
                    var label = string.IsNullOrWhiteSpace(item.ProductCode)
                        ? item.Description
                        : $"{item.ProductCode} · {item.Description}";
                    row.Load(label, item.OperatorName, item.Quantity, item.Value);
                    BranchGiftCards.Add(row);
                }
            }
            else
            {
                foreach (var item in report.Operators)
                {
                    var row = new BranchGiftCardRowViewModel();
                    row.Load("Gift Cards", item.Name, item.Quantity, item.Value);
                    BranchGiftCards.Add(row);
                }
            }

            if (BranchGiftCards.Count == 0) BranchGiftCards.Add(new BranchGiftCardRowViewModel());

            if (updateTeam)
            {
                foreach (var giftCard in report.Operators)
                {
                    var teamRow = Operators.FirstOrDefault(row =>
                        string.Equals(NormaliseName(row.Name), NormaliseName(giftCard.Name), StringComparison.Ordinal));
                    if (teamRow is null) continue;
                    teamRow.GiftCardQuantityEntry = giftCard.Quantity <= 0 ? string.Empty : giftCard.Quantity.ToString();
                    teamRow.GiftCardValueEntry = giftCard.Value <= 0m ? string.Empty : giftCard.Value.ToString("0.##");
                }
            }
        }
        finally
        {
            _suspendAutoCalculate = false;
        }

        Calculate();
        RecalculateBranch();
    }

    public CalculatorStateDocument CaptureState()
    {
        return new CalculatorStateDocument
        {
            StoreLocation = StoreLocation,
            SelectedDate = SelectedDate,
            RangeStartDate = RangeStartDate,
            HistoricalTargetMode = HistoricalTargetMode,
            SalesTarget = SalesTarget,
            AbvTarget = AbvTarget,
            AubTarget = AubTarget,
            Operators = Operators.Select(row =>
            {
                var input = row.ToInput();
                return new OperatorStateDocument
                {
                    Name = input.Name,
                    Sales = input.Sales,
                    GiftCardValue = input.GiftCardValue,
                    GiftCardQuantity = input.GiftCardQuantity,
                    Transactions = input.Transactions,
                    Units = input.Units
                };
            }).ToList(),
            BranchHourlyRows = BranchHours.Select(row => new BranchHourlyStateDocument
            {
                TimeBand = row.TimeBand,
                Sales = row.Sales,
                Transactions = row.Transactions,
                Units = row.Units,
                Abv = row.Abv,
                Aub = row.Aub,
                PercentOfSales = row.PercentOfSales,
                IsReportDriven = row.IsReportDriven
            }).ToList(),
            BranchGiftCards = BranchGiftCards.Select(row => new BranchGiftCardStateDocument
            {
                Item = row.Item,
                OperatorName = row.OperatorName,
                Quantity = row.Quantity,
                Sales = row.Sales
            }).ToList(),
            BranchReportDriven = _branchReportDriven,
            BranchReportedAbv = _branchReportedAbv,
            BranchReportedAub = _branchReportedAub
        };
    }

    public void ApplyState(CalculatorStateDocument state)
    {
        _suspendAutoCalculate = true;
        try
        {
            StoreLocation = state.StoreLocation ?? string.Empty;
            SelectedDate = state.SelectedDate ?? DateTime.Today;
            // Stage 6A.76: the mode formerly called "Weekly" is now "Date Range".
            // State files written before the rename carry the old name; map it across
            // rather than letting it silently fall back to Daily.
            var savedMode = state.HistoricalTargetMode ?? string.Empty;
            if (string.Equals(savedMode, "Weekly", StringComparison.OrdinalIgnoreCase))
                savedMode = "Date Range";
            HistoricalTargetMode = HistoricalTargetModes.Contains(savedMode) ? savedMode : "Daily";
            // After the mode, because switching into Date Range reseeds it.
            RangeStartDate = state.RangeStartDate ?? (state.SelectedDate ?? DateTime.Today).Date.AddDays(-6);
            SalesTarget = state.SalesTarget;
            AbvTarget = state.AbvTarget;
            AubTarget = state.AubTarget;

            Operators.Clear();
            foreach (var item in state.Operators ?? new List<OperatorStateDocument>())
            {
                var row = new OperatorRowViewModel();
                row.LoadInput(new OperatorInput(
                    item.Name ?? string.Empty,
                    item.Sales,
                    item.GiftCardValue,
                    Math.Clamp(item.GiftCardQuantity, 0, 99),
                    Math.Max(0, item.Transactions),
                    Math.Max(0, item.Units)));
                Operators.Add(row);
            }

            if (Operators.Count == 0)
            {
                Operators.Add(new OperatorRowViewModel());
            }

            BranchHours.Clear();
            foreach (var item in state.BranchHourlyRows ?? new List<BranchHourlyStateDocument>())
            {
                if (string.IsNullOrWhiteSpace(item.TimeBand)) continue;
                var row = new BranchHourlyRowViewModel(item.TimeBand);
                if (item.IsReportDriven)
                {
                    row.LoadReport(item.Sales, Math.Max(0, item.Transactions), Math.Max(0, item.Units), item.Abv, item.Aub, item.PercentOfSales);
                }
                else
                {
                    row.Load(item.Sales, Math.Max(0, item.Transactions), Math.Max(0, item.Units));
                }
                BranchHours.Add(row);
            }
            BranchGiftCards.Clear();
            foreach (var item in state.BranchGiftCards ?? new List<BranchGiftCardStateDocument>())
            {
                var row = new BranchGiftCardRowViewModel();
                row.Load(item.Item, item.OperatorName, Math.Max(0, item.Quantity), item.Sales);
                BranchGiftCards.Add(row);
            }
            if (BranchGiftCards.Count == 0) BranchGiftCards.Add(new BranchGiftCardRowViewModel());
            _branchReportDriven = state.BranchReportDriven;
            _branchReportedAbv = state.BranchReportedAbv;
            _branchReportedAub = state.BranchReportedAub;
        }
        finally
        {
            _suspendAutoCalculate = false;
        }

        Calculate();
        RecalculateBranch();
        StatusText = "Saved calculator state loaded";
    }

    public void SetStatus(string text) => StatusText = text;

    public void ResetAppSettings()
    {
        _suspendAutoCalculate = true;
        try
        {
            StoreLocation = string.Empty;
            SelectedDate = DateTime.Today;
            HistoricalTargetMode = "Daily";
            RangeStartDate = DateTime.Today.AddDays(-6);
            SalesTarget = 0m;
            AbvTarget = 0m;
            AubTarget = 0m;
        }
        finally
        {
            _suspendAutoCalculate = false;
        }

        Calculate();
        StatusText = "App settings reset to defaults";
    }

    private void AddOperator()
    {
        Operators.Add(new OperatorRowViewModel());
        StatusText = "Team member added";
    }

    private void RemoveOperator(OperatorRowViewModel? row)
    {
        if (row is null) return;
        if (Operators.Count == 1) row.Reset();
        else Operators.Remove(row);
        StatusText = "Team member removed";
    }

    private void AddDefaultBranchHours()
    {
        for (var hour = 8; hour < 20; hour++)
        {
            BranchHours.Add(new BranchHourlyRowViewModel($"{hour:00}:00–{hour + 1:00}:00"));
        }
    }

    private void AddBranchGiftCard()
    {
        BranchGiftCards.Add(new BranchGiftCardRowViewModel());
        StatusText = "Gift card row added";
    }

    private void RemoveBranchGiftCard(BranchGiftCardRowViewModel? row)
    {
        if (row is null) return;
        if (BranchGiftCards.Count == 1)
        {
            row.Load(string.Empty, string.Empty, 0, 0m);
        }
        else
        {
            BranchGiftCards.Remove(row);
        }
        RecalculateBranchIfReady();
        StatusText = "Gift card row removed";
    }

    private void BranchHours_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BranchHourlyRowViewModel row in e.OldItems) row.PropertyChanged -= BranchHour_PropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (BranchHourlyRowViewModel row in e.NewItems) row.PropertyChanged += BranchHour_PropertyChanged;
        }
        RecalculateBranchIfReady();
    }

    private void BranchGiftCards_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BranchGiftCardRowViewModel row in e.OldItems) row.PropertyChanged -= BranchGiftCard_PropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (BranchGiftCardRowViewModel row in e.NewItems) row.PropertyChanged += BranchGiftCard_PropertyChanged;
        }
        RecalculateBranchIfReady();
    }

    private void BranchHour_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BranchHourlyRowViewModel.SalesEntry)
            or nameof(BranchHourlyRowViewModel.TransactionsEntry)
            or nameof(BranchHourlyRowViewModel.UnitsEntry))
        {
            if (!_suspendAutoCalculate) _branchReportDriven = false;
            RecalculateBranchIfReady();
        }
    }

    private void BranchGiftCard_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BranchGiftCardRowViewModel.QuantityEntry)
            or nameof(BranchGiftCardRowViewModel.SalesEntry))
        {
            RecalculateBranchIfReady();
        }
    }

    private void RecalculateBranchIfReady()
    {
        if (!_suspendAutoCalculate) RecalculateBranch();
    }

    private void RecalculateBranch()
    {
        BranchTotalSales = BranchHours.Sum(row => row.Sales);
        BranchTotalTransactions = BranchHours.Sum(row => row.Transactions);
        BranchTotalUnits = BranchHours.Sum(row => row.Units);
        BranchActualAbv = _branchReportDriven
            ? _branchReportedAbv
            : BranchTotalTransactions > 0 ? BranchTotalSales / BranchTotalTransactions : 0m;
        BranchActualAub = _branchReportDriven
            ? _branchReportedAub
            : BranchTotalTransactions > 0 ? (decimal)BranchTotalUnits / BranchTotalTransactions : 0m;
        BranchGiftCardQuantity = BranchGiftCards.Sum(row => row.Quantity);
        BranchGiftCardSales = BranchGiftCards.Sum(row => row.Sales);

        var maxSales = BranchHours.Count == 0 ? 0m : BranchHours.Max(row => row.Sales);
        BranchBestHourText = maxSales > 0m
            ? BranchHours.First(row => row.Sales == maxSales).TimeBand
            : "—";

        foreach (var row in BranchHours)
        {
            if (!_branchReportDriven || !row.IsReportDriven)
                row.PercentOfSales = BranchTotalSales > 0m ? (row.Sales / BranchTotalSales) * 100m : 0m;
            row.IsStrongestHour = maxSales > 0m && row.Sales == maxSales;
        }

        // Bars in the % Sales column (design 5A): each hour against the busiest one.
        var maxShare = BranchHours.Count == 0 ? 0m : BranchHours.Max(row => row.PercentOfSales);
        foreach (var row in BranchHours)
            row.BarFraction = maxShare > 0m ? (double)(row.PercentOfSales / maxShare) : 0d;
    }

    private void Operators_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OperatorRowViewModel row in e.OldItems)
            {
                row.PropertyChanged -= Operator_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OperatorRowViewModel row in e.NewItems)
            {
                row.PropertyChanged += Operator_PropertyChanged;
            }
        }

        CalculateIfReady();
    }

    private void Operator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OperatorRowViewModel.SalesEntry)
            or nameof(OperatorRowViewModel.GiftCardValueEntry)
            or nameof(OperatorRowViewModel.GiftCardQuantityEntry)
            or nameof(OperatorRowViewModel.TransactionsEntry)
            or nameof(OperatorRowViewModel.UnitsEntry))
        {
            CalculateIfReady();
        }
    }

    private void CalculateIfReady()
    {
        if (!_suspendAutoCalculate)
        {
            Calculate();
        }
    }

    private void Calculate()
    {
        var targets = new PerformanceTargets(SalesTarget, AbvTarget, AubTarget);
        var inputs = Operators.Select(row => row.ToInput()).ToList();
        var team = _teamCalculator.Calculate(inputs, targets);

        for (var index = 0; index < Operators.Count && index < team.Operators.Count; index++)
        {
            Operators[index].ApplyPerformance(team.Operators[index], targets);
        }

        TotalSales = team.TotalCountedSales;
        TotalGiftCards = team.TotalGiftCardValue;
        TotalGiftCardQuantity = team.TotalGiftCardQuantity;
        TotalTransactions = team.TotalTransactions;
        TotalCountedUnits = team.TotalCountedUnits;
        ActualAbv = team.ActualAbv;
        ActualAub = team.ActualAub;
        TotalProfitLoss = team.TotalProfitLoss;
        TotalProfitLossText = FormatSignedMoney(team.TotalProfitLoss);
        TotalProfitLossState = targets.AbvTarget > 0m && team.TotalTransactions > 0
            ? GetState(team.TotalProfitLoss)
            : "None";

        SalesVarianceText = FormatSignedMoney(team.SalesVariance);
        SalesVarianceState = GetState(team.SalesVariance);
        SalesSummaryText = FormatArrowMoney(team.SalesVariance);
        AbvSummaryText = FormatArrowMoney(team.AbvVariance);
        AbvSummaryState = GetState(team.AbvVariance);
        AubSummaryText = FormatArrowNumber(team.AubVariance);
        AubSummaryState = GetState(team.AubVariance);
        StatusText = "Calculations are up to date";

        // Top performers strip (design C1): best ABV, AUB and P&L among people with enough
        // transactions to count. Rules in Core/Services/TopPerformers.
        TopPerformerChips = TopPerformers.Find(team.Operators, targets.AbvTarget > 0m)
            .Select(t => new TopPerformerChip(
                t.Measure,
                string.Join(" & ", t.Names),
                t.Measure == "P&L" ? FormatSignedMoney(t.Value) : t.Measure == "ABV" ? $"£{t.Value:N2}" : $"{t.Value:N2}",
                t.Measure == "P&L" ? GetState(t.Value) : "None"))
            .ToList();
    }

    private IReadOnlyList<TopPerformerChip> _topPerformerChips = Array.Empty<TopPerformerChip>();

    public IReadOnlyList<TopPerformerChip> TopPerformerChips
    {
        get => _topPerformerChips;
        private set
        {
            if (SetProperty(ref _topPerformerChips, value)) OnPropertyChanged(nameof(HasTopPerformers));
        }
    }

    public bool HasTopPerformers => TopPerformerChips.Count > 0;

    // ---------------------------------------------------------------- Reports (design C)
    // Voids, returns and no-sales per person, from the Refunds, Voids & No Sales report the
    // Team figures already come from. Values of £20 or more are flagged as worth a look.
    public const decimal ReportFlagValue = 20m;

    public ObservableCollection<ReportPersonRow> ReportRows { get; } = new();

    private decimal _reportVoidsValue, _reportRefundsValue;
    private int _reportVoidsQuantity, _reportRefundsQuantity, _reportNoSales;

    public decimal ReportVoidsValue { get => _reportVoidsValue; private set => SetProperty(ref _reportVoidsValue, value); }
    public int ReportVoidsQuantity { get => _reportVoidsQuantity; private set => SetProperty(ref _reportVoidsQuantity, value); }
    public decimal ReportRefundsValue { get => _reportRefundsValue; private set => SetProperty(ref _reportRefundsValue, value); }
    public int ReportRefundsQuantity { get => _reportRefundsQuantity; private set => SetProperty(ref _reportRefundsQuantity, value); }
    public int ReportNoSales { get => _reportNoSales; private set => SetProperty(ref _reportNoSales, value); }
    public bool HasReportData => ReportRows.Count > 0;

    // The summary box's "Highest" column and its £20 ring, as on the Daily Briefing.
    private string _reportVoidsTop = "–", _reportRefundsTop = "–", _reportNoSalesTop = "–";
    private bool _reportVoidsFlagged, _reportRefundsFlagged;

    public string ReportVoidsTop { get => _reportVoidsTop; private set => SetProperty(ref _reportVoidsTop, value); }
    public string ReportRefundsTop { get => _reportRefundsTop; private set => SetProperty(ref _reportRefundsTop, value); }
    public string ReportNoSalesTop { get => _reportNoSalesTop; private set => SetProperty(ref _reportNoSalesTop, value); }
    public bool ReportVoidsFlagged { get => _reportVoidsFlagged; private set => SetProperty(ref _reportVoidsFlagged, value); }
    public bool ReportRefundsFlagged { get => _reportRefundsFlagged; private set => SetProperty(ref _reportRefundsFlagged, value); }

    public void ApplyActivityReport(IReadOnlyList<OperatorActivityRecord> activity)
    {
        ReportRows.Clear();
        foreach (var person in activity
                     .OrderByDescending(p => p.VoidsValue + p.TotalRefundsValue)
                     .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            ReportRows.Add(new ReportPersonRow(
                person.Name,
                person.VoidsQuantity,
                person.VoidsValue,
                person.RefundsQuantity,
                person.TotalRefundsValue,
                person.NoSales,
                person.VoidsValue >= ReportFlagValue,
                person.TotalRefundsValue >= ReportFlagValue));
        }

        ReportVoidsValue = activity.Sum(p => p.VoidsValue);
        ReportVoidsQuantity = activity.Sum(p => p.VoidsQuantity);
        ReportRefundsValue = activity.Sum(p => p.TotalRefundsValue);
        ReportRefundsQuantity = activity.Sum(p => p.RefundsQuantity);
        ReportNoSales = activity.Sum(p => p.NoSales);
        ReportVoidsFlagged = ReportVoidsValue >= ReportFlagValue;
        ReportRefundsFlagged = ReportRefundsValue >= ReportFlagValue;
        ReportVoidsTop = Highest(activity, p => p.VoidsValue, v => $"£{v:N2}");
        ReportRefundsTop = Highest(activity, p => p.TotalRefundsValue, v => $"£{v:N2}");
        ReportNoSalesTop = Highest(activity, p => p.NoSales, v => $"{v:0}");
        OnPropertyChanged(nameof(HasReportData));
    }

    public void ClearActivityReport() => ApplyActivityReport(Array.Empty<OperatorActivityRecord>());

    // "Name · £27.95" for whoever has the most; names joined when tied; a dash when nobody has any.
    private static string Highest(IReadOnlyList<OperatorActivityRecord> activity, Func<OperatorActivityRecord, decimal> measure, Func<decimal, string> format)
    {
        if (activity.Count == 0) return "–";
        var top = activity.Max(measure);
        if (top <= 0m) return "–";
        var names = activity.Where(p => measure(p) == top).Select(p => p.Name).ToList();
        return $"{string.Join(" & ", names)} · {format(top)}";
    }

    private static string NormaliseName(string? value) =>
        string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static string GetState(decimal? value) => value is null ? "None" : value.Value >= 0m ? "Good" : "Bad";
    private static string FormatSignedMoney(decimal? value) => value is null ? "—" : value.Value >= 0m ? $"+£{value.Value:N2}" : $"-£{Math.Abs(value.Value):N2}";
    private static string FormatArrowMoney(decimal? value) => value is null ? "—" : value.Value >= 0m ? $"▲ £{Math.Abs(value.Value):N2}" : $"▼ £{Math.Abs(value.Value):N2}";
    private static string FormatArrowNumber(decimal? value) => value is null ? "—" : value.Value >= 0m ? $"▲ {Math.Abs(value.Value):N2}" : $"▼ {Math.Abs(value.Value):N2}";
}

/// <summary>One chip in the top performers strip. State Good / Bad colours a P&amp;L value.</summary>
public sealed record TopPerformerChip(string Label, string Names, string Value, string State);

/// <summary>One person's row in the Reports section.</summary>
public sealed record ReportPersonRow(
    string Name,
    int VoidsQuantity,
    decimal VoidsValue,
    int RefundsQuantity,
    decimal RefundsValue,
    int NoSales,
    bool VoidsFlagged,
    bool RefundsFlagged);
