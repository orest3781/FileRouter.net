using FileRouter.Core;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.ViewModels;
using PdfSharp.Pdf;

namespace FileRouter.Wpf.Tests;

public class UnlockViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "frunlock_" + Guid.NewGuid());
    private readonly Config _cfg = new();
    private int _saves;

    public UnlockViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private UnlockViewModel Vm() => new(_cfg, () => _saves++);

    private string MakeEncrypted(string name, string userPw = "secret")
    {
        var path = Path.Combine(_dir, name);
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.SecuritySettings.UserPassword = userPw;
        doc.SecuritySettings.OwnerPassword = "owner-" + userPw;
        doc.Save(path);
        return path;
    }

    [Fact]
    public async Task UnlocksInPlaceWithTheRightPassword()
    {
        var vm = Vm();
        var path = MakeEncrypted("locked.pdf");
        vm.AddFiles(new[] { path });
        vm.Password = "secret";
        await vm.UnlockAsync();
        var line = Assert.Single(vm.ResultLines);
        Assert.Equal(UnlockResultKind.Ok, line.Kind);
        Assert.Contains("locked.pdf — unlocked", line.Text);
        Assert.Equal("1 unlocked", vm.Summary);
    }

    [Fact]
    public async Task WrongPasswordReportsPerFileWithoutTouchingIt()
    {
        var vm = Vm();
        var path = MakeEncrypted("locked.pdf");
        var before = File.ReadAllBytes(path);
        vm.AddFiles(new[] { path });
        vm.Password = "wrong";
        await vm.UnlockAsync();
        var line = Assert.Single(vm.ResultLines);
        Assert.Equal(UnlockResultKind.Fail, line.Kind);
        Assert.StartsWith("✗", line.Text);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Contains("1 failed", vm.Summary);
    }

    [Fact]
    public async Task AWrongPasswordIsNeverRemembered()
    {
        // the old behavior saved the label unconditionally after the attempt —
        // a stored wrong password would just fail again silently next session
        var vm = Vm();
        vm.AddFiles(new[] { MakeEncrypted("locked.pdf") });
        vm.Password = "wrong";
        vm.RememberLabel = "Payer A";
        await vm.UnlockAsync();
        Assert.Empty(_cfg.SavedPasswords);
        Assert.Equal(0, _saves);
        Assert.Equal("Payer A", vm.RememberLabel);   // kept for the retry
    }

    [Fact]
    public async Task KeepCopyWritesASuffixedCopy()
    {
        var vm = Vm();
        var path = MakeEncrypted("locked.pdf");
        vm.AddFiles(new[] { path });
        vm.Password = "secret";
        vm.KeepCopy = true;
        await vm.UnlockAsync();
        Assert.True(File.Exists(Path.Combine(_dir, "locked_unlocked.pdf")));
        Assert.True(File.Exists(path));   // original untouched
    }

    [Fact]
    public async Task RememberPersistsADpapiEntry()
    {
        var vm = Vm();
        vm.AddFiles(new[] { MakeEncrypted("locked.pdf") });
        vm.Password = "secret";
        vm.RememberLabel = "Payer A";
        await vm.UnlockAsync();

        var saved = Assert.Single(_cfg.SavedPasswords);
        Assert.Equal("Payer A", saved.Label);
        Assert.True(PasswordVault.IsProtected(saved.Password));
        Assert.Equal("secret", PasswordVault.Reveal(saved.Password));
        Assert.Equal(1, _saves);
        Assert.Equal("", vm.RememberLabel);
    }

    [Fact]
    public void SelectingASavedPasswordFillsTheBox()
    {
        _cfg.SavedPasswords.Add(new SavedPassword
        { Label = "X", Password = PasswordVault.Protect("pw123") });
        var vm = Vm();
        vm.SelectedSaved = vm.Saved[0];
        Assert.Equal("pw123", vm.Password);
    }

    [Fact]
    public async Task EmptyListDisablesUnlockAndHints()
    {
        var vm = Vm();
        Assert.False(vm.UnlockCommand.CanExecute(null));
        await vm.UnlockAsync();
        Assert.Equal("Add at least one PDF first.", vm.Summary);
    }

    [Fact]
    public void OnlyExistingPdfsAreAcceptedAndTheDropExplainsItself()
    {
        var vm = Vm();
        var txt = Path.Combine(_dir, "not.txt");
        File.WriteAllText(txt, "x");
        vm.AddFiles(new[] { txt, Path.Combine(_dir, "ghost.pdf") });
        Assert.Empty(vm.Files);
        Assert.Contains("nothing added", vm.AddNote);

        var pdf = MakeEncrypted("real.pdf");
        vm.AddFiles(new[] { pdf, txt });
        Assert.Single(vm.Files);
        Assert.Contains("1 added", vm.AddNote);
        Assert.Contains("1 ignored", vm.AddNote);
        Assert.True(vm.UnlockCommand.CanExecute(null));
    }
}

