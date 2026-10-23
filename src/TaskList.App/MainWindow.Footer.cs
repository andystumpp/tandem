using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TaskList.Core;
using Windows.Graphics;

namespace TaskList.App;

/// <summary>The footer bar and the window placement it controls (pin to corner, keep on top).</summary>
public sealed partial class MainWindow
{
    private bool _movingWindow;

    private void BuildFooter()
    {
        FooterButtons.Children.Clear();
        FooterButtons.Children.Add(FooterButton("", "Add a task (Ctrl+N)", () => BeginAdd("focus")));
        FooterButtons.Children.Add(BuildResearchButton());
        FooterButtons.Children.Add(FooterButton("", _prefs.PinnedToCorner ? "Pinned to the corner (drag the window to unpin)" : "Pin to bottom-right corner",
            TogglePinToCorner, _prefs.PinnedToCorner));
        FooterButtons.Children.Add(FooterButton(_prefs.AlwaysOnTop ? "" : "",
            _prefs.AlwaysOnTop ? "Kept on top of other windows" : "Keep on top of other windows", ToggleAlwaysOnTop, _prefs.AlwaysOnTop));
        var theme = FooterButton("", "Theme", () => { });
        theme.Flyout = BuildThemeFlyout();
        FooterButtons.Children.Add(theme);
        FooterButtons.Children.Add(FooterButton("", "Reload backlog.md now", () => OpenStore(_store.FilePath)));
        FooterButtons.Children.Add(FooterButton("", "Open another task file", ChooseFile));
        FooterButtons.Children.Add(FooterButton("", "Edit backlog.md (VS Code)", EditInEditor));
    }

