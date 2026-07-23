using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Views;

public partial class ProcessingView : UserControl
{
    private ShellViewModel? _shell;

    public ProcessingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_shell is not null) _shell.RequestNameFocus -= FocusNameBox;
            _shell = DataContext as ShellViewModel;
            if (_shell is not null) _shell.RequestNameFocus += FocusNameBox;
        };
    }

    private void FocusNameBox()
    {
        NameBox.Focus();
        NameBox.CaretIndex = NameBox.Text.Length;
    }

    private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shell is null) return;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Tab when !shift:
                var before = _shell.TypedName.Length;
                if (_shell.CompleteNextWord())
                {
                    // Python-parity: the word Tab just added is SELECTED, so
                    // each press visibly claims one more word (and typing
                    // over it discards just that word)
                    NameBox.Select(before, NameBox.Text.Length - before);
                    e.Handled = true;
                }
                break;
            case Key.Tab when shift:
                _shell.DropLastWord();
                NameBox.CaretIndex = NameBox.Text.Length;
                e.Handled = true;
                break;
            case Key.Enter:
                _shell.OnEnter();
                e.Handled = true;
                break;
            case Key.Escape when _shell.HasSuggestions:
                // first Esc closes the popup; a second one stops the session
                _shell.DismissSuggestions();
                e.Handled = true;
                break;
        }
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _shell?.DismissSuggestions();

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // programmatic sets (Tab-complete, clear-on-advance) park the caret at
        // 0; the filing loop always wants it at the end
        if (NameBox.CaretIndex == 0 && NameBox.Text.Length > 0)
            NameBox.CaretIndex = NameBox.Text.Length;
    }

    private void SuggestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shell is null || e.AddedItems.Count == 0) return;
        _shell.TypedName = (string)e.AddedItems[0]!;
        _shell.DismissSuggestions();
        FocusNameBox();
    }
}