public class BulkRenameViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "frbulk_" + Guid.NewGuid());

    public BulkRenameViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void FindReplacePreviewMatchesThePlan()
    {
        var vm = new BulkRenameViewModel();
        vm.AddFiles(new[] { Touch("scan_001.pdf"), Touch("keep.pdf") });
        vm.Find = "scan";
        vm.Replace = "fax";

        Assert.Equal("fax_001.pdf", vm.Preview[0].NewName);
        Assert.True(vm.Preview[0].Changed);
        Assert.False(vm.Preview[1].Changed);
        Assert.Equal("Rename 1 file", vm.RenameButtonText);
    }

    [Fact]
    public void ReviewTransformParsesTheMedicalFaxNames()
    {
        var vm = new BulkRenameViewModel();
        vm.AddFiles(new[] { Touch("BROWN_ADAM_4_25_1966_NYCHSRO_MEDREVIEW_566379260-1_X.pdf") });
        vm.ReceivedDate = new DateTime(2024, 1, 26);
        vm.ReviewMode = true;
        Assert.Equal("20240126-BROWN-ADAM.pdf", vm.Preview[0].NewName);
    }

    [Fact]
    public void HandEditSurvivesAnOpChange()
    {
        var vm = new BulkRenameViewModel();
        var src = Touch("scan_001.pdf");
        vm.AddFiles(new[] { src });
        vm.SetOverride(src, "CUSTOM NAME.pdf");   // typed extension is stripped
        vm.Find = "scan";
        vm.Replace = "fax";

        Assert.Equal("CUSTOM NAME.pdf", vm.Preview[0].NewName);
        Assert.True(vm.Preview[0].Manual);

        vm.SetOverride(src, "");   // clearing goes back to the op result
        Assert.Equal("fax_001.pdf", vm.Preview[0].NewName);
    }

    [Fact]
    public void ApplyRenamesOnDiskAndUndoRestores()
    {
        var vm = new BulkRenameViewModel();
        var src = Touch("scan_001.pdf");
        vm.AddFiles(new[] { src });
        vm.Find = "scan";
        vm.Replace = "fax";
        vm.Apply();

        Assert.True(File.Exists(Path.Combine(_dir, "fax_001.pdf")));
        Assert.False(File.Exists(src));
        Assert.Contains("Renamed 1 file", vm.Status);
        Assert.Equal("", vm.Find);   // ops reset after a batch

        vm.UndoBatch();
        Assert.True(File.Exists(src));
        Assert.Equal("Original names restored.", vm.Status);
    }
}

public class MatchMergeViewModelTests : IDisposable
{
    private const string Csv =
        "First Name,Last Name,Control ID,DOB\n" +
        "Adam,Brown,566379260,4/25/1966\n" +
        "Adam,Brown,696009058,11/10/1955\n" +
        "Frank,Evans,176797656,8/9/1997\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "frmm_" + Guid.NewGuid());
    private readonly Config _cfg = new();
    private readonly FakeDialogs _dialogs = new();
    private Dictionary<string, string>? _savedHeaders;

    public MatchMergeViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private MatchMergeViewModel Vm() => new(_cfg, h => _savedHeaders = h, _dialogs);

    private string WriteRoster()
    {
        var path = Path.Combine(_dir, "roster.csv");
        File.WriteAllText(path, Csv, new System.Text.UTF8Encoding(true));
        return path;
    }

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void RosterLoadGuessesHeadersAndRemembersThem()
    {
        var vm = Vm();
        vm.LoadRosterFrom(WriteRoster());
        Assert.Equal("First Name", vm.FirstHeader);
        Assert.Equal("Last Name", vm.LastHeader);
        Assert.Equal("Control ID", vm.ControlHeader);
        Assert.Contains("2 people", vm.Status);   // the two Adam Browns are one person
        Assert.Equal("First Name", _savedHeaders!["first"]);
    }

    [Fact]
    public void SavedHeaderMappingWinsOverTheGuess()
    {
        _cfg.MergeHeaders["control"] = "DOB";   // deliberately odd saved choice
        var vm = Vm();
        vm.LoadRosterFrom(WriteRoster());
        Assert.Equal("DOB", vm.ControlHeader);
    }

    [Fact]
    public void FilesClassifyIntoTheFourBuckets()
    {
        var vm = Vm();
        vm.LoadRosterFrom(WriteRoster());
        vm.AddFiles(new[]
        {
            Touch("20240126-EVANS-FRANK.pdf"),               // unambiguous
            Touch("20240126-BROWN-ADAM.pdf"),                // two Adams -> triage
            Touch("20240126-EVANS-FRANK-176797656.pdf"),     // already merged
            Touch("scan_001.pdf"),                           // no name
        });

        Assert.Equal(1, vm.MergeCount);
        Assert.Equal(1, vm.AmbiguousCount);
        Assert.Equal("Merge 1 matched", vm.MergeButtonText);
        Assert.True(vm.CanTriage);
        Assert.Single(vm.AmbiguousItems);
        Assert.Contains(vm.Rows, r => r.Note == "already has the id");
    }

    [Fact]
    public void MergeRenamesAndUndoRestores()
    {
        var vm = Vm();
        vm.LoadRosterFrom(WriteRoster());
        var f = Touch("20240126-EVANS-FRANK.pdf");
        vm.AddFiles(new[] { f });

        vm.MergeCommand.Execute(null);
        Assert.True(File.Exists(Path.Combine(_dir, "20240126-EVANS-FRANK-176797656.pdf")));
        Assert.Contains("Merged 1 file", vm.Status);

        vm.UndoCommand.Execute(null);
        Assert.True(File.Exists(f));
    }

    [Fact]
    public void NoRosterShowsAHintPerFile()
    {
        var vm = Vm();
        vm.AddFiles(new[] { Touch("20240126-EVANS-FRANK.pdf") });
        Assert.Equal("load a roster first", Assert.Single(vm.Rows).Note);
        Assert.False(vm.MergeCommand.CanExecute(null));
    }
}
