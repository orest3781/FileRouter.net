using System.Text.Json;
using System.Windows.Threading;
using FileRouter.Core;
using FileRouter.Wpf;

/// <summary>Regression harness for the commit reentrancy bug: firing the route
/// commit twice in quick succession must file ONE document, not mislabel the
/// next one with the first's typed name. The unit suite covers this headless
/// (FilingLoopTests.DoubleFireCommitsExactlyOnce); this proves it against the
/// real WebView2 release timing.</summary>
public static class Reentrancy
{
    public static int Run()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(60_000);
            Console.WriteLine("REENTRANCY FAIL: watchdog");
            Environment.Exit(2);
        });
        return SmokeUi.RunSta(Drive,
            "REENTRANCY PASS — double-fire filed one doc, no mislabel",
            "REENTRANCY FAIL:");
    }

    private static List<string> Drive()
    {
        var failures = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "fr_reentry_" + Guid.NewGuid().ToString("N"));
        var inbox = Path.Combine(root, "inbox");
        var dest = Path.Combine(root, "dest");
        foreach (var d in new[] { inbox, dest, Path.Combine(root, "deferred") })
            Directory.CreateDirectory(d);
        MinimalPdf.Write(Path.Combine(inbox, "20240101--1111111111.pdf"), "FIRST DOC");
        MinimalPdf.Write(Path.Combine(inbox, "20240102--2222222222.pdf"), "SECOND DOC");

        var cfgPath = Path.Combine(root, "config.json");
        File.WriteAllText(cfgPath, JsonSerializer.Serialize(new
        {
            inbox = inbox.Replace('\\', '/'),
            deferred = Path.Combine(root, "deferred").Replace('\\', '/'),
            history_db = "history.sqlite",
            naming_mode = "replace",     // so the name IS the whole filename
            sort = "filename_asc",
            uppercase_names = true,
            routes = new[] { new { label = "Dest", path = dest.Replace('\\', '/'), hotkey = "Ctrl+1" } },
        }));

        SmokeUi.Boot();
        var window = new MainWindow(Config.Load(cfgPath), cfgPath);
        var dialogs = new RecordingDialogs();
        window.Dialogs = dialogs;
        var shell = window.Shell;

        window.Loaded += async (_, _) =>
        {
            try
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!window.Pdf.Ready && Environment.TickCount64 < deadline) await Task.Delay(100);
                // wait out the window's own Initialize() so its Ready refresh
                // can't land after our StartProcessing and cancel the session
                while (shell.CountLine.Length == 0 && Environment.TickCount64 < deadline) await Task.Delay(100);
                shell.StartProcessing();
                while (!window.Pdf.CurrentUrl.Contains("1111111111")
                       && Environment.TickCount64 < deadline) await Task.Delay(100);
                await Task.Delay(1000);

                // fire the commit TWICE without awaiting the first
                shell.TypedName = "ALICE";
                var t1 = shell.OnRouteAsync(0);
                var t2 = shell.OnRouteAsync(0);   // must be dropped by the guard
                await Task.WhenAll(t1, t2);
                await Task.Delay(500);

                // doc #1 filed as ALICE
                if (!File.Exists(Path.Combine(dest, "ALICE.pdf")))
                    failures.Add("first doc not filed as ALICE.pdf");
                // doc #2 must NOT have been filed (esp. not as ALICE (2).pdf)
                if (File.Exists(Path.Combine(dest, "ALICE (2).pdf")))
                    failures.Add("MISLABEL: second doc was filed under the first's name");
                if (!File.Exists(Path.Combine(inbox, "20240102--2222222222.pdf")))
                    failures.Add("second doc left the inbox — the guard failed");
                var filed = Directory.GetFiles(dest).Length;
                if (filed != 1) failures.Add($"expected exactly 1 filed, got {filed}");
                if (failures.Count > 0)
                    failures.Add($"diag: screen={shell.Screen} current={shell.Session.Current} "
                        + $"routes={shell.Routes.Count} enabled={shell.Routes.FirstOrDefault()?.Enabled} "
                        + $"warns=[{string.Join(" | ", dialogs.Warnings)}] status='{shell.StatusLine}'");
            }
            catch (Exception ex) { failures.Add("exception: " + ex.Message); }
            finally
            {
                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        };

        window.Show();
        Dispatcher.Run();
        return failures;
    }
}
