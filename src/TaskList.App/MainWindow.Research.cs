using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TaskList.Core;

namespace TaskList.App;

/// <summary>Background research through the Claude CLI, two jobs at a time.</summary>
public sealed partial class MainWindow
{
    private const int MaxConcurrentResearch = 2;

    private readonly List<(TaskRef Ref, string Text)> _researchQueue = new();
    private readonly HashSet<string> _researchRunning = new();
    private readonly HashSet<string> _researchFailed = new();

    /// <summary>
    /// Questions and research-style tasks (see <see cref="ResearchHeuristics"/>) get researched once,
    /// automatically, unless that's turned off.
    /// </summary>
    private void QueueRequestedResearch()
    {
        if (!_prefs.AutoResearch) return;
        var doc = _store.Document;
        for (int i = 0; i < doc.Focus.Count; i++)
        {
            var t = doc.Focus[i];
            if (ResearchHeuristics.ShouldResearch(t) && !t.Done) EnqueueResearch(TaskRef.InFocus(i, t), t, automatic: true);
        }
        foreach (var area in doc.Areas.Where(a => a.Active))
        {
            for (int i = 0; i < area.Tasks.Count; i++)
            {
                var t = area.Tasks[i];
                if (ResearchHeuristics.ShouldResearch(t) && !t.Done) EnqueueResearch(new TaskRef(area.Title, i, t.Text), t, automatic: true);
            }
        }
    }

    private void EnqueueResearch(TaskRef reference, TaskItem task, bool automatic)
    {
        if (_researchRunning.Contains(task.Text) || _researchQueue.Any(q => q.Text == task.Text)) return;
        if (automatic && (ResearchRunner.HasResult(task) || _researchFailed.Contains(task.Text))) return;
        _researchQueue.Add((reference, task.Text));
        PumpResearch();
    }

    private void PumpResearch()
    {
        bool started = false;
        while (_researchQueue.Count > 0 && _researchRunning.Count < MaxConcurrentResearch)
        {
            var (reference, text) = _researchQueue[0];
            _researchQueue.RemoveAt(0);
            var doc = _store.Document;
            var task = reference.Resolve(doc) ?? doc.AllTasks.FirstOrDefault(t => t.Text == text);
            if (task == null) continue;
            var area = reference.AreaTitle == null ? null : doc.FindArea(reference.AreaTitle);
            var related = (area?.Tasks ?? doc.Focus).ToList();

            _researchRunning.Add(text);
            started = true;
            _ = RunResearch(task, area, related);
        }
        BuildFooter();
        if (started) Render();
    }

    private async Task RunResearch(TaskItem task, Area? area, List<TaskItem> related)
    {
        try
        {
            await Task.Run(() => ResearchRunner.RunAsync(task, area, related));
            _researchFailed.Remove(task.Text);
            _state.SeenResearch.Remove(Path.GetFileName(ResearchRunner.ResultPath(task)));
            _state.Save();
            AddResearchBanner(task.Text);
            // Windows' own notification only when the app isn't in front; otherwise the banner says it.
            if (!IsForeground()) Notify("Research ready", task.Text);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _researchFailed.Add(task.Text);
            ShowStatus($"Research needs {AiCli.DisplayName}'s command-line tool on your PATH (see the AI engine setting)");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _researchFailed.Add(task.Text);
            ShowStatus($"Research failed: {ex.Message.Split('\n')[0]}");
        }
        finally
        {
            _researchRunning.Remove(task.Text);
            Render();
            PumpResearch();
        }
    }

    private static void Notify(string title, string body)
    {
        try
        {
            AppNotificationManager.Default.Show(new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification());
        }
        catch (Exception)
        {
            // Notifications are optional; the footer already shows the result.
        }
    }

    // ---------- "Research ready" banners ----------

    private static readonly TimeSpan BannerLifetime = TimeSpan.FromHours(1);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _bannerTimer;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private bool IsForeground() => GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void AddResearchBanner(string taskText)
    {
        _state.ResearchBanners[taskText] = DateTime.Now;
        _state.Save();
        RenderBanners(animateNewest: taskText);
    }

