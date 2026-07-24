using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Tests;

/// <summary>Runs scheduled work inline on the calling thread, so every shell
/// flow completes synchronously by the time the call returns — tests assert
/// immediately after driving the view model, no pumping or sleeping.</summary>
public sealed class InlineWorkScheduler : IWorkScheduler
{
    public Task<T> Run<T>(Func<T> work) => Task.FromResult(work());

    public Task Run(Action work)
    {
        work();
        return Task.CompletedTask;
    }
}
