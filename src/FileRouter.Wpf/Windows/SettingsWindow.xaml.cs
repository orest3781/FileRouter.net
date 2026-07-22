using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_vm.TryBuildResult()) DialogResult = true;
    }

    /// <summary>The hotkey box records the actual keystroke instead of free
    /// text — what you press is exactly what will file.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.Back or Key.Delete:
                box.SetCurrentValue(TextBox.TextProperty, "");
                UpdateSource(box);
                return;
            case Key.Tab:
                e.Handled = false;   // keep keyboard navigation working
                return;
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin:
                return;              // modifiers alone aren't a hotkey yet
        }
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
            _ => key.ToString(),
        });
        box.SetCurrentValue(TextBox.TextProperty, string.Join("+", parts));
        UpdateSource(box);
    }

    private static void UpdateSource(TextBox box) =>
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    private void OnRouteSwatch(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedRoute is { } r && sender is Button { Tag: string color })
            r.Color = color;
    }

    private void OnWatchSwatch(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedWatch is { } w && sender is Button { Tag: string color })
            w.Color = color;
    }

    private void OnSizeUp(object sender, RoutedEventArgs e) => NudgeSize(+1);
    private void OnSizeDown(object sender, RoutedEventArgs e) => NudgeSize(-1);

    /// <summary>Step the base text size from its effective value (blank = the
    /// 14pt default), clamped to the valid 6-72 range.</summary>
    private void NudgeSize(int delta)
    {
        var current = int.TryParse(_vm.UiFontSizeText.Trim(), out var n) ? n : 14;
        _vm.UiFontSizeText = Math.Clamp(current + delta, 6, 72).ToString();
    }

    private void OnAddPassword(object sender, RoutedEventArgs e)
    {
        if (!_vm.AddPassword(NewPwLabel.Text, NewPwValue.Password))
        {
            PwHint.Text = "Give it a name and a password first.";
            return;
        }
        PwHint.Text = "";
        NewPwLabel.Text = "";
        NewPwValue.Password = "";
    }
}
