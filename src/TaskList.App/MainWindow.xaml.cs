using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppNotifications;
using TaskList.Core;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;
using Windows.UI;

namespace TaskList.App;

/// <summary>
/// The window shell: file sync, timers and window placement. Other parts live in partial files:
/// Theme (looks and picker), Footer (buttons, pin to corner), News, Research, and Render (the list).
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly Preferences _prefs;
    private readonly LocalState _state;
    private BacklogStore _store = null!;
    private readonly ProgressArc _ring;

    private readonly DispatcherQueueTimer _pollTimer;
    private readonly DispatcherQueueTimer _statusTimer;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        ChooseBackdrop();
        InitGlass();
        Windows.System.Power.PowerManager.PowerSupplyStatusChanged += (_, _) => DispatcherQueue.TryEnqueue(ChooseBackdrop);

        _prefs = Preferences.Load();
        _state = LocalState.Load();
        ApplyAiEngine();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 360;
            presenter.PreferredMinimumHeight = 480;
            presenter.IsAlwaysOnTop = _prefs.AlwaysOnTop;
        }
        RestoreWindowBounds();
        AppWindow.Changed += OnAppWindowChanged;
        Root.SizeChanged += (_, e) =>
        {
            Canvas.SetLeft(GlowA, e.NewSize.Width - 300);
            LayoutTitleBar();
        };
        NewsPill.SizeChanged += (_, _) => UpdateTitleBarPassthrough();

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(6);
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, _) => FileText.Text = Path.GetFileName(_store.FilePath);

        _ring = new ProgressArc(RingHost);
        ApplyTheme(render: false);
        BuildFooter();
        OpenStore(Environment.GetEnvironmentVariable("TASKLIST_BACKLOG") ?? _prefs.BacklogPath);

        // Pick up edits made in other editors, and roll "today" over at midnight.
        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(2);
        _pollTimer.Tick += (_, _) => Poll();
        _pollTimer.Start();

        InitNews();
        InitEdgeReveal();
        RenderBanners(animateNewest: null);

        // Ctrl+N adds a focus task from anywhere in the window.
        var addShortcut = new KeyboardAccelerator { Key = VirtualKey.N, Modifiers = VirtualKeyModifiers.Control };
        addShortcut.Invoked += (_, e) => { BeginAdd("focus"); e.Handled = true; };
        Root.KeyboardAccelerators.Add(addShortcut);

        Closed += (_, _) =>
        {
            SaveWindowBounds();
            try { AppNotificationManager.Default.Unregister(); } catch (Exception) { }
        };
    }

    // ---------- Glass ----------

    private MicaBackdrop? _mica;

    private WallpaperGlass? _glass;

    /// <summary>Starts the frosted-wallpaper glass and keeps it aligned as the window moves.</summary>
    private void InitGlass()
    {
        _glass = new WallpaperGlass(WallpaperImage, AppWindow);
        Root.Loaded += async (_, _) => await _glass.RefreshAsync(Root.XamlRoot.RasterizationScale);
        AppWindow.Changed += async (_, args) =>
        {
            if (_glass == null || Root.XamlRoot == null) return;
            if (args.DidPositionChange || args.DidSizeChange) _glass.Position(Root.XamlRoot.RasterizationScale);
            // A move can land on another monitor (different wallpaper or size): reload if so.
            if (args.DidPositionChange && !_movingWindow) await _glass.RefreshAsync(Root.XamlRoot.RasterizationScale);
        };
    }


    /// <summary>
    /// On battery Windows switches off the live blur of windows behind (acrylic renders flat gray), so
    /// the app uses Mica there, which still tints the glass with the desktop wallpaper. Plugged in, it
    /// uses the see-through acrylic glass. It switches live when the power source changes.
    /// </summary>
    private void ChooseBackdrop()
    {
        bool onBattery = Windows.System.Power.PowerManager.PowerSupplyStatus == Windows.System.Power.PowerSupplyStatus.NotPresent;
        SystemBackdrop backdrop = onBattery
            ? _mica ??= new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt }
            : _backdrop;
        if (!ReferenceEquals(SystemBackdrop, backdrop)) SystemBackdrop = backdrop;
    }

    // ---------- Title bar ----------

    /// <summary>The ticker fills the space up to the caption buttons (min/max/close).</summary>
    private void LayoutTitleBar()
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1;
        double captions = AppWindow.TitleBar.RightInset / scale;
        NewsPill.Width = Math.Clamp(Root.ActualWidth - captions - 12 - 16, 140, 600);
        UpdateTitleBarPassthrough();
    }

    /// <summary>The whole strip drags the window, except the ticker, which stays clickable.</summary>
    private void UpdateTitleBarPassthrough()
    {
        if (Root.XamlRoot == null) return;
        var source = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        if (NewsPill.Visibility != Visibility.Visible || NewsPill.ActualWidth == 0)
        {
            source.ClearRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough);
            return;
        }
        double scale = Root.XamlRoot.RasterizationScale;
        var origin = NewsPill.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        source.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough,
        [
            new RectInt32((int)(origin.X * scale), (int)(origin.Y * scale), (int)(NewsPill.ActualWidth * scale), (int)(NewsPill.ActualHeight * scale)),
        ]);
    }

    // ---------- AI engine ----------

    private void ApplyAiEngine()
    {
        AiCli.Engine = Enum.TryParse<AiEngine>(_prefs.AiEngine, out var engine) ? engine : AiEngine.Claude;
        AiCli.CustomCommand = _prefs.AiCustomCommand;
    }

    // ---------- File ----------

    private void OpenStore(string path)
    {
        if (_store != null) _store.Changed -= OnStoreChanged;
        _store = new BacklogStore(path);
        _store.Changed += OnStoreChanged;
        FileText.Text = Path.GetFileName(_store.FilePath);
        ToolTipService.SetToolTip(FileButton, $"{_store.FilePath}\nClick to edit");
        try
        {
            _store.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Couldn't open {_store.FilePath}: {ex.Message}");
            Render();
        }
    }

    private async void ChooseFile()
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".markdown");
        var result = await picker.PickSingleFileAsync();
        if (result == null) return;
        _prefs.BacklogPath = result.Path;
        _prefs.Save();
        OpenStore(result.Path);
    }

    private void Poll()
    {
        if (_state.RollOverIfNewDay())
        {
            _state.Save();
            Render();
        }
        try
        {
            _store.PollForExternalChanges();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Sync paused: {ex.Message}");
        }
        if (_renderPending && !IsTyping()) Render();
    }

    private void OnStoreChanged(bool external)
    {
        // While the user is typing, don't rebuild the list out from under them.
        if (external && IsTyping())
        {
            _renderPending = true;
            return;
        }
        Render();
        QueueRequestedResearch();
    }

    /// <summary>Applies a change to backlog.md. Problems show in the footer instead of crashing.</summary>
    private void Apply(Func<BacklogDocument, bool> change)
    {
        try
        {
            if (!_store.Mutate(change))
            {
                if (_store.Document.Errors.Count > 0) ShowStatus("Fix the problems in backlog.md first");
                Render();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Couldn't save: {ex.Message}");
        }
    }

    private void ApplyToTask(TaskRef reference, Func<BacklogDocument, TaskItem, bool> change) =>
        Apply(doc => reference.Resolve(doc) is { } task && change(doc, task));

    private void ShowStatus(string message)
    {
        FileText.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private static void ShellOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    /// <summary>Opens the backlog in VS Code when it's installed, otherwise in the default editor.</summary>
    private void EditInEditor()
    {
        try
        {
            Process.Start(new ProcessStartInfo("code", $"\"{_store.FilePath}\"") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ShellOpen(_store.FilePath);
        }
    }

    private void OnOpenInEditorClick(object sender, RoutedEventArgs e) => EditInEditor();

    // ---------- Window bounds ----------

    private void RestoreWindowBounds()
    {
        var size = Environment.GetEnvironmentVariable("TASKLIST_PREVIEW_BG") == "1"
            ? new SizeInt32(690, 1350)
            : new SizeInt32(_prefs.WindowWidth ?? 460, _prefs.WindowHeight ?? 900);
        if (_prefs.PinnedToCorner && Environment.GetEnvironmentVariable("TASKLIST_PREVIEW_BG") != "1")
        {
            AppWindow.Resize(size);
            DispatcherQueue.TryEnqueue(() => SnapToCorner(animate: false));
            return;
        }
        if (_prefs.WindowX is int x && _prefs.WindowY is int y)
        {
            var rect = new RectInt32(x, y, size.Width, size.Height);
            if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) != null)
            {
                AppWindow.MoveAndResize(rect);
                return;
            }
        }
        AppWindow.Resize(size);
    }

    private void SaveWindowBounds()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            // Remember the task window itself, not the news panel that was open beside it.
            int panel = (int)(NewsColumn.Width.Value * (Root.XamlRoot?.RasterizationScale ?? 1));
            _prefs.WindowX = AppWindow.Position.X + panel;
            _prefs.WindowY = AppWindow.Position.Y;
            _prefs.WindowWidth = AppWindow.Size.Width - panel;
            _prefs.WindowHeight = AppWindow.Size.Height;
        }
        _prefs.Save();
        _state.Save();
    }
}
