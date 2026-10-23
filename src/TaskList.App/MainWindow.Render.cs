using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TaskList.Core;
using Windows.System;
using Windows.UI.Text;

namespace TaskList.App;

/// <summary>Builds the single column: header numbers, Focus, Areas, inactive areas.</summary>
public sealed partial class MainWindow
{
    private bool _rendering;
    private bool _renderPending;
    private string? _addingKey;
    private bool _firstRender = true;
    private bool _focusExpanded;
    private int? _revealFocusFrom;
    private string? _justAdded;
    private string? _revealArea;
    private const int FocusFoldAt = 6;
    private bool _animateAddBox;
    private readonly HashSet<string> _expandedTasks = new();

    /// <summary>Width of the rank column in area cards; task rows align past it.</summary>
    private const double RankColumn = 30;
    private static readonly FontFamily DisplayFont = new("Segoe UI Variable Display");
    private readonly HashSet<string> _expandedOutcomes = new();

    /// <summary>True while the user has typed something we'd throw away by re-rendering.</summary>
    private bool IsTyping() =>
        Root.XamlRoot != null && FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox { Text.Length: > 0 };

    private void Render()
    {
        if (_rendering || _store == null) return;
        _rendering = true;
        try
        {
            _renderPending = false;
            var doc = _store.Document;
            EnsureNext(doc);
            UpdateHeader(doc);
            ContentPanel.ChildrenTransitions = _firstRender && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled
                ? new TransitionCollection { new EntranceThemeTransition { IsStaggeringEnabled = true, FromVerticalOffset = 24 } }
                : null;
            _firstRender = false;
            ContentPanel.Children.Clear();

            if (doc.Errors.Count > 0) ContentPanel.Children.Add(BuildErrorBanner(doc));

            ContentPanel.Children.Add(BuildFocusHeader(doc));
            ContentPanel.Children.Add(BuildFocusPanel(doc));

            var active = doc.Areas.Where(a => a.Active).OrderBy(a => a.Priority ?? int.MaxValue).ToList();
            var inactive = doc.Areas.Where(a => !a.Active).ToList();
            ContentPanel.Children.Add(SectionHeader("AREAS", active.Count, Margin(6, 22, 6, 4)));
            for (int i = 0; i < active.Count; i++)
            {
                ContentPanel.Children.Add(BuildArea(active[i], i + 1));
            }
            if (active.Count == 0)
            {
                ContentPanel.Children.Add(Hint("No active areas. Add one with \"## [Active:true] Name\" in backlog.md."));
            }

            if (inactive.Count > 0)
            {
                // Inactive areas stay in view but start collapsed: kept around, not competing for attention.
                ContentPanel.Children.Add(SectionHeader("INACTIVE", inactive.Count, Margin(6, 18, 6, 4), muted: true));
                foreach (var area in inactive) ContentPanel.Children.Add(BuildArea(area, null));
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private string? _nextText;

    /// <summary>
    /// NEXT is derived, never set by hand: the first open task planned for today, in the order the
    /// list shows (Focus, then active areas by priority); with nothing planned, simply the first open task.
    /// </summary>
    private void EnsureNext(BacklogDocument doc)
    {
        var inOrder = BacklogDocument.Ordered(doc.Focus).Select(x => x.Task)
            .Concat(doc.Areas.Where(a => a.Active).OrderBy(a => a.Priority ?? int.MaxValue)
                .SelectMany(a => BacklogDocument.Ordered(a.Tasks).Select(x => x.Task)))
            .Where(t => !t.Done && t.Owner == null)
            .ToList();
        _nextText = (inOrder.FirstOrDefault(_state.IsToday) ?? inOrder.FirstOrDefault())?.Text;
    }

    private bool IsNext(TaskItem t) => !t.Done && t.Text == _nextText;

    private void UpdateHeader(BacklogDocument doc)
    {
        var (done, open) = doc.Progress;
        int total = done + open;
        int percent = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);
        _ring.SetValue(total == 0 ? 0 : (double)done / total);
        PercentText.Inlines.Clear();
        PercentText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = percent.ToString() });
        PercentText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "%", FontSize = 10, Foreground = _palette.HeaderSecondary });
        TitleRun.Text = doc.Title ?? "Tasks";
        DateRun.Text = $" · {DateTime.Now:ddd, MMM d}";

        MomentumRun.Text = total == 0 ? "A clean slate." :
            open == 0 ? "All done. Nice work." :
            done == 0 ? $"{open} to go. Pick one and start." :
            percent < 50 ? $"{done} down, {open} to go. Keep going." :
            $"{done} down, {open} to go. Keep the momentum.";

        var all = doc.AllTasks.ToList();
        var planned = all.Where(_state.IsToday).ToList();
        CountsRun.Text = $"{all.Count(t => !t.Done)} open · {all.Count(t => t.Done)} done · {planned.Count(t => t.Done)}/{planned.Count} planned";
    }

    private bool IsVisible(TaskItem t) =>
        !(_prefs.HideDone && t.Done) && (!_prefs.TodayOnly || _state.IsToday(t) || IsNext(t));

    // ---------- Sections ----------

    private Border BuildErrorBanner(BacklogDocument doc)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = doc.Errors.Count == 1 ? "backlog.md has a problem" : $"backlog.md has {doc.Errors.Count} problems",
            FontWeight = FontWeights.SemiBold,
            Foreground = _palette.Text,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Changes are paused so nothing gets overwritten. Fix these in your editor and the list updates on its own.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = _palette.TextSecondary,
        });
        foreach (var error in doc.Errors.Take(6))
        {
            stack.Children.Add(new TextBlock { Text = $"Line {error.Line}: {error.Message}", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = _palette.Text });
        }
        var open = new HyperlinkButton { Content = "Open backlog.md", Padding = new Thickness(0, 4, 0, 0) };
        open.Click += (_, _) => ShellOpen(_store.FilePath);
        stack.Children.Add(open);
        return new Border
        {
            Child = stack,
            Background = new SolidColorBrush(Palette.WithAlpha(_palette.Theme.Accent, 0x26)),
            BorderBrush = _palette.AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = Margin(0, 0, 0, 8),
        };
    }

    private Grid BuildFocusHeader(BacklogDocument doc)
    {
        var grid = new Grid { Margin = Margin(6, 4, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        int open = doc.Focus.Count(t => !t.Done);
        grid.Children.Add(SectionHeader("FOCUS", open, new Thickness(0)));

        var pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var today = Pill("", "Today", _prefs.TodayOnly, "Show only tasks planned for today");
        today.Click += (_, _) => { _prefs.TodayOnly = !_prefs.TodayOnly; _prefs.Save(); Render(); };
        pills.Children.Add(today);

        int doneCount = doc.AllTasks.Count(t => t.Done);
        var hide = Pill(_prefs.HideDone ? "" : "", _prefs.HideDone ? $"{doneCount} done hidden" : "Hide done",
            _prefs.HideDone, _prefs.HideDone ? "Show completed tasks" : "Hide completed tasks");
        hide.Click += (_, _) => { _prefs.HideDone = !_prefs.HideDone; _prefs.Save(); Render(); };
        pills.Children.Add(hide);
        Grid.SetColumn(pills, 1);
        grid.Children.Add(pills);
        return grid;
    }

    private Border BuildFocusPanel(BacklogDocument doc)
    {
        var stack = new StackPanel();
        if (_addingKey == "focus") stack.Children.Add(BuildAddBox("focus", "Add a focus task", text => Apply(d =>
        {
            var added = BacklogMarkdown.ParseTask(text);
            d.Focus.Add(added);
            _justAdded = added.Text;
            return true;
        })));

        // Long focus lists fold after a few rows; NEXT always stays visible.
        var visible = BacklogDocument.Ordered(doc.Focus).Where(x => IsVisible(x.Task)).ToList();
        int limit = _focusExpanded || visible.Count <= FocusFoldAt + 1 ? visible.Count : FocusFoldAt;
        int shown = 0;
        for (int i = 0; i < visible.Count; i++)
        {
            var (task, index) = visible[i];
            if (i >= limit && !IsNext(task)) continue;
            var row = BuildTaskRow(TaskRef.InFocus(index, task), task, null);
            if (_revealFocusFrom is int from && i >= from)
            {
                int delay = (i - from) * 35;
                row.Loaded += (_, _) => Motion.EnterFromBelow(row, 8, delay);
            }
            stack.Children.Add(row);
            shown++;
        }
        _revealFocusFrom = null;
        if (visible.Count > FocusFoldAt + 1)
        {
            int hidden = visible.Count - shown;
            var more = new HyperlinkButton
            {
                Content = _focusExpanded ? "Show less" : $"Show {hidden} more",
                FontSize = 12,
                Foreground = _palette.AccentBrush,
                Margin = Margin(42, 0, 0, 2),
                Padding = new Thickness(0, 2, 0, 4),
            };
            more.Click += (_, _) =>
            {
                _focusExpanded = !_focusExpanded;
                if (_focusExpanded) _revealFocusFrom = FocusFoldAt;
                Render();
            };
            stack.Children.Add(more);
        }
        if (shown == 0 && _addingKey != "focus")
        {
            stack.Children.Add(Hint(doc.Focus.Count > 0
                ? "Everything in focus is done or filtered out."
                : "Nothing in focus yet. Press + to add the few things that matter today.", Margin(14, 12, 14, 12)));
        }
        return Card(stack);
    }

    /// <summary>One glass card per area. The number is the area's rank by priority.</summary>
    private Border BuildArea(Area area, int? rank)
    {
        bool collapsed = IsCollapsed(area);
        var stack = new StackPanel();

        // Header: rank, title, + (on hover), progress, chevron. Tap to collapse; right-click for options.
        bool showOutcome = _prefs.ShowOutcomes && area.Outcome.Length > 0;
        var header = new Grid { Padding = new Thickness(12, 10, 14, showOutcome ? 4 : (collapsed ? 10 : 6)), CornerRadius = new CornerRadius(10), Background = _palette.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RankColumn) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = rank is int n ? n.ToString("00") : "–",
            FontFamily = DisplayFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = rank != null ? _palette.AccentBrush : _palette.TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var title = new TextBlock
        {
            Text = area.Title,
            FontFamily = DisplayFont,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = _palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        // The + takes no room of its own: it fades in on the header while the pointer is over the area.
        var add = RowIcon("", $"Add a task to {area.Title}", _palette.AccentBrush);
        add.VerticalAlignment = VerticalAlignment.Center;
        add.Margin = Margin(4, 0, 0, 0);
        add.Opacity = 0;
        Motion.SmoothOpacity(add);
        add.Click += (_, _) => BeginAdd("area:" + area.Title);
        Grid.SetColumn(add, 2);
        header.Children.Add(add);

        int done = area.Tasks.Count(t => t.Done);
        var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = Margin(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        progress.Children.Add(MiniBar(area.Tasks.Count == 0 ? 0 : (double)done / area.Tasks.Count));
        progress.Children.Add(new TextBlock { Text = $"{done}/{area.Tasks.Count}", FontSize = 12, Foreground = _palette.TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(progress, 3);
        header.Children.Add(progress);

        var chevron = new FontIcon { Glyph = collapsed ? "" : "", FontSize = 10, Foreground = _palette.TextTertiary };
        Grid.SetColumn(chevron, 4);
        header.Children.Add(chevron);

        StackPanel? tasksPanel = null;
        header.Tapped += (_, _) =>
        {
            void Toggle()
            {
                if (SetCollapsed(area, !collapsed) == false) _revealArea = area.Title;
                Render();
            }
            if (!collapsed && tasksPanel != null) Motion.Exit(tasksPanel, Toggle, 150);
            else Toggle();
        };
        HoverHighlight(header, null);
        header.ContextFlyout = BuildAreaMenu(area);
        ToolTipService.SetToolTip(header, collapsed ? "Expand · right-click for options" : "Collapse · right-click for options");
        stack.Children.Add(header);

        if (showOutcome)
        {
            // The outcome folds to one line under the title; the chevron opens the full text.
            bool expanded = _expandedOutcomes.Contains(area.Title);
            var outcome = new Grid { Margin = Margin(12 + RankColumn - 16, 0, 18, collapsed ? 10 : 4), Background = _palette.Transparent, ColumnSpacing = 4 };
            outcome.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            outcome.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outcome.Children.Add(new FontIcon
            {
                Glyph = expanded ? "" : "",
                FontSize = 8,
                Foreground = _palette.TextTertiary,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = Margin(0, 5, 0, 0),
            });
            var text = new TextBlock
            {
                Text = area.Outcome,
                FontSize = 12.5,
                LineHeight = 18,
                Foreground = _palette.TextSecondary,
                TextWrapping = expanded ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(text, 1);
            outcome.Children.Add(text);
            ToolTipService.SetToolTip(outcome, expanded ? "Fold the outcome" : "Show the full outcome");
            outcome.Tapped += (_, e) =>
            {
                if (!_expandedOutcomes.Remove(area.Title)) _expandedOutcomes.Add(area.Title);
                e.Handled = true;
                Render();
            };
            stack.Children.Add(outcome);
        }

        if (!collapsed)
        {
            // Tasks line up with the area title, not the rank.
            var tasks = new StackPanel { Margin = Margin(RankColumn - 6, 0, 0, 4) };
            tasksPanel = tasks;
            if (_revealArea == area.Title)
            {
                _revealArea = null;
                tasks.Loaded += (_, _) => Motion.EnterFromBelow(tasks, 10);
            }
            int shown = 0;
            foreach (var (task, index) in BacklogDocument.Ordered(area.Tasks))
            {
                if (!IsVisible(task)) continue;
                tasks.Children.Add(BuildTaskRow(new TaskRef(area.Title, index, task.Text), task, area));
                shown++;
            }
            var key = "area:" + area.Title;
            if (_addingKey == key)
            {
                tasks.Children.Add(BuildAddBox(key, $"Add a task to {area.Title}", text => Apply(d =>
                {
                    if (d.FindArea(area.Title) is not { } a) return false;
                    var added = BacklogMarkdown.ParseTask(text);
                    a.Tasks.Add(added);
                    _justAdded = added.Text;
                    return true;
                })));
            }
            else if (shown == 0)
            {
                tasks.Children.Add(Hint(area.Tasks.Count > 0 ? "All done here." : "No tasks yet. Right-click the title to add one.", Margin(10, 2, 10, 8)));
            }
            stack.Children.Add(tasks);
        }

        var card = Card(stack, padding: 4);
        card.Opacity = area.Active ? 1 : 0.62;
        card.PointerEntered += (_, _) => add.Opacity = 1;
        card.PointerExited += (_, _) => add.Opacity = 0;
        return card;
    }

    /// <summary>Active areas start expanded and inactive ones collapsed; each remembers what you chose.</summary>
    private bool IsCollapsed(Area area) =>
        area.Active ? _prefs.CollapsedAreas.Contains(area.Title) : !_prefs.ExpandedAreas.Contains(area.Title);

    /// <summary>Returns the new collapsed state.</summary>
    private bool SetCollapsed(Area area, bool collapsed)
    {
        if (area.Active)
        {
            _prefs.CollapsedAreas.Remove(area.Title);
            if (collapsed) _prefs.CollapsedAreas.Add(area.Title);
        }
        else
        {
            _prefs.ExpandedAreas.Remove(area.Title);
            if (!collapsed) _prefs.ExpandedAreas.Add(area.Title);
        }
        _prefs.Save();
        return collapsed;
    }

    // ---------- Task row ----------

    private Grid BuildTaskRow(TaskRef reference, TaskItem task, Area? area)
    {
        bool next = IsNext(task) && !task.Done;
        bool today = _state.IsToday(task);
        bool waiting = task.Owner != null && !task.Done;

        var row = new Grid
        {
            Padding = new Thickness(12, 9, 6, 9),
            CornerRadius = new CornerRadius(10),
            Background = next ? _palette.NextBand : today ? _palette.TodayBand : _palette.Transparent,
            Margin = next || today ? Margin(0, 1, 0, 1) : new Thickness(0),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (next)
        {
            // A short accent spine marks the one thing to do now.
            var spine = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                Background = _palette.AiGradientVertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = Margin(-9, 3, 0, 3),
            };
            Grid.SetColumnSpan(spine, 2);
            row.Children.Add(spine);
        }

        // Round checkbox. Ticking it fills right away, then the list settles a beat later.
        var circle = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1.5),
            BorderBrush = task.Done || next ? _palette.AccentBrush : _palette.TextTertiary,
            Background = task.Done ? _palette.AccentBrush : _palette.Transparent,
            Margin = Margin(0, next ? 2 : 1, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (task.Done) circle.Child = CheckMark();
        ToolTipService.SetToolTip(circle, task.Done ? "Mark as open" : "Mark as done");
        circle.PointerEntered += (_, _) => { if (!task.Done) circle.BorderBrush = _palette.AccentBrush; };
        circle.PointerExited += (_, _) => { if (!task.Done && !next) circle.BorderBrush = _palette.TextTertiary; };

        var text = new TextBlock
        {
            Text = task.Text,
            FontSize = next ? 15 : 14,
            LineHeight = next ? 21 : 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = task.Done ? _palette.TextTertiary : waiting ? _palette.TextSecondary : _palette.Text,
            FontWeight = next ? FontWeights.SemiBold : FontWeights.Normal,
            TextDecorations = task.Done ? TextDecorations.Strikethrough : TextDecorations.None,
            VerticalAlignment = VerticalAlignment.Top,
            // Long tasks fold to two lines; the chevron beside them opens the rest.
            MaxLines = _expandedTasks.Contains(task.Text) ? 0 : 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (task.Priority is int p) ToolTipService.SetToolTip(text, $"Priority {p}");
        Grid.SetColumn(text, 1);

        circle.Tapped += (_, e) =>
        {
            e.Handled = true;
            if (!task.Done)
            {
                circle.Background = _palette.AccentBrush;
                circle.BorderBrush = _palette.AccentBrush;
                circle.Child = CheckMark();
                text.Foreground = _palette.TextTertiary;
                text.TextDecorations = TextDecorations.Strikethrough;
                Motion.Pop(circle);
                DelayThen(TimeSpan.FromMilliseconds(280), () =>
                {
                    // With done tasks hidden, the row bows out before the list settles.
                    if (_prefs.HideDone) Motion.Exit(row, () => ToggleDone(reference, task));
                    else ToggleDone(reference, task);
                });
            }
            else
            {
                Motion.Pop(circle);
                ToggleDone(reference, task);
            }
        };
        row.Children.Add(circle);
        row.Children.Add(text);
        text.DoubleTapped += (_, _) => BeginEdit(row, text, reference, task);

        var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(tags, 2);
        row.Children.Add(tags);
        if (task.Owner is { } owner) tags.Children.Add(Avatar(owner));

        // Fold/unfold for tasks longer than two lines. Shown only when there's more to see.
        var more = RowIcon(_expandedTasks.Contains(task.Text) ? "\uE70E" : "\uE70D", "Show the whole task", _palette.TextTertiary);
        ((FontIcon)more.Content).FontSize = 10;
        more.Margin = Margin(4, -1, 0, 0);
        more.Visibility = _expandedTasks.Contains(task.Text) ? Visibility.Visible : Visibility.Collapsed;
        more.Click += (_, _) =>
        {
            bool open = !_expandedTasks.Remove(task.Text);
            if (open) _expandedTasks.Add(task.Text);
            text.MaxLines = open ? 0 : 2;
            ((FontIcon)more.Content).Glyph = open ? "\uE70E" : "\uE70D";
            ToolTipService.SetToolTip(more, open ? "Fold to two lines" : "Show the whole task");
        };
        text.IsTextTrimmedChanged += (_, _) =>
        {
            if (!_expandedTasks.Contains(task.Text)) more.Visibility = text.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed;
        };
        tags.Children.Add(more);
        if (next)
        {
            var badge = new Border
            {
                BorderBrush = _palette.AccentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 2),
                Margin = Margin(10, 1, 2, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "NEXT", FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 160, Foreground = _palette.AccentBrush },
            };
            tags.Children.Add(badge);
        }

        // Plan-for-today and reorder: always on the NEXT row, otherwise on hover.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = Margin(4, -2, 0, 0) };
        var sun = RowIcon("", today ? "Remove from today's plan" : "Plan for today", today ? _palette.Today : _palette.TextTertiary);
        sun.Click += (_, _) => ToggleToday(task);
        var up = RowIcon("", "Move up", _palette.TextTertiary);
        up.Click += (_, _) => Apply(d => MoveVisual(d, reference, -1));
        var down = RowIcon("", "Move down", _palette.TextTertiary);
        down.Click += (_, _) => Apply(d => MoveVisual(d, reference, +1));
        actions.Children.Add(sun);
        actions.Children.Add(up);
        actions.Children.Add(down);
        Grid.SetColumn(actions, 3);
        row.Children.Add(actions);

        // Research: a spinner while Claude works; an accent bulb once there's something to read.
        bool running = _researchRunning.Contains(task.Text);
        bool hasResult = ResearchRunner.HasResult(task);
        bool flagged = hasResult || ResearchHeuristics.ShouldResearch(task);
        FrameworkElement research;
        if (running)
        {
            var spinner = new ProgressRing { IsActive = true, Width = 14, Height = 14, Margin = Margin(7, 2, 7, 0), Foreground = _palette.AccentBrush, VerticalAlignment = VerticalAlignment.Top };
            ToolTipService.SetToolTip(spinner, $"{AiCli.DisplayName} is researching this");
            research = spinner;
        }
        else
        {
            var bulb = RowIcon("", hasResult ? "Open research" : $"Research with {AiCli.DisplayName}", _palette.TextTertiary);
            bulb.Content = Sparkle(flagged ? _palette.AiGradient : _palette.TextTertiary);
            bulb.Click += (_, _) =>
            {
                if (hasResult) ShowResearchPreview(task.Text);
                else EnqueueResearch(reference, task, automatic: false);
            };
            research = bulb;
        }
        research.Margin = new Thickness(research.Margin.Left, -2, research.Margin.Right, 0);
        Grid.SetColumn(research, 4);
        row.Children.Add(research);

        void ShowActions(bool hover)
        {
            sun.Opacity = hover || next || today ? 1 : 0;
            up.Opacity = down.Opacity = hover || next ? 1 : 0;
            research.Opacity = hover || flagged || running ? 1 : 0;
        }
        ShowActions(false);

        foreach (var icon in new UIElement[] { sun, up, down, research }) Motion.SmoothOpacity(icon);
        HoverHighlight(row, next ? _palette.NextBand : today ? _palette.TodayBand : null, ShowActions);
        row.ContextFlyout = BuildTaskMenu(row, text, reference, task, area);
        if (_justAdded == task.Text)
        {
            _justAdded = null;
            row.Loaded += (_, _) => Motion.EnterFromBelow(row, 12);
        }
        return row;
    }

    /// <summary>The sparkle that marks Claude-powered things (research), drawn as a vector so it can take the AI gradient.</summary>
    private static PathIcon Sparkle(Brush fill, double size = 15) => new()
    {
        Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
            "M7,1 C7.5,4.4 9.1,6 12.5,6.5 C9.1,7 7.5,8.6 7,12 C6.5,8.6 4.9,7 1.5,6.5 C4.9,6 6.5,4.4 7,1 Z " +
            "M12.5,9.5 C12.75,11 13.4,11.65 14.9,11.9 C13.4,12.15 12.75,12.8 12.5,14.3 C12.25,12.8 11.6,12.15 10.1,11.9 C11.6,11.65 12.25,11 12.5,9.5 Z"),
        Foreground = fill,
        Width = size,
        Height = size,
    };

    /// <summary>A small round badge with the owner's initials, colored consistently per person.</summary>
    private Border Avatar(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2 ? $"{parts[0][0]}{parts[^1][0]}" : name[..Math.Min(2, name.Length)];
        string[] hues = ["#6D5BD0", "#1F9D6B", "#B7791F", "#2B7BD6", "#C2457E", "#0E9AA7", "#D14343", "#7A5BC4"];
        int hash = 0;
        foreach (var c in name.ToLowerInvariant()) hash = (hash * 31 + c) & 0x7fffffff;
        var color = Palette.Hex(hues[hash % hues.Length]);
        var badge = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(color),
            Margin = Margin(8, 0, 0, 0),
            Child = new TextBlock
            {
                Text = initials.ToUpperInvariant(),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Palette.Hex("#FFFFFF")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTipService.SetToolTip(badge, $"Assigned to {name}");
        return badge;
    }

    /// <summary>"Assign to" with everyone already used in the backlog, someone new, or nobody.</summary>
    private MenuFlyoutSubItem BuildAssignMenu(TaskRef reference, TaskItem task)
    {
        var assign = new MenuFlyoutSubItem { Text = task.Owner == null ? "Assign to" : $"Assigned to {task.Owner}", Icon = new FontIcon { Glyph = "\uE77B" } };
        var people = _store.Document.AllTasks.Select(t => t.Owner).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        foreach (var person in people)
        {
            var item = new ToggleMenuFlyoutItem { Text = person, IsChecked = string.Equals(task.Owner, person, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => ApplyToTask(reference, (_, t) => { t.Owner = person; return true; });
            assign.Items.Add(item);
        }
        if (people.Count > 0) assign.Items.Add(new MenuFlyoutSeparator());
        assign.Items.Add(MenuItem("Someone else…", "\uE8FA", async () =>
        {
            var name = await PromptAsync("Assign to", "", "Assign", "Name");
            if (name != null) ApplyToTask(reference, (_, t) => { t.Owner = name; return true; });
        }));
        if (task.Owner != null)
        {
            assign.Items.Add(MenuItem("Unassign", "\uE711", () => ApplyToTask(reference, (_, t) => { t.Owner = null; return true; })));
        }
        return assign;
    }

    private FontIcon CheckMark() => new() { Glyph = "", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = _palette.OnAccent };

    private void DelayThen(TimeSpan delay, Action action)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => action();
        timer.Start();
    }

    private Grid MiniBar(double fraction)
    {
        var bar = new Grid { Width = 36, Height = 4, CornerRadius = new CornerRadius(2), Background = _palette.Track, VerticalAlignment = VerticalAlignment.Center };
        if (fraction > 0)
        {
            bar.Children.Add(new Border
            {
                Width = Math.Max(4, 36 * fraction),
                CornerRadius = new CornerRadius(2),
                Background = _palette.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }
        return bar;
    }

    private MenuFlyout BuildTaskMenu(Grid row, TextBlock text, TaskRef reference, TaskItem task, Area? area)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("Edit", "", () => BeginEdit(row, text, reference, task)));
        menu.Items.Add(MenuItem(_state.IsToday(task) ? "Remove from today" : "Plan for today", "", () => ToggleToday(task)));

        menu.Items.Add(BuildAssignMenu(reference, task));

        var priority = new MenuFlyoutSubItem { Text = "Priority", Icon = new FontIcon { Glyph = "" } };
        foreach (var level in new int?[] { 1, 2, 3, 4, null })
        {
            var item = new ToggleMenuFlyoutItem { Text = level is int l ? $"P{l}" : "None", IsChecked = task.Priority == level };
            item.Click += (_, _) => ApplyToTask(reference, (_, t) => { t.Priority = level; return true; });
            priority.Items.Add(item);
        }
        menu.Items.Add(priority);

        var moveTo = new MenuFlyoutSubItem { Text = "Move to", Icon = new FontIcon { Glyph = "" } };
        if (!reference.IsFocus) moveTo.Items.Add(MenuItem("Focus", null, () => Apply(d => MoveTask(d, reference, doc => doc.Focus))));
        foreach (var a in _store.Document.Areas)
        {
            if (a.Title == reference.AreaTitle) continue;
            var target = a.Title;
            moveTo.Items.Add(MenuItem(a.Active ? target : $"{target} (inactive)", null, () => Apply(d => MoveTask(d, reference, doc => doc.FindArea(target)?.Tasks))));
        }
        menu.Items.Add(moveTo);

        menu.Items.Add(new MenuFlyoutSeparator());
        if (ResearchRunner.HasResult(task))
        {
            menu.Items.Add(MenuItem("Open research", "", () => ShowResearchPreview(task.Text)));
        }
        var research = MenuItem(ResearchRunner.HasResult(task) ? $"Research again with {AiCli.DisplayName}" : $"Research with {AiCli.DisplayName}", "",
            () => EnqueueResearch(reference, task, automatic: false));
        research.IsEnabled = !_researchRunning.Contains(task.Text);
        menu.Items.Add(research);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("Delete", "", () => Motion.Exit(row, () => Apply(d =>
            reference.Resolve(d) is { } t && reference.ResolveList(d) is { } list && list.Remove(t)))));
        return menu;
    }

    private MenuFlyout BuildAreaMenu(Area area)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("Add task", "", () => BeginAdd("area:" + area.Title)));
        menu.Items.Add(MenuItem("Edit outcome…", "", async () =>
        {
            var outcome = await PromptAsync("Outcome", area.Outcome, "Save", "What does success look like?");
            if (outcome != null) Apply(d => d.FindArea(area.Title) is { } a && Set(() => a.Outcome = outcome));
        }));
        menu.Items.Add(MenuItem("Rename…", "", async () =>
        {
            var name = await PromptAsync("Rename area", area.Title, "Rename");
            if (name == null || name == area.Title) return;
            if (_prefs.CollapsedAreas.Remove(area.Title)) _prefs.CollapsedAreas.Add(name);
            if (_prefs.ExpandedAreas.Remove(area.Title)) _prefs.ExpandedAreas.Add(name);
            _prefs.Save();
            Apply(d => d.FindArea(area.Title) is { } a && d.FindArea(name) == null && Set(() => a.Title = name));
        }));
        menu.Items.Add(MenuItem(area.Active ? "Make inactive" : "Make active", area.Active ? "" : "", () =>
            Apply(d => d.FindArea(area.Title) is { } a && Set(() => a.Active = !a.Active))));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("New area…", "", async () =>
        {
            var name = await PromptAsync("New area", "", "Add");
            if (name == null) return;
            Apply(d =>
            {
                if (d.FindArea(name) != null) return false;
                var a = new Area(name) { Active = true };
                d.Areas.Add(a);
                return true;
            });
        }));
        menu.Items.Add(MenuItem("Delete area…", "", async () =>
        {
            if (await ConfirmAsync($"Delete “{area.Title}”?", $"This removes the area and its {area.Tasks.Count} task(s) from backlog.md.", "Delete"))
            {
                Apply(d => d.FindArea(area.Title) is { } a && d.Areas.Remove(a));
            }
        }));
        return menu;
    }

    // ---------- Actions ----------

    private void ToggleDone(TaskRef reference, TaskItem shown)
    {
        ApplyToTask(reference, (d, t) =>
        {
            t.Done = !t.Done;
            if (t.Done)
            {
                _state.Completed[t.Text] = DateTime.Now;
            }
            else
            {
                _state.Completed.Remove(t.Text);
            }
            _state.Save();
            return true;
        });
    }

    private void ToggleToday(TaskItem task)
    {
        _state.SetToday(task, !_state.IsToday(task));
        _state.Save();
        Render();
    }

    private void BeginAdd(string key)
    {
        _addingKey = key;
        _animateAddBox = true;
        if (key.StartsWith("area:") && _store.Document.FindArea(key[5..]) is { } adding) SetCollapsed(adding, false);
        Render();
        if (key == "focus") MainScroll.ChangeView(null, 0, null);
    }

    private TextBox BuildAddBox(string key, string placeholder, Action<string> onAdd)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            Margin = Margin(4, 4, 4, 6),
            Background = _palette.Transparent,
            BorderBrush = _palette.PanelStroke,
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                var text = box.Text.Trim();
                if (text.Length > 0)
                {
                    box.Text = "";
                    onAdd(text);
                }
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                _addingKey = null;
                e.Handled = true;
                Render();
            }
        };
        box.LostFocus += (_, _) =>
        {
            if (_rendering || box.Text.Trim().Length > 0) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                // Close the box when focus leaves it empty (but not when we just re-rendered it).
                if (_addingKey == key && FocusManager.GetFocusedElement(Root.XamlRoot) is not TextBox)
                {
                    _addingKey = null;
                    Render();
                }
            });
        };
        bool animate = _animateAddBox;
        _animateAddBox = false;
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            if (animate) Motion.EnterFromBelow(box, 8);
        };
        return box;
    }

    private void BeginEdit(Grid row, TextBlock text, TaskRef reference, TaskItem task)
    {
        var box = new TextBox { Text = task.Text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(box, 1);
        row.Children.Remove(text);
        row.Children.Add(box);

        bool finished = false;
        void Finish(bool commit)
        {
            if (finished) return;
            finished = true;
            var newText = box.Text.Trim();
            if (commit && newText.Length > 0 && newText != task.Text)
            {
                ApplyToTask(reference, (_, t) =>
                {
                    var parsed = BacklogMarkdown.ParseTask(newText);
                    _state.Rename(t.Text, parsed.Text);
                    _state.Save();
                    t.Text = parsed.Text;
                    foreach (var kv in parsed.Attributes.Items) t.Attributes.Set(kv.Key, kv.Value);
                    return true;
                });
            }
            else
            {
                DispatcherQueue.TryEnqueue(Render);
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { Finish(true); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { Finish(false); e.Handled = true; }
        };
        box.LostFocus += (_, _) => Finish(true);
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
    }

    private static bool Set(Action change)
    {
        change();
        return true;
    }

    private static bool MoveTask(BacklogDocument d, TaskRef reference, Func<BacklogDocument, List<TaskItem>?> target)
    {
        if (reference.Resolve(d) is not { } t || reference.ResolveList(d) is not { } from || target(d) is not { } to) return false;
        from.Remove(t);
        to.Add(t);
        return true;
    }

    /// <summary>Moves a task one step in the order you see (priority, then file order).</summary>
    private static bool MoveVisual(BacklogDocument d, TaskRef reference, int delta)
    {
        if (reference.Resolve(d) is not { } t || reference.ResolveList(d) is not { } list) return false;
        var ordered = BacklogDocument.Ordered(list).Select(x => x.Task).ToList();
        int pos = ordered.IndexOf(t), j = pos + delta;
        if (j < 0 || j >= ordered.Count) return false;
        var other = ordered[j];
        if (t.Priority != other.Priority) (t.Priority, other.Priority) = (other.Priority, t.Priority);
        int a = list.IndexOf(t), b = list.IndexOf(other);
        list[a] = other;
        list[b] = t;
        return true;
    }

    // ---------- Small builders ----------

    private static Thickness Margin(double l, double t, double r, double b) => new(l, t, r, b);

    /// <summary>
    /// "FOCUS 6". On glass it sits straight on the background; over a picture it gets its own small
    /// frosted chip so it stays readable whatever the image does behind it.
    /// </summary>
    private FrameworkElement SectionHeader(string label, int count, Thickness margin, bool muted = false)
    {
        bool chip = !_palette.Theme.IsGlass;
        var ink = chip ? _palette.TextSecondary : _palette.HeaderSecondary;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, CharacterSpacing = 160, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = muted ? ink : _palette.AccentBrush, VerticalAlignment = VerticalAlignment.Center });
        if (!chip)
        {
            panel.Margin = margin;
            return panel;
        }
        return new Border
        {
            Child = panel,
            Background = _palette.Panel,
            BorderBrush = _palette.PanelStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 3, 10, 4),
            Margin = new Thickness(margin.Left - 6, margin.Top, margin.Right, margin.Bottom),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private Button Pill(string glyph, string text, bool on, string tooltip)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = on ? _palette.AccentBrush : _palette.TextSecondary });
        content.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = on ? _palette.Text : _palette.TextSecondary });
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 3, 11, 4),
            MinHeight = 26,
            CornerRadius = new CornerRadius(13),
            Background = on ? _palette.AccentSoft : _palette.Panel,
            BorderBrush = _palette.PanelStroke,
            BorderThickness = new Thickness(1),
            MinWidth = 0,
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private Button RowIcon(string glyph, string tooltip, Brush foreground)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = foreground },
            Background = _palette.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Top,
        };
        ApplyQuietHover(button);
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    /// <summary>
    /// Icon buttons sit on tinted glass, where the stock hover (an opaque gray plate with a border)
    /// looks broken. This swaps in a soft tint of the current ink for hover and press, with no border.
    /// </summary>
    private void ApplyQuietHover(Button button)
    {
        var ink = _palette.Text.Color;
        var hover = new SolidColorBrush(Palette.WithAlpha(ink, 0x14));
        var press = new SolidColorBrush(Palette.WithAlpha(ink, 0x24));
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = press;
        button.Resources["ButtonBorderBrushPointerOver"] = _palette.Transparent;
        button.Resources["ButtonBorderBrushPressed"] = _palette.Transparent;
        button.Resources["ButtonBackgroundDisabled"] = _palette.Transparent;
    }

    /// <summary>A round close button with a comfortable 32 px target.</summary>
    private Button CloseButton(string tooltip)
    {
        var button = RowIcon("", tooltip, _palette.TextSecondary);
        button.Width = 32;
        button.Height = 32;
        button.Padding = new Thickness(0);
        button.CornerRadius = new CornerRadius(16);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ((FontIcon)button.Content).FontSize = 12;
        return button;
    }

    /// <summary>The glass surface used for Focus and every area: translucent fill with a lit top edge.</summary>
    private Border Card(UIElement child, double padding = 4) => new()
    {
        Child = child,
        Background = _palette.Panel,
        BorderBrush = _palette.PanelStroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(padding),
        Margin = Margin(0, 0, 0, 10),
    };

    private TextBlock Hint(string text, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Foreground = _palette.TextTertiary,
        Margin = margin ?? Margin(12, 6, 12, 6),
    };

    private void HoverHighlight(Panel element, Brush? resting, Action<bool>? onHover = null)
    {
        Motion.SmoothChanges(element);
        element.PointerEntered += (_, _) =>
        {
            if (resting == null) element.Background = _palette.Hover;
            onHover?.Invoke(true);
        };
        element.PointerExited += (_, _) =>
        {
            element.Background = resting ?? _palette.Transparent;
            onHover?.Invoke(false);
        };
    }

    private static MenuFlyoutItem MenuItem(string text, string? glyph, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (glyph != null) item.Icon = new FontIcon { Glyph = glyph };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuFlyoutItem MenuItem(string text, string? glyph, Func<Task> onClick) =>
        MenuItem(text, glyph, () => { _ = onClick(); });

    private async Task<string?> PromptAsync(string title, string initial, string primary, string? placeholder = null)
    {
        var box = new TextBox { Text = initial, MinWidth = 300, PlaceholderText = placeholder ?? "", TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
        };
        bool accepted = false;
        box.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            accepted = true;
            dialog.Hide();
        };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        var result = await dialog.ShowAsync();
        var text = box.Text.Trim().Replace("\r", " ").Replace("\n", " ");
        return (accepted || result == ContentDialogResult.Primary) && text.Length > 0 ? text : null;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
