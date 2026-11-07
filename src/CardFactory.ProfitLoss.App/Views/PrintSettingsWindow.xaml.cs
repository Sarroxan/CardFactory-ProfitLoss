using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardFactory.ProfitLoss.App.Services;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>
/// The app's own print box (owner, 4 Nov 2026, concept M1), opened from "Change…" above the
/// preview. Shows the settings kept on this till, offers only what the chosen printer can do
/// (no Colour choice on a black and white printer, no Both sides on a one-sided one), and on
/// Print hands back the choices to print with and keep.
/// </summary>
public partial class PrintSettingsWindow : Window
{
    private readonly PrintChoices _choices;
    private readonly string? _normalPrinter;

    public PrintChoices Choices => _choices;

    /// <summary>Print only the first page this once (not kept).</summary>
    public bool FirstPageOnly => FirstPageChoice.IsChecked == true;

    public PrintSettingsWindow(string title, PrintChoices current, int pageCount, PageOrientation orientation)
    {
        InitializeComponent();
        TitleText.Text = title;
        _choices = current.Copy();
        _normalPrinter = PrintSettings.DefaultPrinterName();

        PrinterList.ItemsSource = PrintSettings.PrinterNames()
            .Select(name => string.Equals(name, _normalPrinter, StringComparison.OrdinalIgnoreCase) ? name + "  (normal printer)" : name)
            .ToList();
        PaperRun.Text = "A4 · " + (orientation == PageOrientation.Landscape ? "landscape" : "portrait") + " · fit to page";
        PagesRow.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        // Flipped so the back reads the right way up: short edge for landscape, long for portrait.
        BothSidesChoice.Content = orientation == PageOrientation.Landscape ? "Both sides (short edge)" : "Both sides (long edge)";

        ColourChoice.IsChecked = _choices.Colour;
        BlackWhiteChoice.IsChecked = !_choices.Colour;
        BothSidesChoice.IsChecked = _choices.BothSides;
        OneSideChoice.IsChecked = !_choices.BothSides;
        ShowCopies();
        ShowPrinter();
    }

    private void ShowPrinter()
    {
        var name = _choices.Printer ?? _normalPrinter;
        PrinterButton.Tag = string.IsNullOrWhiteSpace(name) ? "No printer found on this till" : name;

        // Offer only what this printer can do.
        var queue = PrintSettings.OpenQueue(_choices.Printer);
        var abilities = queue is null ? new PrinterAbilities(false, false) : PrintSettings.Abilities(queue);
        ColourRow.Visibility = abilities.Colour ? Visibility.Visible : Visibility.Collapsed;
        SidesRow.Visibility = abilities.BothSidesShortEdge ? Visibility.Visible : Visibility.Collapsed;
        var notes = new List<string>();
        if (!abilities.Colour) notes.Add("this printer prints in black and white only");
        if (!abilities.BothSidesShortEdge) notes.Add("it prints on one side only");
        NoteText.Text = notes.Count == 0
            ? "These are filled in for you and kept for next time on this till."
            : "These are kept for next time on this till · " + char.ToUpper(notes[0][0]) + string.Join(", ", notes)[1..] + ".";
    }

    private void ShowCopies() => CopiesText.Text = _choices.Copies.ToString();

    private void PrinterButton_Click(object sender, RoutedEventArgs e) => PrinterPopup.IsOpen = !PrinterPopup.IsOpen;

    private void PrinterChoice_Click(object sender, RoutedEventArgs e)
    {
        PrinterPopup.IsOpen = false;
        if (sender is not FrameworkElement { Tag: string shown }) return;
        var name = shown.EndsWith("  (normal printer)", StringComparison.Ordinal) ? shown[..^"  (normal printer)".Length] : shown;
        // The normal printer is kept as "the normal one", so a new normal printer is followed.
        _choices.Printer = string.Equals(name, _normalPrinter, StringComparison.OrdinalIgnoreCase) ? null : name;
        ShowPrinter();
    }

    private void FewerCopies_Click(object sender, RoutedEventArgs e)
    {
        _choices.Copies = Math.Max(1, _choices.Copies - 1);
        ShowCopies();
    }

    private void MoreCopies_Click(object sender, RoutedEventArgs e)
    {
        _choices.Copies = Math.Min(20, _choices.Copies + 1);
        ShowCopies();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        // A choice hidden because the printer can't do it keeps its setting for other printers.
        if (ColourRow.Visibility == Visibility.Visible) _choices.Colour = ColourChoice.IsChecked == true;
        if (SidesRow.Visibility == Visibility.Visible) _choices.BothSides = BothSidesChoice.IsChecked == true;
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
