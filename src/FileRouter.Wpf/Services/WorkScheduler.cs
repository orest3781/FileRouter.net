namespace FileRouter.Wpf.Services;

/// <summary>Where the shell runs its filesystem work. The app offloads to the
/// thread pool so SMB latency can never freeze the UI (or starve the global
/// mouse hook); tests inject an inline version so every flow stays
/// deterministic and synchronous.</summary>
public interface IWorkScheduler
{
    Task<T> Run<T>(Func<T> work);
    Task Run(Action work);
}

/// <summary>Thread-pool offload — the production scheduler.</summary>
public sealed class TaskWorkScheduler : IWorkScheduler
{
    public Task<T> Run<T>(Func<T> work) => Task.Run(work);
    public Task Run(Action work) => Task.Run(work);
}