    private void DismissBanner(string taskText, FrameworkElement banner)
    {
        Motion.Exit(banner, () =>
        {
            _state.ResearchBanners.Remove(taskText);
            _state.Save();
            RenderBanners(animateNewest: null);
        });
    }

    /// <summary>Shows the banners that are less than an hour old, newest at the bottom (at most three).</summary>
    private void RenderBanners(string? animateNewest)
    {
        foreach (var stale in _state.ResearchBanners.Where(kv => DateTime.Now - kv.Value > BannerLifetime).Select(kv => kv.Key).ToList())
        {
            _state.ResearchBanners.Remove(stale);
        }

        BannerStack.Children.Clear();
        var live = _state.ResearchBanners.OrderBy(kv => kv.Value).TakeLast(3).ToList();
        foreach (var (text, at) in live)
        {
            var banner = BuildBanner(text, at);
            BannerStack.Children.Add(banner);
            if (text == animateNewest) banner.Loaded += (_, _) => Motion.EnterFromBelow(banner, 16, 0, 380);
        }

        // Check again in a minute so banners leave on time.
        if (_bannerTimer == null)
        {
            _bannerTimer = DispatcherQueue.CreateTimer();
            _bannerTimer.Interval = TimeSpan.FromMinutes(1);
            _bannerTimer.Tick += (_, _) => { if (_state.ResearchBanners.Count > 0) RenderBanners(null); };
            _bannerTimer.Start();
        }
    }

