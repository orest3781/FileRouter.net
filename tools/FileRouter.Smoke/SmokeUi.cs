using System.Windows;
using FileRouter.Wpf;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.Theme;

/// <summary>Shared WPF bootstrap for the smoke modes: loads the real App.xaml
/// resources (styles, converters, fonts) and the theme without running the
/// app's startup path, so windows constructed here resolve their resources
/// exactly as in production.</summary>
internal static class SmokeUi
{
    public static App Boot()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();   // App.xaml resources
        ThemeManager.Apply(app, dark: false);
        return app;
    }

    public static int RunSta(Func<List<string>> drive, string passLine, string failHead)
    {
        List<string> failures = new();
        var ui = new Thread(() => failures = drive());
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        if (failures.Count == 0)
        {
            Console.WriteLine(passLine);
            return 0;
        }
        Console.WriteLine(failHead);
        foreach (var f in failures) Console.WriteLine("  * " + f);
        return 1;
    }
}

/// <summary>Modal dialogs recorded instead of shown — a blocked message loop
/// would hang the harness. Replaces the WinForms SuppressDialogs hook that
/// leaked into the production form.</summary>
internal sealed class RecordingDialogs : IDialogService
{
    public List<string> Warnings { get; } = new();
    public void Warn(string message, string title) => Warnings.Add(message);
    public void Info(string message, string title) { }
    public bool Confirm(string message, string title) => true;
    public string? AskSaveFile(string filter, string suggested) => null;
    public string? AskOpenFile(string filter) => null;
    public string? BrowseFolder(string? startAt) => null;
}
