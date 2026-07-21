using FileRouter.Core;

namespace FileRouter.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var cfgPath = args.Length >= 2 && args[0] == "--config"
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "config.json");

        Config cfg;
        try
        {
            cfg = Config.Load(cfgPath);
        }
        catch (ConfigException ex)
        {
            MessageBox.Show(ex.Message, "FileRouter — configuration problem",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // the MainForm ctor opens SQLite and takes the daily backup — a locked
        // or corrupt history DB must fail with a dialog, not a silent crash
        try
        {
            Application.Run(new MainForm(cfg, cfgPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show("FileRouter couldn't start:\n\n" + ex.Message,
                "FileRouter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
