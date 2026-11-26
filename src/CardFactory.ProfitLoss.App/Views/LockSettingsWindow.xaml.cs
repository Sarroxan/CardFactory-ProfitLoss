using System.Windows;
using System.Windows.Input;
using CardFactory.ProfitLoss.App.Services;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>Menu › Lock (owner, 26 Nov 2026, concept P1): turn the lock on or off, choose after how
/// long unused, and set or change the 4-digit PIN.</summary>
public partial class LockSettingsWindow : Window
{
    private readonly LockChoices _choices;

    public LockSettingsWindow()
    {
        InitializeComponent();
        _choices = LockSettings.Load();
        LockOnToggle.IsChecked = _choices.Enabled;
        (_choices.Minutes <= 30 ? After30 : _choices.Minutes >= 120 ? After120 : After60).IsChecked = true;
        ChangePinButton.Content = _choices.HasPin ? "Change PIN" : "Set PIN";
        Loaded += (_, _) => UpdateRows();
    }

    /// <summary>After "Forgot the PIN?": opens ready to type a new PIN.</summary>
    public LockSettingsWindow(bool newPin) : this()
    {
        if (newPin) Loaded += (_, _) => { LockOnToggle.IsChecked = true; ShowPinBoxes(); };
    }

    private bool On => LockOnToggle.IsChecked == true;

    private void Choice_Changed(object sender, RoutedEventArgs e) => UpdateRows();

    private void UpdateRows()
    {
        if (AfterRow is null || PinRow is null) return;
        AfterRow.Visibility = On ? Visibility.Visible : Visibility.Collapsed;
        PinRow.Visibility = On ? Visibility.Visible : Visibility.Collapsed;
        // Turning it on for the first time needs a PIN straight away.
        if (On && !_choices.HasPin) ShowPinBoxes();
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ChangePin_Click(object sender, RoutedEventArgs e) => ShowPinBoxes();

    private void ShowPinBoxes()
    {
        PinBoxes.Visibility = Visibility.Visible;
        ChangePinButton.Visibility = Visibility.Collapsed;
        Dispatcher.InvokeAsync(() => PinBox.Focus());
    }

    private void Pin_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (On)
        {
            if (PinBoxes.Visibility == Visibility.Visible)
            {
                var pin = PinBox.Password;
                if (!LockSettings.IsPin(pin)) { ShowError("The PIN must be 4 numbers."); return; }
                if (pin != PinAgainBox.Password) { ShowError("The two PINs don't match - type them again."); return; }
                LockSettings.SetPin(_choices, pin);
            }
            _choices.Minutes = After30.IsChecked == true ? 30 : After120.IsChecked == true ? 120 : 60;
        }

        _choices.Enabled = On;
        LockSettings.Save(_choices);
        DialogResult = true;
        Close();
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
        PinBox.Clear();
        PinAgainBox.Clear();
        PinBox.Focus();
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
