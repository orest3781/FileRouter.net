using System.Media;
using System.Windows;

namespace FileRouter.Wpf.Services;

/// <summary>The moments the app can chime for.</summary>
public enum SoundEvent { NewAlert, Filed, SetAside, Error }

/// <summary>Plays a short sound for an app moment. The shell decides WHETHER
/// to play (config gate) and passes the per-event spec; the service decides
/// HOW. Headless tests inject a null (or recording) implementation.</summary>
public interface ISoundService
{
    /// <param name="spec">"" = the built-in Sendu sound, "none" = silent,
    /// "windows" = a fitting system chime, else a path to a .wav.</param>
    void Play(SoundEvent evt, string spec);
}

/// <summary>Silent — the default in tests and headless hosts.</summary>
public sealed class NullSoundService : ISoundService
{
    public void Play(SoundEvent evt, string spec) { }
}

/// <summary>Real playback. Sounds always play on a background thread (Sound
/// PlaySync there never blocks the UI) and every failure is swallowed — a
/// missing custom .wav or a dead audio device must never break filing.</summary>
public sealed class SoundService : ISoundService
{
    // Concurrent because every Play runs on a thread-pool thread and two can
    // overlap — setting aside the LAST document fires SetAside and the
    // session-done sound back to back, and a plain Dictionary written from both
    // at once can tear or spin.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        SoundEvent, byte[]?> _builtin = new();

    public void Play(SoundEvent evt, string spec)
    {
        spec = (spec ?? "").Trim();
        if (spec.Equals("none", StringComparison.OrdinalIgnoreCase)) return;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (spec.Length == 0 || spec.Equals("default", StringComparison.OrdinalIgnoreCase))
                    PlayBuiltin(evt);
                else if (spec.Equals("windows", StringComparison.OrdinalIgnoreCase))
                    WindowsSoundFor(evt).Play();
                else if (File.Exists(spec))
                    new SoundPlayer(spec).PlaySync();
                else
                    PlayBuiltin(evt);   // stale custom path — fall back, never silent-fail loudly
            }
            catch { /* audio is a nicety; never let it surface */ }
        });
    }

    private void PlayBuiltin(SoundEvent evt)
    {
        // Error has no bespoke sound — the system's critical chime says it best
        if (evt == SoundEvent.Error) { SystemSounds.Hand.Play(); return; }
        var bytes = BuiltinBytes(evt);
        if (bytes is null) { WindowsSoundFor(evt).Play(); return; }
        using var ms = new MemoryStream(bytes);
        using var player = new SoundPlayer(ms);
        player.PlaySync();
    }

    private byte[]? BuiltinBytes(SoundEvent evt) => _builtin.GetOrAdd(evt, LoadBuiltin);

    private static byte[]? LoadBuiltin(SoundEvent evt)
    {
        var name = evt switch
        {
            SoundEvent.NewAlert => "sendu-alert",
            SoundEvent.Filed => "sendu-send",
            SoundEvent.SetAside => "sendu-aside",
            _ => null,
        };
        byte[]? bytes = null;
        if (name is not null)
        {
            try
            {
                var uri = new Uri(
                    $"pack://application:,,,/FileRouter.Wpf;component/Assets/sounds/{name}.wav");
                using var stream = Application.GetResourceStream(uri)!.Stream;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                bytes = ms.ToArray();
            }
            catch { bytes = null; }
        }
        return bytes;
    }

    private static SystemSound WindowsSoundFor(SoundEvent evt) => evt switch
    {
        SoundEvent.NewAlert => SystemSounds.Exclamation,
        SoundEvent.Filed => SystemSounds.Asterisk,
        SoundEvent.SetAside => SystemSounds.Beep,
        _ => SystemSounds.Hand,
    };
}
