using System.Windows.Input;

namespace FileRouter.Wpf.Mvvm;

/// <summary>Plain synchronous command.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}

/// <summary>Async command that blocks reentry: while a run is in flight,
/// CanExecute is false and a second Execute is a no-op. This is the app-wide
/// reentrancy guard — a fast double Enter/Ctrl+1 during the viewer-release
/// await must never start a second commit (it would capture the same textbox
/// text and mislabel the next document).</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private Task? _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>Raised when the task faults; default behavior swallows after
    /// reporting here so an exception never kills the message loop.</summary>
    public event Action<Exception>? OnError;

    /// <summary>The in-flight (or last) run — awaited by tests and the smoke.</summary>
    internal Task Completion => _running ?? Task.CompletedTask;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) =>
        _running is null && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (_running is not null) return;   // reentrancy: second press is a no-op
        var task = Run();
        _running = task;
        RaiseCanExecuteChanged();
        try { await task; }
        finally { _running = null; RaiseCanExecuteChanged(); }
    }

    private async Task Run()
    {
        try { await _execute(); }
        catch (Exception ex) { OnError?.Invoke(ex); }
    }
}

/// <summary>Typed variant of <see cref="AsyncRelayCommand"/> with the same
/// reentrancy contract.</summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T, Task> _execute;
    private readonly Func<T, bool>? _canExecute;
    private Task? _running;

    public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public event Action<Exception>? OnError;

    internal Task Completion => _running ?? Task.CompletedTask;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) =>
        _running is null && (parameter is not T t || (_canExecute?.Invoke(t) ?? true));

    public async void Execute(object? parameter)
    {
        if (_running is not null || parameter is not T t) return;
        var task = Run(t);
        _running = task;
        RaiseCanExecuteChanged();
        try { await task; }
        finally { _running = null; RaiseCanExecuteChanged(); }
    }

    private async Task Run(T arg)
    {
        try { await _execute(arg); }
        catch (Exception ex) { OnError?.Invoke(ex); }
    }
}
