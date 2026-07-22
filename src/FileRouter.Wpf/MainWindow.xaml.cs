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
    internal WebViewPdfViewer Pdf => _pdf;

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
        Shell.SettingsApplied += () => App.ApplyFont(Application.Current, Shell.Cfg);

        // Python-parity window lifecycle: the Ready dashboard is a compact
        // window parked in the top-right corner; the window only grows to the
        // full viewer layout while a session runs. Both geometries remembered.
        Shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.Screen)) ApplyWindowMode();
        };
        WindowStartupLocation = WindowStartupLocation.Manual;
        EnterCompact(initial: true);

        // a manual user resize flips SizeToContent to Manual (WPF behavior);
        // re-assert auto-fit when the tile set changes so the dashboard keeps
        // tracking its content
        Shell.Tiles.CollectionChanged += (_, _) =>
        {
            if (_compact && SizeToContent != SizeToContent.Height)
                SizeToContent = SizeToContent.Height;
        };
        InputBindings.Add(new System.Windows.Input.KeyBinding(
            new Mvvm.RelayCommand(() => OnSettings(this, new RoutedEventArgs())),
            System.Windows.Input.Key.OemComma, System.Windows.Input.ModifierKeys.Control));

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

    // ------------------------------------------------ compact/normal modes
    private Rect? _normalBounds;
    private Rect? _compactBounds;
    private bool _compact;

    private void ApplyWindowMode()
    {
        if (Shell.IsReady) EnterCompact(initial: false);
        else EnterNormal();
    }

    private void EnterCompact(bool initial)
    {
        if (!initial)
        {
            if (_compact) return;
            _normalBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        }
        _compact = true;

        Viewer.Visibility = Visibility.Collapsed;
        Split.Visibility = Visibility.Collapsed;
        ViewerCol.MinWidth = 0;
        ViewerCol.Width = new GridLength(0);
        SplitterCol.Width = new GridLength(0);
        PanelCol.MinWidth = 0;
        PanelCol.Width = new GridLength(1, GridUnitType.Star);
        MinWidth = 400;
        MinHeight = 0;

        // auto-fit: the dashboard sizes itself to its content and grows
        // downward as monitored folders appear — no scrollbars. Capped at the
        // work area so a huge folder list can't push it off-screen (the
        // ScrollViewer only kicks in past that cap).
        var wa = SystemParameters.WorkArea;   // DIPs, primary monitor
        MaxHeight = wa.Height - 24;
        SizeToContent = SizeToContent.Height;

        if (_compactBounds is { } b)
        {
            Left = b.Left; Top = b.Top; Width = b.Width;
        }
        else
        {
            Width = 470;
            Left = wa.Right - Width - 12;
            Top = wa.Top + 12;
        }
    }

    private void EnterNormal()
    {
        if (!_compact) return;
        // capture BEFORE touching MinWidth — raising it resizes the window
        // immediately and would corrupt the geometry math below
        var compact = new Rect(Left, Top, ActualWidth, ActualHeight);
        _compactBounds = compact;
        _compact = false;

        // back to explicit sizing BEFORE the bounds are set, or the Height
        // assignments below would be ignored
        SizeToContent = SizeToContent.Manual;
        MaxHeight = double.PositiveInfinity;

        Viewer.Visibility = Visibility.Visible;
        Split.Visibility = Visibility.Visible;
        ViewerCol.MinWidth = 320;
        ViewerCol.Width = new GridLength(1, GridUnitType.Star);
        SplitterCol.Width = new GridLength(5);
        PanelCol.MinWidth = 370;
        PanelCol.Width = new GridLength(430);
        MinWidth = 900;
        MinHeight = 600;

        if (_normalBounds is { } b)
        {
            Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
        }
        else
        {
            // grow leftward from the parked corner so the big window opens on
            // the same monitor the user parked the dashboard on
            Width = 1280;
            Height = 860;
            Left = Math.Max(SystemParameters.VirtualScreenLeft, compact.Right - Width);
            Top = compact.Top;
        }
    }

    private void OnViewHistory(object sender, RoutedEventArgs e) =>
        new Windows.HistoryWindow(new ViewModels.HistoryViewModel(Shell.History, Dialogs))
        { Owner = this }.ShowDialog();

    private void OnUnlock(object sender, RoutedEventArgs e) =>
        new Windows.UnlockWindow(new UnlockViewModel(Shell.Cfg, Shell.SaveConfigNow))
        { Owner = this }.ShowDialog();

    private void OnBulkRename(object sender, RoutedEventArgs e) =>
        new Windows.BulkRenameWindow(new BulkRenameViewModel()) { Owner = this }.ShowDialog();

    private void OnMatchMerge(object sender, RoutedEventArgs e) =>
        new Windows.MatchMergeWindow(new MatchMergeViewModel(
            Shell.Cfg, Shell.SaveMergeHeaders, Dialogs)) { Owner = this }.ShowDialog();

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (!Shell.IsReady)
        {
            Dialogs.Info("Finish or stop the current session first (Esc stops it — nothing is lost).",
                "FileRouter");
            return;
        }
        var vm = new SettingsViewModel(Shell.Cfg, Dialogs);
        var win = new Windows.SettingsWindow(vm) { Owner = this };
        if (win.ShowDialog() == true && vm.Result is { } cfg)
            Shell.ApplySettings(cfg);
    }

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
        public string? AskFilePath(string f, string s) => _get().AskFilePath(f, s);
        public string? BrowseFolder(string? s) => _get().BrowseFolder(s);
    }
}
