using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaskList.Core;
using Windows.Graphics;

namespace TaskList.App;

/// <summary>
/// Clicking the news ticker widens the window to the left and shows the last two weeks of AI headlines,
/// grouped by day. The task list keeps its size and place; only the new column is added.
/// </summary>
public sealed partial class MainWindow
{
    private const double NewsPanelWidth = 340;
    private bool _newsPanelOpen;
    private bool _newsPanelAnimating;

    private void ToggleNewsPanel()
    {
        if (_newsPanelAnimating) return;
        if (_newsPanelOpen) CloseNewsPanel();
        else OpenNewsPanel();
    }

    private void OpenNewsPanel()
    {
        BuildNewsPanel();
        NewsPanel.Visibility = Visibility.Visible;
        _newsPanelOpen = true;
        AnimateWindowWidth(+1);
    }

    private void CloseNewsPanel()
    {
        _newsPanelOpen = false;
        AnimateWindowWidth(-1);
    }

    /// <summary>Grows (or shrinks) the window on its left edge while the news column opens with it.</summary>
    private void AnimateWindowWidth(int direction)
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1;
        int delta = (int)(NewsPanelWidth * scale);
        var start = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);

        // Don't run off the left edge of the screen: if there's no room, grow less.
        if (direction > 0)
        {
            var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
            delta = Math.Min(delta, Math.Max(0, start.X - work.X));
            if (delta < 120 * scale)
            {
                // Not enough room on the left: open over the list instead of widening.
                delta = 0;
            }
        }
        else
        {
            delta = (int)(NewsColumn.Width.Value * scale);
        }

        if (delta == 0 && direction > 0)
        {
            // Fallback: the panel overlays the left part of the list.
            NewsColumn.Width = new GridLength(0);
            Grid.SetColumnSpan(NewsPanel, 2);
            NewsPanel.HorizontalAlignment = HorizontalAlignment.Left;
            NewsPanel.Width = Math.Min(NewsPanelWidth, Root.ActualWidth - 40);
            Motion.EnterFromBelow(NewsPanel, 0, 0, 220);
            return;
        }

        bool animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        var began = DateTime.Now;
        _newsPanelAnimating = true;
        _movingWindow = true;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(12);
        void Step()
        {
            double t = animate ? Math.Min(1, (DateTime.Now - began).TotalMilliseconds / 260) : 1;
            double e = 1 - Math.Pow(1 - t, 3);
            int shift = (int)(delta * e) * direction;
            AppWindow.MoveAndResize(new RectInt32(start.X - shift, start.Y, start.Width + shift, start.Height));
            double columnPx = direction > 0 ? delta * e : delta * (1 - e);
            NewsColumn.Width = new GridLength(Math.Max(0, columnPx / scale));
            if (t >= 1)
            {
                timer.Stop();
                _newsPanelAnimating = false;
                _movingWindow = false;
                if (direction < 0)
                {
                    NewsPanel.Visibility = Visibility.Collapsed;
                    NewsPanel.Children.Clear();
                }
            }
        }
        timer.Tick += (_, _) => Step();
        Step();
        timer.Start();
    }


    private void BuildNewsPanel()
    {
        Grid.SetColumnSpan(NewsPanel, 1);
        NewsPanel.Width = double.NaN;
        NewsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        NewsPanel.Children.Clear();
        NewsPanel.RowDefinitions.Clear();
        NewsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        NewsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        NewsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var surface = new Border
        {
            Background = _palette.Panel,
            BorderBrush = _palette.PanelStroke,
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        Grid.SetRowSpan(surface, 3);
        NewsPanel.Children.Add(surface);

        NewsPanel.Children.Add(BuildNewsHeader());

        var history = NewsService.LoadHistory();

        var list = new StackPanel { Padding = new Thickness(12, 0, 12, 16) };
        var shown = history;
        if (shown.Count == 0)
        {
            list.Children.Add(Hint("No headlines yet. They appear here as the ticker fetches them.", new Thickness(8, 8, 8, 8)));
        }

        foreach (var day in shown.GroupBy(h => h.SeenAt.Date).OrderByDescending(g => g.Key))
        {
            list.Children.Add(new TextBlock
            {
                Text = DayLabel(day.Key).ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                CharacterSpacing = 140,
                Foreground = _palette.TextSecondary,
                Margin = new Thickness(4, 16, 0, 6),
            });

            bool first = true;
            foreach (var item in day)
            {
                list.Children.Add(first ? NewsHero(item) : NewsRow(item));
                first = false;
            }
        }
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        NewsPanel.Children.Add(scroll);
    }

    /// <summary>Header over the theme's own picture (or its glow), so the panel belongs to the window.</summary>
    private Grid BuildNewsHeader()
    {
        var header = new Grid { Height = 118 };
        var theme = _palette.Theme;
        if (theme.Banner is { } banner)
        {
            header.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(ThemeImagePath(banner))) { DecodePixelWidth = 700 },
                Stretch = Stretch.UniformToFill,
                VerticalAlignment = VerticalAlignment.Top,
            });
        }
        else
        {
            var glow = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
            glow.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(theme.GlowA, 0xC0), Offset = 0 });
            glow.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(theme.GlowB, 0x90), Offset = 1 });
            header.Background = glow;
        }
        // Soft fade into the list below.
        var fade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        var paper = _palette.Dark ? Palette.Hex("#141620") : Palette.Hex("#FFFFFF");
        fade.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(paper, 0x00), Offset = 0.25 });
        fade.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(paper, 0xC8), Offset = 1 });
        header.Children.Add(new Border { Background = fade });

        var bar = new Grid { Padding = new Thickness(18, 0, 8, 12), ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Bottom };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = _palette.AiGradient,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "AI", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = _palette.OnAccent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "AI news", FontSize = 18, FontWeight = FontWeights.SemiBold, FontFamily = DisplayFont, Foreground = _palette.Text });
        titles.Children.Add(new TextBlock { Text = "Headlines from the last two weeks", FontSize = 11, Foreground = _palette.TextSecondary });
        Grid.SetColumn(titles, 1);
        bar.Children.Add(titles);
        var refresh = RowIcon("", "Refresh headlines", _palette.TextSecondary);
        refresh.VerticalAlignment = VerticalAlignment.Center;
        refresh.Click += (_, _) => RefreshNews(force: true);
        Grid.SetColumn(refresh, 2);
        bar.Children.Add(refresh);
        var close = CloseButton("Close news");
        close.Click += (_, _) => ToggleNewsPanel();
        Grid.SetColumn(close, 3);
        bar.Children.Add(close);
        header.Children.Add(bar);
        return header;
    }

    private static readonly Dictionary<string, string> TopicColors = new()
    {
        ["Models"] = "#6D5BD0", ["Funding"] = "#1F9D6B", ["Policy"] = "#B7791F", ["Security"] = "#D14343",
        ["Chips"] = "#2B7BD6", ["Research"] = "#0E9AA7", ["Products"] = "#C2457E", ["Industry"] = "#6B7280",
    };

    private static Windows.UI.Color TopicColor(string topic) => Palette.Hex(TopicColors.GetValueOrDefault(topic, "#6B7280"));

    /// <summary>The day's top story: a large picture card.</summary>
    private Grid NewsHero(NewsService.HistoryItem item)
    {
        var card = new Grid { CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 6), Background = _palette.Transparent };
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var picture = new Grid { Height = 150, CornerRadius = new CornerRadius(12) };
        picture.Children.Add(ImagePlaceholder(item.Headline, large: true));
        var image = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        Motion.SmoothOpacity(image);
        picture.Children.Add(image);
        LoadNewsImage(item.Url, image, 640);
        card.Children.Add(picture);

        var texts = new StackPanel { Spacing = 3, Padding = new Thickness(4, 8, 4, 6) };
        texts.Children.Add(TopicTag(item.Headline));
        texts.Children.Add(new TextBlock { Text = item.Headline, FontSize = 15, FontWeight = FontWeights.SemiBold, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Foreground = _palette.Text });
        if (Source(item) is { } source) texts.Children.Add(new TextBlock { Text = source, FontSize = 11, Foreground = _palette.TextTertiary });
        Grid.SetRow(texts, 1);
        card.Children.Add(texts);
        WireOpen(card, item);
        return card;
    }

    /// <summary>Other stories: text with a small square thumbnail.</summary>
    private Grid NewsRow(NewsService.HistoryItem item)
    {
        var row = new Grid { Padding = new Thickness(4, 8, 4, 8), CornerRadius = new CornerRadius(10), Background = _palette.Transparent, ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(TopicTag(item.Headline));
        texts.Children.Add(new TextBlock { Text = item.Headline, FontSize = 13, LineHeight = 18, TextWrapping = TextWrapping.Wrap, Foreground = _palette.Text });
        if (Source(item) is { } source) texts.Children.Add(new TextBlock { Text = source, FontSize = 11, Foreground = _palette.TextTertiary });
        row.Children.Add(texts);

        var thumb = new Grid { Width = 58, Height = 58, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top };
        thumb.Children.Add(ImagePlaceholder(item.Headline, large: false));
        var image = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        Motion.SmoothOpacity(image);
        thumb.Children.Add(image);
        LoadNewsImage(item.Url, image, 160);
        Grid.SetColumn(thumb, 1);
        row.Children.Add(thumb);
        WireOpen(row, item);
        return row;
    }

    private void WireOpen(Panel element, NewsService.HistoryItem item)
    {
        HoverHighlight(element, null);
        if (!NewsService.IsWebUrl(item.Url)) return;
        ToolTipService.SetToolTip(element, item.Url);
        element.Tapped += (_, _) => Process.Start(new ProcessStartInfo(item.Url!) { UseShellExecute = true });
    }

    private static string? Source(NewsService.HistoryItem item) =>
        item.Url != null && Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", "") : null;

    private StackPanel TopicTag(string headline)
    {
        var topic = NewsTopics.Classify(headline);
        var color = TopicColor(topic);
        var tag = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        tag.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
        tag.Children.Add(new TextBlock { Text = topic, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color) });
        return tag;
    }

    /// <summary>Shown until (or instead of) the article's picture: the topic color with a soft sparkle.</summary>
    private Border ImagePlaceholder(string headline, bool large)
    {
        var color = TopicColor(NewsTopics.Classify(headline));
        var fill = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        fill.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(color, 0x55), Offset = 0 });
        fill.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(_palette.Theme.Accent2, 0x30), Offset = 1 });
        return new Border
        {
            Background = fill,
            Child = new Viewbox { Width = large ? 34 : 18, Height = large ? 34 : 18, Child = Sparkle(new SolidColorBrush(Palette.WithAlpha(Palette.Hex("#FFFFFF"), 0xCC)), 16) },
        };
    }

    private async void LoadNewsImage(string? url, Image target, int decodeWidth)
    {
        if (url == null) return;
        var path = NewsImages.Cached(url) ?? await Task.Run(() => NewsImages.GetAsync(url));
        if (path == null) return;
        try
        {
            target.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = decodeWidth };
            target.Opacity = 1;
        }
        catch (Exception)
        {
            // An unreadable image just leaves the placeholder.
        }
    }

    private static string DayLabel(DateTime date)
    {
        var today = DateTime.Today;
        if (date == today) return "Today";
        if (date == today.AddDays(-1)) return "Yesterday";
        return date.ToString("dddd, MMM d");
    }

}
