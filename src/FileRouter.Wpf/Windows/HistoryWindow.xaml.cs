using System.Windows;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Windows;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
