using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FileRouter.Wpf.Services;

/// <summary>WebView2-backed viewer. Edge's built-in PDF renderer means no
/// bundled PDF library — the same deliberate trade the WinForms app made.</summary>
public sealed class WebViewPdfViewer : IPdfViewer
{
    private readonly WebView2 _view;
    private bool _ready;

    public WebViewPdfViewer(WebView2 view) => _view = view;

    public string? InitError { get; private set; }
    public bool Ready => _ready;

    public async Task<bool> InitAsync()
    {
        try
        {
            await _view.EnsureCoreWebView2Async();
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            InitError = ex.ToString();
            return false;
        }
    }

    public Task ShowAsync(string path)
    {
        if (_ready)
            _view.CoreWebView2.Navigate(new Uri(Path.GetFullPath(path)).AbsoluteUri);
        return Task.CompletedTask;
    }

    public void Blank()
    {
        if (_ready) _view.CoreWebView2.Navigate("about:blank");
    }

    /// <summary>Navigate to a blank page and wait for completion so Edge
    /// releases the PDF file handle before the move — verbatim contract from
    /// MainForm.ReleaseViewerAsync, proven by the smoke test.</summary>
    public async Task ReleaseAsync()
    {
        if (!_ready) return;
        var tcs = new TaskCompletionSource();
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            _view.CoreWebView2.NavigationCompleted -= Handler;
            tcs.TrySetResult();
        }
        _view.CoreWebView2.NavigationCompleted += Handler;
        _view.CoreWebView2.Navigate("about:blank");
        await Task.WhenAny(tcs.Task, Task.Delay(2000));
    }

    /// <summary>Current document URL — used by the smoke test to prove the
    /// real viewer rendered the real file.</summary>
    internal string CurrentUrl => _ready ? _view.CoreWebView2.Source ?? "" : "";
}
