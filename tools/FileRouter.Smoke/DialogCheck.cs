using System;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using FileRouter.App;
using FileRouter.Core;

public static class DialogCheck
{
    public static int Run()
    {
        var errors = new List<string>();
        var t = new Thread(() =>
        {
            ApplicationConfiguration.Initialize();
            try { using var d = new UnlockDialog(); _ = d.Handle; } catch (Exception ex) { errors.Add("Unlock: " + ex.Message); }
            try { using var d = new BulkRenameDialog(); _ = d.Handle; } catch (Exception ex) { errors.Add("BulkRename: " + ex.Message); }
            try { using var d = new MatchMergeDialog(new Config(), _ => { }); _ = d.Handle; } catch (Exception ex) { errors.Add("MatchMerge: " + ex.Message); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (errors.Count == 0) { Console.WriteLine("DIALOGS OK — all three construct cleanly"); return 0; }
        foreach (var e in errors) Console.WriteLine("DIALOG FAIL: " + e);
        return 1;
    }
}
