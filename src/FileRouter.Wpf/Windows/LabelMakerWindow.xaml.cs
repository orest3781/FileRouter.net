using System.Windows;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Windows;

public partial class LabelMakerWindow : Window
{
    public LabelMakerWindow(LabelMakerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // client edits are settings — they stick even without printing
        Closing += (_, _) => vm.Persist();
    }
}
