using System.Windows;
using FileRouter.Core;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf;

public partial class MainWindow : Window
{
    private readonly WebViewPdfViewer _pdf;
    private readonly FolderWatchService _watch;
    internal ShellViewModel Shell { get; }
    internal IDialogService Dialogs { get; set; }

    public MainWindow(Config cfg, string cfgPath)
    {
        InitializeComponent();
        _pdf = new WebViewPdfViewer(Viewer);
        Dialogs = new DialogService(this);
        _watch = new FolderWatchService(context: SynchronizationContext.Current);
        Shell = new ShellViewModel(cfg, cfgPath, _pdf,
            new DialogRelay(() => Dialogs), _watch, SynchronizationContext.Current);
        DataContext = Shell;

        Shell.RoutesRebuilt += RebindRouteHotkeys;

        Loaded += async (_, _) =>
        {
            if (!await _pdf.InitAsync())
                Dialogs.Warn(
                    "The PDF viewer (WebView2) failed to start:\n\n" + _pdf.InitError,
                    "FileRouter");
            Shell.Initialize();
        };
        Closed += (_, _) => { _watch.Dispose(); Shell.Dispose(); };
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private readonly List<System.Windows.Input.KeyBinding> _routeBindings = new();

    /// <summary>Config-driven route hotkeys: rebuilt with the route buttons at
    /// every session start (and after Settings changes).</summary>
    private void RebindRouteHotkeys()
    {
        foreach (var b in _routeBindings) InputBindings.Remove(b);
        _routeBindings.Clear();
        foreach (var route in Shell.Routes)
        {
            if (route.Gesture is null || !route.Enabled) continue;
            var binding = new System.Windows.Input.KeyBinding(Shell.RouteCommand, route.Gesture)
            {
                CommandParameter = route.Index,
            };
            _routeBindings.Add(binding);
            InputBindings.Add(binding);
        }
    }

    /// <summary>Lets the smoke harness swap in a recording dialog service
    /// after construction without the view model holding a stale reference.</summary>
    private sealed class DialogRelay : IDialogService
    {
        private readonly Func<IDialogService> _get;
        public DialogRelay(Func<IDialogService> get) => _get = get;
        public void Warn(string m, string t) => _get().Warn(m, t);
        public void Info(string m, string t) => _get().Info(m, t);
        public bool Confirm(string m, string t) => _get().Confirm(m, t);
        public string? AskSaveFile(string f, string s) => _get().AskSaveFile(f, s);
        public string? AskOpenFile(string f) => _get().AskOpenFile(f);
        public string? BrowseFolder(string? s) => _get().BrowseFolder(s);
    }
}
