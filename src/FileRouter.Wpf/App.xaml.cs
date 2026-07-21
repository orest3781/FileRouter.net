using System.Windows;

namespace FileRouter.Wpf;

/// <summary>Startup lands in OnStartup (wired in the shell task): parse
/// --config, load Config with a readable error dialog, boot the theme,
/// compose services, show the main window.</summary>
public partial class App : Application
{
}
