// UI smoke harness: runs the REAL MainForm, waits for WebView2 to render the
// first PDF, then drives commit / set-aside / undo through the same internal
// handlers the buttons call. Proves the one thing unit tests can't: that Edge's
// PDF viewer releases the file handle so the move actually succeeds.
//
// WinForms + WebView2 require an STA thread; a console app's implicit Main is
// MTA, so the whole UI runs on an explicit STA thread here.
//
// Exit 0 + "SMOKE PASS" on success; nonzero otherwise.

using System.Text;
using System.Text.Json;
using FileRouter.App;
using FileRouter.Core;

if (args.Length > 0 && args[0] == "dialogs") return DialogCheck.Run();
if (args.Length > 0 && args[0] == "reentrancy") return Reentrancy.Run();
var failures = new List<string>();
var root = Path.Combine(Path.GetTempPath(), "fr_smoke_" + Guid.NewGuid().ToString("N"));

// hard watchdog: never hang CI
_ = Task.Run(async () =>
{
    await Task.Delay(75_000);
    Console.WriteLine("SMOKE FAIL: 75s watchdog fired (UI never completed)");
    Environment.Exit(2);
});

var ui = new Thread(() => RunUi(root, failures));
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();

if (failures.Count == 0)
{
    Console.WriteLine("SMOKE PASS — commit under live Edge viewer, set-aside, undo, history all OK");
    return 0;
}
Console.WriteLine("SMOKE FAIL:");
foreach (var f in failures) Console.WriteLine("  * " + f);
return 1;

static void RunUi(string root, List<string> failures)
{
    var inbox = Path.Combine(root, "inbox");
    var dest = Path.Combine(root, "invoices");
    var deferred = Path.Combine(root, "deferred");
    foreach (var d in new[] { inbox, dest, deferred }) Directory.CreateDirectory(d);

    WritePdf(Path.Combine(inbox, "20240101--1111111111.pdf"), "ALPHA ONE");
    WritePdf(Path.Combine(inbox, "20240102--2222222222.pdf"), "BETA TWO");
    WritePdf(Path.Combine(inbox, "20240103--3333333333.pdf"), "GAMMA THREE");

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

    ApplicationConfiguration.Initialize();
    var form = new MainForm(Config.Load(cfgPath), cfgPath) { SuppressDialogs = true };

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

    form.Shown += async (_, _) =>
    {
        try
        {
            await Wait(() => form.ViewerReady || form.InitError != null, "WebView2 init");
            if (form.InitError != null) { failures.Add("WebView2 init: " + form.InitError.Split('\n')[0]); return; }

            form.StartProcessing();
            await Wait(() => form.CurrentPdfUrl.Contains("1111111111"), "first PDF in viewer");
            await Task.Delay(1500);   // worst-case: let Edge fully lock the file

            form.SetTypedName("SMITH JOHN");
            await form.OnRouteAsync(0);
            var expected = Path.Combine(dest, "20240101-SMITH JOHN-1111111111_INVOICE.pdf");
            if (!File.Exists(expected)) failures.Add("commit: filed file missing (" + (form.LastDialog ?? "no error") + ")");
            if (File.Exists(Path.Combine(inbox, "20240101--1111111111.pdf"))) failures.Add("commit: source still in inbox");

            await Wait(() => form.CurrentPdfUrl.Contains("2222222222"), "second PDF");
            await Task.Delay(500);
            Invoke(form, "OnSkip");
            await Wait(() => File.Exists(Path.Combine(deferred, "20240102--2222222222.pdf")), "file set aside");

            Invoke(form, "OnUndo");
            await Wait(() => File.Exists(Path.Combine(inbox, "20240102--2222222222.pdf")), "undo restored the file");

            if (form.SessionForSmoke.RowIds.Count < 2)
                failures.Add("history: fewer than 2 rows recorded");
        }
        catch (Exception ex) { failures.Add("exception: " + ex.Message); }
        finally { Log("closing"); form.Close(); }
    };

    Application.Run(form);
}

static void Invoke(MainForm form, string method) =>
    typeof(MainForm).GetMethod(method,
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .Invoke(form, null);

// A minimal valid one-page PDF Edge renders fine.
static void WritePdf(string path, string text)
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
