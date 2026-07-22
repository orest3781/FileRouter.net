using System.Text.Json;

/// <summary>Regenerate the demo: 5 sample faxes in demo\inbox, empty route and
/// set-aside folders, and a ready-to-use demo\config.json. Self-contained (no
/// Python) — used by reset.bat. The demo folder is resolved relative to the
/// current directory (reset.bat cd's to the project root first).</summary>
public static class DemoReset
{
    public static int Run()
    {
        var demo = Path.Combine(Directory.GetCurrentDirectory(), "demo");
        var inbox = Path.Combine(demo, "inbox");
        var invoices = Path.Combine(demo, "invoices");
        var statements = Path.Combine(demo, "statements");
        var deferred = Path.Combine(demo, "deferred");
        var failed = Path.Combine(demo, "failed");   // a monitored folder for the dashboard

        // wipe the generated folders, recreate empty
        foreach (var d in new[] { inbox, invoices, statements, deferred, failed })
        {
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
            Directory.CreateDirectory(d);
        }

        // seed the monitored folder so a dashboard tile shows (one file trips
        // the "URGENT" alert -> the tile flashes)
        MinimalPdf.Write(Path.Combine(failed, "retry_00842.pdf"), "FAILED FAX 00842");
        MinimalPdf.Write(Path.Combine(failed, "URGENT_00843.pdf"), "URGENT FAILED FAX 00843");

        var names = new[] { "SMITH JOHN", "GARCIA MARIA", "O'BRIEN PATRICK", "MULLER HANS", "TANAKA YUKI" };
        for (var i = 0; i < names.Length; i++)
            MinimalPdf.Write(
                Path.Combine(inbox, $"2024{i + 1:00}15--{1000000000 + i}.pdf"),
                $"MEDICAL REVIEW   -   {names[i]}   -   Document {i + 1} of {names.Length}");

        File.WriteAllText(Path.Combine(demo, "config.json"), JsonSerializer.Serialize(new
        {
            inbox = inbox.Replace('\\', '/'),
            deferred = deferred.Replace('\\', '/'),
            history_db = "history.sqlite",
            naming_mode = "insert",
            sort = "size_desc",
            uppercase_names = true,
            enter_commits = true,
            monitor_title = "Needs attention",
            flash_alerts = true,
            alert_texts = new[] { "URGENT" },
            watch_folders = new[]
            {
                new { label = "Failed faxes", path = failed.Replace('\\', '/'), recursive = false, filetypes = "pdf", color = "#c0392b" },
            },
            routes = new[]
            {
                new { label = "Invoices", path = invoices.Replace('\\', '/'), hotkey = "Ctrl+1", append_suffix = true, suffix = "_INVOICE", color = "#2e7d32" },
                new { label = "Statements", path = statements.Replace('\\', '/'), hotkey = "Ctrl+2", append_suffix = false, suffix = "", color = "#1565c0" },
            },
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Demo reset: {names.Length} sample faxes in {inbox}");
        Console.WriteLine("Run  run.bat  to launch against it.");
        return 0;
    }
}
