using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Theme;

namespace FileRouter.Wpf.ViewModels;

/// <summary>One monitored-folder tile on the Ready dashboard. Rendered as a
/// real focusable button in the view (the WinForms tiles were mouse-only
/// panels). Back/Fore are recomputed by the flash tick.</summary>
public sealed class TileViewModel : ObservableObject
{
    public string Label { get; }
    public string Path { get; }
    public string Display { get; }
    public string Tooltip { get; }
    public bool Alerting { get; }
    public RelayCommand OpenCommand { get; }

    private readonly Rgb _baseBack;

    private Rgb _back;
    public Rgb Back { get => _back; private set => Set(ref _back, value); }

    private Rgb _fore;
    public Rgb Fore { get => _fore; private set => Set(ref _fore, value); }

    public TileViewModel(FolderMonitor.FolderStatus s, ThemePalette palette)
    {
        Label = s.Label;
        Path = s.Path;
        Alerting = s.Alerting;
        Display = s.Error.Length > 0
            ? $"{s.Label}: {s.Error}"
            : $"{s.Label}: {s.Count}" + (s.Alerting ? "   ⚠" : "");
        Tooltip = BuildTip(s);
        _baseBack = ThemePalette.ParseColor(s.Color) ?? palette.TileDefaultBg;
        OpenCommand = new RelayCommand(() => ShellViewModel.OpenFolder(s.Path));
        ApplyFlash(flashOn: false, flashAlerts: true, palette);
    }

    /// <summary>Alerting tiles turn alert-red while flashing (or steadily when
    /// flashing is disabled); otherwise the configured color with a
    /// WCAG-contrast-picked foreground.</summary>
    public void ApplyFlash(bool flashOn, bool flashAlerts, ThemePalette palette)
    {
        if (Alerting && (flashOn || !flashAlerts))
        {
            Back = palette.Danger;
            Fore = palette.DangerText;
        }
        else
        {
            Back = _baseBack;
            Fore = ThemePalette.IdealForeground(_baseBack);
        }
    }

    private static string BuildTip(FolderMonitor.FolderStatus s)
    {
        var lines = new List<string> { s.Path.Length > 0 ? s.Path : "(not set)" };
        if (s.Error.Length > 0) lines.Add(s.Error);
        if (s.Matches.Count > 0)
        {
            lines.Add("Alerts:");
            lines.AddRange(s.Matches.Take(8).Select(m => "  " + m));
            if (s.Matches.Count > 8) lines.Add($"  … +{s.Matches.Count - 8} more");
        }
        lines.Add("Click to open the folder");
        return string.Join("\n", lines);
    }
}
