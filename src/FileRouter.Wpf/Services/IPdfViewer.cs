namespace FileRouter.Wpf.Services;

/// <summary>The PDF pane, as the view model sees it. The real implementation
/// wraps WebView2; tests substitute a recorder. The one load-bearing member is
/// <see cref="ReleaseAsync"/> — Edge must let go of the file handle BEFORE the
/// commit moves the file.</summary>
public interface IPdfViewer
{
    /// <summary>Display a PDF. No-op when the viewer never initialized.</summary>
    Task ShowAsync(string path);

    /// <summary>Navigate away and wait until the engine has actually released
    /// the current document's file handle (bounded by a 2 s fallback).</summary>
    Task ReleaseAsync();

    /// <summary>Show nothing (Ready/Done screens).</summary>
    void Blank();
}
