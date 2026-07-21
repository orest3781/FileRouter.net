using FileRouter.Wpf.Mvvm;

namespace FileRouter.Wpf.Tests;

public class MvvmTests
{
    private sealed class Vm : ObservableObject
    {
        private string _name = "";
        public string Name { get => _name; set => Set(ref _name, value); }
    }

    [Fact]
    public void SetRaisesOnlyOnChange()
    {
        var vm = new Vm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Name = "A";
        vm.Name = "A";   // no-op
        vm.Name = "B";
        Assert.Equal(new[] { "Name", "Name" }, raised);
    }

    [Fact]
    public void RelayCommandHonorsCanExecute()
    {
        var allowed = false;
        var runs = 0;
        var cmd = new RelayCommand(() => runs++, () => allowed);
        Assert.False(cmd.CanExecute(null));
        allowed = true;
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task AsyncCommandBlocksReentry()
    {
        var running = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var runs = 0;
        var cmd = new AsyncRelayCommand(async () =>
        {
            runs++;
            running.TrySetResult();
            await release.Task;
        });

        cmd.Execute(null);
        await running.Task;
        Assert.False(cmd.CanExecute(null));

        cmd.Execute(null);               // second press during the await
        release.SetResult();
        await cmd.Completion;

        Assert.Equal(1, runs);
        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public async Task AsyncCommandReportsErrorsInsteadOfThrowing()
    {
        Exception? seen = null;
        var cmd = new AsyncRelayCommand(() => throw new InvalidOperationException("boom"));
        cmd.OnError += ex => seen = ex;
        cmd.Execute(null);
        await cmd.Completion;
        Assert.IsType<InvalidOperationException>(seen);
    }

    [Fact]
    public async Task TypedAsyncCommandPassesParameterAndBlocksReentry()
    {
        var release = new TaskCompletionSource();
        var seen = new List<int>();
        var cmd = new AsyncRelayCommand<int>(async i => { seen.Add(i); await release.Task; });
        cmd.Execute(1);
        cmd.Execute(2);                  // blocked — first still running
        release.SetResult();
        await cmd.Completion;
        Assert.Equal(new[] { 1 }, seen);
    }
}