    private Button FooterButton(string glyph, string tooltip, Action onClick, bool highlighted = false)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = highlighted ? _palette.AccentBrush : _palette.TextSecondary },
            Background = _palette.Transparent,
            BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
            Padding = new Microsoft.UI.Xaml.Thickness(7, 6, 7, 6),
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(6),
            MinWidth = 0,
        };
        ApplyQuietHover(button);
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---------- Research menu ----------

    private List<TaskItem> UnreadResearch() =>
        _store?.Document.AllTasks.Where(t => ResearchRunner.HasResult(t) &&
            !_state.SeenResearch.Contains(Path.GetFileName(ResearchRunner.ResultPath(t)))).ToList() ?? new();

    private void OpenResearch(TaskItem task)
    {
        var path = ResearchRunner.ResultPath(task);
        var name = Path.GetFileName(path);
        if (!_state.SeenResearch.Contains(name))
        {
            _state.SeenResearch.Add(name);
            _state.Save();
            BuildFooter();
        }
        ShellOpen(path);
    }

    private Button BuildResearchButton()
    {
        int running = _researchRunning.Count + _researchQueue.Count;
        int unread = UnreadResearch().Count;
        int badgeCount = running > 0 ? running : unread;

        var grid = new Grid();
        grid.Children.Add(Sparkle(running > 0 || unread > 0 ? _palette.AiGradient : _palette.TextSecondary, 16));
        if (badgeCount > 0)
        {
            grid.Children.Add(new Border
            {
                Background = _palette.AiGradient,
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(7),
                MinWidth = 14,
                Height = 14,
                Padding = new Microsoft.UI.Xaml.Thickness(3, 0, 3, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Microsoft.UI.Xaml.Thickness(0, -6, -9, 0),
                Child = new TextBlock { Text = badgeCount.ToString(), FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = _palette.OnAccent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }
        var tooltip = running > 0 ? $"{AiCli.DisplayName} is researching {running} task(s)" : unread > 0 ? $"{unread} new research result(s)" : "Research";
        var button = FooterButton("", tooltip, () => { });
        button.Content = grid;
        button.Flyout = BuildResearchMenu();
        button.Click += (_, _) => button.Flyout = BuildResearchMenu();
        return button;
    }

    private MenuFlyout BuildResearchMenu()
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.TopEdgeAlignedRight };
        foreach (var text in _researchRunning)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = $"Researching: {Shorten(text)}", IsEnabled = false, Icon = new FontIcon { Glyph = "" } });
        }
        foreach (var (_, text) in _researchQueue)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = $"Queued: {Shorten(text)}", IsEnabled = false, Icon = new FontIcon { Glyph = "" } });
        }
        var unread = UnreadResearch();
        var results = _store?.Document.AllTasks.Where(ResearchRunner.HasResult).ToList() ?? new();
        if (menu.Items.Count > 0 && results.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var task in results)
        {
            bool isNew = unread.Contains(task);
            menu.Items.Add(MenuItem((isNew ? "● " : "") + Shorten(task.Text), "", () => ShowResearchPreview(task.Text)));
        }
        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "No research yet. Hover a task and click its bulb.", IsEnabled = false });
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var auto = new ToggleMenuFlyoutItem { Text = "Research questions and research-style tasks automatically", IsChecked = _prefs.AutoResearch };
        auto.Click += (_, _) =>
        {
            _prefs.AutoResearch = auto.IsChecked;
            _prefs.Save();
            if (_prefs.AutoResearch) QueueRequestedResearch();
        };
        menu.Items.Add(auto);
        menu.Items.Add(MenuItem("Open research folder", "", () =>
        {
            Directory.CreateDirectory(ResearchRunner.ResearchFolder);
            Process.Start(new ProcessStartInfo(ResearchRunner.ResearchFolder) { UseShellExecute = true });
        }));
        return menu;
    }

    private static string Shorten(string s) => s.Length <= 48 ? s : s[..45] + "…";

    // ---------- Window placement ----------

    private void ToggleAlwaysOnTop()
    {
        _prefs.AlwaysOnTop = !_prefs.AlwaysOnTop;
        _prefs.Save();
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = _prefs.AlwaysOnTop;
        BuildFooter();
    }

    private void TogglePinToCorner()
    {
        _prefs.PinnedToCorner = !_prefs.PinnedToCorner;
        _prefs.Save();
        if (_prefs.PinnedToCorner) SnapToCorner(animate: true);
        BuildFooter();
    }

    private PointInt32 CornerPosition()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double scale = Root.XamlRoot?.RasterizationScale ?? 1;
        int margin = (int)(12 * scale);
        return new PointInt32(work.X + work.Width - AppWindow.Size.Width - margin, work.Y + work.Height - AppWindow.Size.Height - margin);
    }

    /// <summary>Glides the window into the bottom-right corner of the screen's work area.</summary>
    private void SnapToCorner(bool animate)
    {
        var target = CornerPosition();
        var start = AppWindow.Position;
        if (!animate || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            MoveWindow(target);
            return;
        }
        var began = DateTime.Now;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(12);
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (DateTime.Now - began).TotalMilliseconds / 300);
            double e = 1 - Math.Pow(1 - t, 3);
            MoveWindow(new PointInt32((int)(start.X + (target.X - start.X) * e), (int)(start.Y + (target.Y - start.Y) * e)));
            if (t >= 1) timer.Stop();
        };
        timer.Start();
    }

    private void MoveWindow(PointInt32 position)
    {
        _movingWindow = true;
        AppWindow.Move(position);
        _movingWindow = false;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_prefs.PinnedToCorner || _movingWindow) return;
        if (args.DidSizeChange)
        {
            // Resizing keeps the window anchored to the corner.
            DispatcherQueue.TryEnqueue(() => SnapToCorner(animate: false));
            return;
        }
        if (args.DidPositionChange)
        {
            var corner = CornerPosition();
            if (Math.Abs(sender.Position.X - corner.X) > 4 || Math.Abs(sender.Position.Y - corner.Y) > 4)
            {
                // The user dragged the window away: that unpins it.
                _prefs.PinnedToCorner = false;
                _prefs.Save();
                BuildFooter();
            }
        }
    }
}
