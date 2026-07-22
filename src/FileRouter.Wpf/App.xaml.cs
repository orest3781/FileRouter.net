using System.Windows;
using FileRouter.Core;

namespace FileRouter.Wpf;

/// <summary>Startup: parse --config, load Config with a readable error dialog,
/// boot the theme, show the shell. Uncaught exceptions append to crash.log
/// beside the config and surface as a dialog — the app survives (the Python
/// original's excepthook behavior).</summary>
public partial class App : Application
{
    private string _cfgPath = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash(ex.Exception);
            MessageBox.Show(
                "Something went wrong — details were written to crash.log.\n\n" +
                ex.Exception.Message, "FileRouter", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            LogCrash(ex.ExceptionObject as Exception);

        _cfgPath = e.Args.Length >= 2 && e.Args[0] == "--config"
            ? e.Args[1]
            : Path.Combine(AppContext.BaseDirectory, "config.json");

        Config cfg;
        try
        {
            cfg = Config.Load(_cfgPath);
        }
        catch (ConfigException ex)
        {
            MessageBox.Show(ex.Message, "FileRouter — configuration problem",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Theme.ThemeManager.Start(this, cfg.Theme);
        ApplyFont(this, cfg);

        try
        {
            var window = new MainWindow(cfg, _cfgPath);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            // the shell ctor opens SQLite and takes the daily backup — a locked
            // or corrupt history DB must fail with a dialog, not a silent crash
            LogCrash(ex);
            MessageBox.Show("FileRouter couldn't start:\n\n" + ex.Message,
                "FileRouter", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>ui_font_family / ui_font_size land in the AppFontFamily and
    /// AppFontSize resources every window's style consumes.</summary>
    public static void ApplyFont(Application app, Config cfg)
    {
        app.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily(
            string.IsNullOrWhiteSpace(cfg.UiFontFamily) ? "Segoe UI" : cfg.UiFontFamily);
        app.Resources["AppFontSize"] = cfg.UiFontSize == 0 ? 14.0 : (double)cfg.UiFontSize;
    }

    private void LogCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_cfgPath)) ?? ".";
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch (Exception) { /* crash logging must never crash */ }
    }
}
