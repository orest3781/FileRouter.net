using System.Text;
using System.Text.Json;
using FileRouter.App;
using FileRouter.Core;

/// <summary>Regression harness for the commit reentrancy bug: firing the route
/// commit twice in quick succession must file ONE document, not mislabel the
/// next one with the first's typed name. Runs on an STA thread (WebView2).</summary>
public static class Reentrancy
{
    public static int Run()
    {
        var failures = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "fr_reentry_" + Guid.NewGuid().ToString("N"));
        var ui = new Thread(() => Drive(root, failures));
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        if (failures.Count == 0) { Console.WriteLine("REENTRANCY PASS — double-fire filed one doc, no mislabel"); return 0; }
        Console.WriteLine("REENTRANCY FAIL:");
        foreach (var f in failures) Console.WriteLine("  * " + f);
        return 1;
    }

    private static void Drive(string root, List<string> failures)
    {
        var inbox = Path.Combine(root, "inbox");
        var dest = Path.Combine(root, "dest");
        foreach (var d in new[] { inbox, dest, Path.Combine(root, "deferred") }) Directory.CreateDirectory(d);
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

        ApplicationConfiguration.Initialize();
        var form = new MainForm(Config.Load(cfgPath), cfgPath) { SuppressDialogs = true };
        _ = Task.Run(async () => { await Task.Delay(60_000); Console.WriteLine("REENTRANCY FAIL: watchdog"); Environment.Exit(2); });

        form.Shown += async (_, _) =>
        {
            try
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!form.ViewerReady && Environment.TickCount64 < deadline) await Task.Delay(100);
                form.StartProcessing();
                while (!form.CurrentPdfUrl.Contains("1111111111") && Environment.TickCount64 < deadline) await Task.Delay(100);
                await Task.Delay(1000);

                // fire the commit TWICE without awaiting the first
                form.SetTypedName("ALICE");
                var t1 = form.OnRouteAsync(0);
                var t2 = form.OnRouteAsync(0);   // must be dropped by the guard
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
            }
            catch (Exception ex) { failures.Add("exception: " + ex.Message); }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
}

internal static class MinimalPdf
{
    public static void Write(string path, string text)
    {
        var stream = $"BT /F1 24 Tf 72 700 Td ({text}) Tj ET";
        var sb = new StringBuilder();
        var offsets = new List<int>();
        void Obj(string body) { offsets.Add(sb.Length); sb.Append(body); }
        sb.Append("%PDF-1.4\n");
        Obj("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n");
        Obj("2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");
        Obj("3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n");
        Obj($"4 0 obj<</Length {stream.Length}>>stream\n{stream}\nendstream endobj\n");
        Obj("5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n");
        var xref = sb.Length;
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:0000000000} 00000 n \n");
        sb.Append($"trailer<</Size 6/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");
        File.WriteAllText(path, sb.ToString());
    }
}
