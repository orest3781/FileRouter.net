// UI smoke harness: runs the REAL WPF MainWindow, waits for WebView2 to render
// the first PDF, then drives commit / set-aside / undo through the same view
// model the buttons bind to. Proves the one thing unit tests can't: that
// Edge's PDF viewer releases the file handle so the move actually succeeds.
//
// WPF + WebView2 require an STA thread; a console app's implicit Main is MTA,
// so the whole UI runs on an explicit STA thread here.
//
// Exit 0 + "SMOKE PASS" on success; nonzero otherwise.

using System.Text.Json;
using System.Windows.Threading;
using FileRouter.Core;
using FileRouter.Wpf;

if (args.Length > 0 && args[0] == "dialogs") return DialogCheck.Run();
if (args.Length > 0 && args[0] == "reentrancy") return Reentrancy.Run();
if (args.Length > 0 && args[0] == "reset-demo") return DemoReset.Run();

// hard watchdog: never hang CI
_ = Task.Run(async () =>
{
    await Task.Delay(75_000);
    Console.WriteLine("SMOKE FAIL: 75s watchdog fired (UI never completed)");
    Environment.Exit(2);
});

return SmokeUi.RunSta(Drive,
    "SMOKE PASS — commit under live Edge viewer, set-aside, undo, history all OK",
    "SMOKE FAIL:");

static List<string> Drive()
{
    var failures = new List<string>();
    var root = Path.Combine(Path.GetTempPath(), "fr_smoke_" + Guid.NewGuid().ToString("N"));
    var inbox = Path.Combine(root, "inbox");
    var dest = Path.Combine(root, "invoices");
    var deferred = Path.Combine(root, "deferred");
    foreach (var d in new[] { inbox, dest, deferred }) Directory.CreateDirectory(d);

    MinimalPdf.Write(Path.Combine(inbox, "20240101--1111111111.pdf"), "ALPHA ONE");
    MinimalPdf.Write(Path.Combine(inbox, "20240102--2222222222.pdf"), "BETA TWO");
    MinimalPdf.Write(Path.Combine(inbox, "20240103--3333333333.pdf"), "GAMMA THREE");

    var cfgPath = Path.Combine(root, "config.json");
    File.WriteAllText(cfgPath, JsonSerializer.Serialize(new
    {
        inbox = inbox.Replace('\\', '/'),
        deferred = deferred.Replace('\\', '/'),
        history_db = "history.sqlite",
        naming_mode = "insert",
        sort = "filename_asc",
        uppercase_names = true,
        routes = new[]
        {
            new { label = "Invoices", path = dest.Replace('\\', '/'),
                  hotkey = "Ctrl+1", append_suffix = true, suffix = "_INVOICE" },
        },
    }));

    SmokeUi.Boot();
    var window = new MainWindow(Config.Load(cfgPath), cfgPath);
    var dialogs = new RecordingDialogs();
    window.Dialogs = dialogs;
    var shell = window.Shell;

    void Log(string m) { Console.WriteLine($"[{Environment.TickCount64 % 100000,6}] {m}"); Console.Out.Flush(); }

    async Task Wait(Func<bool> cond, string what, int ms = 15000)
    {
        Log("wait: " + what);
        var deadline = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < deadline)
        {
            if (cond()) { Log("  ok: " + what); return; }
            await Task.Delay(100);
        }
        Log("  TIMEOUT: " + what);
        failures.Add($"timeout waiting for: {what}");
    }

    window.Loaded += async (_, _) =>
    {
        try
        {
            await Wait(() => window.Pdf.Ready || window.Pdf.InitError != null, "WebView2 init");
            if (window.Pdf.InitError != null)
            {
                failures.Add("WebView2 init: " + window.Pdf.InitError.Split('\n')[0]);
                return;
            }

            // Initialize() (first scan) runs in the window's own Loaded handler;
            // wait for its Ready refresh so StartProcessing can't be undone by it
            await Wait(() => shell.CountLine.Length > 0, "initial scan");

            shell.StartProcessing();
            await Wait(() => window.Pdf.CurrentUrl.Contains("1111111111"), "first PDF in viewer");
            await Task.Delay(1500);   // worst-case: let Edge fully lock the file

            shell.TypedName = "SMITH JOHN";
            await shell.OnRouteAsync(0);
            var expected = Path.Combine(dest, "20240101-SMITH JOHN-1111111111_INVOICE.pdf");
            if (!File.Exists(expected))
                failures.Add("commit: filed file missing ("
                    + (dialogs.Warnings.FirstOrDefault() ?? "no error") + ")");
            if (File.Exists(Path.Combine(inbox, "20240101--1111111111.pdf")))
                failures.Add("commit: source still in inbox");

            await Wait(() => window.Pdf.CurrentUrl.Contains("2222222222"), "second PDF");
            await Task.Delay(500);
            await shell.OnSkipAsync();
            await Wait(() => File.Exists(Path.Combine(deferred, "20240102--2222222222.pdf")),
                "file set aside");

            shell.OnUndo();
            await Wait(() => File.Exists(Path.Combine(inbox, "20240102--2222222222.pdf")),
                "undo restored the file");

            if (shell.Session.RowIds.Count < 2)
                failures.Add("history: fewer than 2 rows recorded");
        }
        catch (Exception ex) { failures.Add("exception: " + ex.Message); }
        finally
        {
            Log("closing");
            window.Close();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    };

    window.Show();
    Dispatcher.Run();
    return failures;
}