    private Border BuildBanner(string taskText, DateTime at)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // A gradient medallion with the sparkle marks it as Claude's work.
        var medal = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = _palette.AiGradient,
            VerticalAlignment = VerticalAlignment.Top,
            Child = Sparkle(_palette.OnAccent, 16),
        };
        grid.Children.Add(medal);

        var texts = new StackPanel { Spacing = 2 };
        var minutes = (int)(DateTime.Now - at).TotalMinutes;
        texts.Children.Add(new TextBlock
        {
            Text = minutes < 1 ? "Research ready · just now" : $"Research ready · {minutes} min ago",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = _palette.AccentBrush,
        });
        texts.Children.Add(new TextBlock
        {
            Text = taskText,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = _palette.Text,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var open = new HyperlinkButton { Content = "Preview findings", Padding = new Thickness(0, 4, 0, 0), FontSize = 12, Foreground = _palette.AccentBrush };
        texts.Children.Add(open);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var close = CloseButton("Dismiss");
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        var banner = new Border
        {
            Child = grid,
            // Nearly opaque so it reads clearly over the list beneath it, lifted with a soft shadow.
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.WithAlpha(_palette.Dark ? Palette.Hex("#1A1C26") : Palette.Hex("#FFFFFF"), 0xF0)),
            BorderBrush = _palette.PanelStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 12, 6, 10),
            Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 0, 24),
        };

        // Clicking anywhere on the banner (or its link) opens the in-app preview and clears the banner.
        void Preview()
        {
            ShowResearchPreview(taskText);
            DismissBanner(taskText, banner);
        }
        open.Click += (_, _) => Preview();
        banner.Tapped += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && (IsInside(source, close) || IsInside(source, open))) return;
            Preview();
        };
        close.Click += (_, _) => DismissBanner(taskText, banner);
        return banner;
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var e = element; e != null; e = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(e))
        {
            if (e == container) return true;
        }
        return false;
    }

    // ---------- Research preview ----------

    /// <summary>Shows a research result inside the app: rendered Markdown, with open-file and research-again actions.</summary>
    private void ShowResearchPreview(string taskText)
    {
        try
        {
            BuildResearchPreview(taskText);
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't show the research: {ex.Message}");
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(Preferences.DefaultPath)!, "crash.log"), $"{DateTime.Now:u} preview: {ex}{Environment.NewLine}");
        }
    }

    private void BuildResearchPreview(string taskText)
    {
        var task = _store.Document.AllTasks.FirstOrDefault(t => t.Text == taskText) ?? new TaskItem(taskText);
        var path = ResearchRunner.ResultPath(task);
        if (!File.Exists(path)) return;
        var name = Path.GetFileName(path);
        if (!_state.SeenResearch.Contains(name))
        {
            _state.SeenResearch.Add(name);
            _state.Save();
            BuildFooter();
        }

        // The sheet shows the title and time itself, so drop the file's own heading and timestamp line.
        var lines = File.ReadAllLines(path).ToList();
        string when = "";
        if (lines.Count > 0 && lines[0].StartsWith("# Research")) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        string by = "Claude";
        if (lines.Count > 0 && lines[0].StartsWith("_Generated by "))
        {
            var meta = lines[0].Trim('_').TrimEnd('.')["Generated by ".Length..];
            int on = meta.LastIndexOf(" on ", StringComparison.Ordinal);
            if (on > 0) { by = meta[..on]; when = meta[(on + 4)..]; }
            lines.RemoveAt(0);
        }

        PreviewOverlay.Children.Clear();
        var scrim = new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.WithAlpha(Palette.Hex("#0B0C12"), (byte)(_palette.Dark ? 0x80 : 0x40))) };
        scrim.Tapped += (_, _) => HideResearchPreview();
        PreviewOverlay.Children.Add(scrim);

        var sheet = new Grid
        {
            Margin = new Thickness(14, 4, 14, 10),
            CornerRadius = new CornerRadius(16),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(_palette.Dark ? Palette.Hex("#191B25") : Palette.Hex("#FFFFFF")),
            BorderBrush = _palette.PanelStroke,
            BorderThickness = new Thickness(1),
            Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 0, 32),
        };
        sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sheet.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: sparkle medallion, label and time, the task, close.
        var header = new Grid { Padding = new Thickness(18, 16, 10, 12), ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = _palette.AiGradient,
            VerticalAlignment = VerticalAlignment.Top,
            Child = Sparkle(_palette.OnAccent, 17),
        });
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = when.Length > 0 ? $"Research by {by} · {when}" : $"Research by {by}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = _palette.AccentBrush });
        titles.Children.Add(new TextBlock { Text = task.Text, FontSize = 16, FontWeight = FontWeights.SemiBold, FontFamily = DisplayFont, Foreground = _palette.Text, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        var close = CloseButton("Close (Esc)");
        close.Click += (_, _) => HideResearchPreview();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        sheet.Children.Add(header);

        var body = new ScrollViewer { Padding = new Thickness(18, 0, 18, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Content = MarkdownView.Render(string.Join("\n", lines), _palette);
        Grid.SetRow(body, 1);
        sheet.Children.Add(body);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(18, 10, 18, 14) };
        var again = new Button { Content = "Research again" };
        again.Click += (_, _) =>
        {
            HideResearchPreview();
            if (FindRef(task.Text) is { } reference) EnqueueResearch(reference, task, automatic: false);
        };
        var file = new Button { Content = "Open file" };
        file.Click += (_, _) => ShellOpen(path);
        var done = new Button { Content = "Done", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        done.Click += (_, _) => HideResearchPreview();
        actions.Children.Add(again);
        actions.Children.Add(file);
        actions.Children.Add(done);
        Grid.SetRow(actions, 2);
        sheet.Children.Add(actions);

        var escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, e) => { HideResearchPreview(); e.Handled = true; };
        sheet.KeyboardAccelerators.Add(escape);
        sheet.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;

        PreviewOverlay.Children.Add(sheet);
        PreviewOverlay.Visibility = Visibility.Visible;
        Motion.FadeIn(scrim, 200);
        Motion.EnterFromBelow(sheet, 24, 0, 340);
        done.Loaded += (_, _) => done.Focus(FocusState.Programmatic);
    }

    private void HideResearchPreview()
    {
        if (PreviewOverlay.Visibility != Visibility.Visible) return;
        Motion.Exit(PreviewOverlay, () =>
        {
            PreviewOverlay.Visibility = Visibility.Collapsed;
            PreviewOverlay.Children.Clear();
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PreviewOverlay);
            visual.Opacity = 1;
            visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
        });
    }

    private TaskRef? FindRef(string text)
    {
        var doc = _store.Document;
        int i = doc.Focus.FindIndex(t => t.Text == text);
        if (i >= 0) return TaskRef.InFocus(i, doc.Focus[i]);
        foreach (var area in doc.Areas)
        {
            int j = area.Tasks.FindIndex(t => t.Text == text);
            if (j >= 0) return new TaskRef(area.Title, j, text);
        }
        return null;
    }
}
