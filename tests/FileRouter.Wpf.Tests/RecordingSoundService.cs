using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Tests;

/// <summary>Records what would have played — no audio in headless tests.</summary>
public sealed class RecordingSoundService : ISoundService
{
    public List<(SoundEvent Evt, string Spec)> Played { get; } = new();

    public void Play(SoundEvent evt, string spec) => Played.Add((evt, spec));
}
