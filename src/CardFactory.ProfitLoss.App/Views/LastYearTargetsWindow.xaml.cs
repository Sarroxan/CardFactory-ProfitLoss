using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using CardFactory.ProfitLoss.App.Services;
using CardFactory.ProfitLoss.App.ViewModels;
using CardFactory.ProfitLoss.Core.Services;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>Menu → Settings → Targets from last year (owner, 16 Oct 2026, concept T2).</summary>
public partial class LastYearTargetsWindow : Window
{
    private readonly LastYearTargets? _lastResult;
    private bool _loading = true;

    /// <param name="lastResult">Last year's figures from the most recent Refresh, if any, so the
    /// working out can be shown with the percentages being typed.</param>
    public LastYearTargetsWindow(bool enabled, decimal abvPercent, decimal aubPercent, LastYearTargets? lastResult)
    {
        InitializeComponent();
        _lastResult = lastResult;
        OnOption.IsChecked = enabled;
        OffOption.IsChecked = !enabled;
        AbvPercentBox.Text = Plain(abvPercent);
        AubPercentBox.Text = Plain(aubPercent);
        _loading = false;
        Refresh();
    }

    public bool Enabled => OnOption.IsChecked == true;
    public decimal AbvPercent { get; private set; }
    public decimal AubPercent { get; private set; }

    private void OnOff_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) Refresh();
    }

    private void Percent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) Refresh();
    }

    /// <summary>Checks both percentages and redraws the working out.</summary>
    private bool Refresh()
    {
        PercentPanel.Opacity = Enabled ? 1.0 : 0.45;
        PercentPanel.IsEnabled = Enabled;

        var abvOk = LastYearTargetSettings.TryPercent(AbvPercentBox.Text, out var abv);
        var aubOk = LastYearTargetSettings.TryPercent(AubPercentBox.Text, out var aub);
        if (abvOk) AbvPercent = abv;
        if (aubOk) AubPercent = aub;

        var problem = !abvOk ? "ABV" : !aubOk ? "AUB" : null;
        PercentError.Text = problem is null
            ? string.Empty
            : $"{problem}: type a percentage from {LastYearTargetCalculator.MinPercent:0} to +{LastYearTargetCalculator.MaxPercent:0}, e.g. +10 or 0.";
        PercentError.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = problem is null || !Enabled;

        WorkingText.Inlines.Clear();
        if (_lastResult is { } r)
        {
            var preview = LastYearTargetCalculator.Calculate(r.FromDate, r.ToDate, 1, r.LastYearAbv, r.LastYearAub, AbvPercent, AubPercent)!;
            WorkingText.Inlines.Add(new Run(MainViewModel.LastYearPeriodText(r)
                + (r.FromDate.Date == r.ToDate.Date ? " (same day last year)" : " (same days last year)")));
            WorkingText.Inlines.Add(new LineBreak());
            AddSum("ABV ", "£" + r.LastYearAbv.ToString("N2"), Multiplier(AbvPercent), "£" + preview.AbvTarget.ToString("N2"));
            WorkingText.Inlines.Add(new LineBreak());
            AddSum("AUB ", r.LastYearAub.ToString("N2"), Multiplier(AubPercent), preview.AubTarget.ToString("N2"));
        }
        else
        {
            WorkingText.Inlines.Add(new Run("Last year's figures and the working out appear here after the next Refresh."));
        }

        return problem is null;
    }

    private void AddSum(string label, string lastYear, string multiplier, string target)
    {
        var navy = (System.Windows.Media.Brush)FindResource("CfDeepBlue");
        WorkingText.Inlines.Add(new Run(label));
        WorkingText.Inlines.Add(new Run(lastYear) { FontWeight = FontWeights.Bold, Foreground = navy });
        WorkingText.Inlines.Add(new Run(" × " + multiplier + " = "));
        WorkingText.Inlines.Add(new Run(target) { FontWeight = FontWeights.Bold, Foreground = navy });
    }

    private static string Multiplier(decimal percent) => (1m + percent / 100m).ToString("0.00##", CultureInfo.InvariantCulture);

    private static string Plain(decimal percent) =>
        (percent >= 0 ? "+" : "-") + Math.Abs(percent).ToString("0.##", CultureInfo.InvariantCulture);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Enabled && !Refresh()) return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
