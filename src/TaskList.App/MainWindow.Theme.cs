using Microsoft.UI.Text;
using TaskList.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace TaskList.App;

/// <summary>Applying a theme to the window, and the theme picker.</summary>
public sealed partial class MainWindow
{
    private readonly GlassBackdrop _backdrop = new();
    private Palette _palette = null!;

    private static string ThemeImagePath(string file) => Path.Combine(AppContext.BaseDirectory, "Assets", "Themes", file);

    private void ApplyTheme(bool render = true, bool animate = false, bool rebuildFooter = true)
    {
        var preview = Environment.GetEnvironmentVariable("TASKLIST_PREVIEW_THEME");
        var theme = Palette.Find(preview ?? _prefs.Theme);
        _palette = new Palette(theme);
        bool dark = theme.DarkCards;
        Root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        Shell.RequestedTheme = Root.RequestedTheme;

        _backdrop.Update(dark, _prefs.Frost);
        if (theme.Banner is { } banner)
        {
            BackgroundImage.Source = new BitmapImage(new Uri(ThemeImagePath(banner)));
            // Below the picture the base color is see-through too, so the desktop still shines through.
            var veil = Palette.WithAlpha(theme.Base, (byte)(0x78 + _prefs.Frost * 0x87));
            // The picture itself fades to transparent at the bottom, so it flows into the window.
            BannerFade.Fill = null;
            BannerHost.Visibility = Visibility.Visible;
            Root.Background = new SolidColorBrush(veil);
            NewsPanel.Background = new SolidColorBrush(veil);
            GlowLayer.Visibility = Visibility.Collapsed;
            PreviewBackground.Visibility = Visibility.Collapsed;
        }
        else
        {
            BannerHost.Visibility = Visibility.Collapsed;
            // A light (or dark) veil of the theme's own color: it's what keeps text readable while the
            // window as a whole lets the desktop through. The Glass slider sets how dense it is.
            var paper = dark ? Palette.Hex("#15161D") : Palette.Hex("#F6F7FA");
            var tint = Palette.WithAlpha(paper, (byte)(0x78 + _prefs.Frost * 0x87));
            var veilBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            veilBrush.GradientStops.Add(new GradientStop { Color = tint, Offset = 0 });
            veilBrush.GradientStops.Add(new GradientStop { Color = tint, Offset = 1 });
            Root.Background = veilBrush;
            NewsPanel.Background = new SolidColorBrush(tint);
            GlowLayer.Visibility = Visibility.Visible;
            GlowA.Fill = Glow(theme.GlowA, dark ? (byte)0x70 : (byte)0x90);
            GlowB.Fill = Glow(theme.GlowB, dark ? (byte)0x58 : (byte)0x78);
            ShowPreviewBackground(dark);
        }

        TitleText.Foreground = _palette.HeaderText;
        DateRun.Foreground = _palette.HeaderSecondary;
        MomentumRun.Foreground = _palette.HeaderText;
        CountsRun.Foreground = _palette.HeaderSecondary;
        PercentText.Foreground = _palette.HeaderText;
        _ring.SetColors(_palette.AiGradient, _palette.HeaderTrack);

        Footer.Background = _palette.Panel;
        Footer.BorderBrush = _palette.PanelStroke;
        FileIcon.Foreground = _palette.TextSecondary;
        FileText.Foreground = _palette.TextSecondary;

        NewsPill.Background = _palette.Panel;
        NewsPill.BorderBrush = _palette.PanelStroke;
        NewsBadge.Background = _palette.AiGradient;
        ((TextBlock)NewsBadge.Child).Foreground = _palette.OnAccent;
        NewsText.Foreground = _palette.Text;
        NewsDots.Foreground = _palette.AccentBrush;

        var caption = _palette.HeaderText.Color;
        AppWindow.TitleBar.ButtonForegroundColor = caption;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Palette.WithAlpha(caption, 0x99);
        AppWindow.TitleBar.ButtonHoverForegroundColor = caption;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Palette.WithAlpha(caption, 0x1A);
        AppWindow.TitleBar.ButtonPressedBackgroundColor = Palette.WithAlpha(caption, 0x2A);

        if (render)
        {
            // While the theme picker is open its button must survive, so the footer is rebuilt when it closes.
            if (rebuildFooter) BuildFooter();
            Render();
            if (_newsPanelOpen) BuildNewsPanel();
            RenderBanners(animateNewest: null);
        }
        if (animate)
        {
            Motion.FadeIn(theme.Banner != null ? BannerHost : GlowLayer, 500);
            Motion.FadeIn(ContentPanel, 280);
        }
    }

    /// <summary>Design-review aid: screenshots can't see acrylic, so paint a stand-in desktop.</summary>
    private void ShowPreviewBackground(bool dark)
    {
        if (Environment.GetEnvironmentVariable("TASKLIST_PREVIEW_BG") != "1") return;
        var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        bg.GradientStops.Add(new GradientStop { Color = Palette.Hex(dark ? "#26303B" : "#E4EEF2"), Offset = 0 });
        bg.GradientStops.Add(new GradientStop { Color = Palette.Hex(dark ? "#10151C" : "#B9D3DC"), Offset = 1 });
        PreviewBackground.Fill = bg;
        PreviewBackground.Visibility = Visibility.Visible;
    }

    private static RadialGradientBrush Glow(Color color, byte alpha)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(color, alpha), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(color, (byte)(alpha / 3)), Offset = 0.55 });
        brush.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(color, 0), Offset = 1 });
        return brush;
    }

    // ---------- Picker ----------

    private Flyout BuildThemeFlyout()
    {
        var panel = new StackPanel { Spacing = 12, Width = 300 };
        panel.Children.Add(new TextBlock { Text = "Theme", FontSize = 15, FontWeight = FontWeights.SemiBold });

        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var themes = Palette.Themes;
        for (int i = 0; i < (themes.Length + 1) / 2; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Slider? frost = null;
        for (int i = 0; i < themes.Length; i++)
        {
            var theme = themes[i];
            var tile = ThemeTile(theme, theme.Name == _palette.Theme.Name);
            tile.Click += (_, _) =>
            {
                _prefs.Theme = theme.Name;
                _prefs.Save();
                foreach (var b in grid.Children.OfType<Button>()) b.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                tile.BorderBrush = new SolidColorBrush(theme.Accent);
                ApplyTheme(animate: true, rebuildFooter: false);
            };
            Grid.SetRow(tile, i / 2);
            Grid.SetColumn(tile, i % 2);
            grid.Children.Add(tile);
        }
        panel.Children.Add(grid);

        panel.Children.Add(new TextBlock { Text = "Glass", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, -8) });
        frost = new Slider { Minimum = 0, Maximum = 100, Value = _prefs.Frost * 100, StepFrequency = 1 };
        ToolTipService.SetToolTip(frost, "Left: more of the blurred desktop, right: more frosted");
        frost.ValueChanged += (_, e) =>
        {
            _prefs.Frost = e.NewValue / 100;
            if (_palette.Theme.IsGlass) _backdrop.Update(_palette.Dark, _prefs.Frost);
            ApplyTheme(render: false);
            _prefs.Save();
        };
        panel.Children.Add(frost);

        var news = new ToggleSwitch { Header = "AI news ticker", IsOn = _prefs.ShowNews };
        news.Toggled += (_, _) =>
        {
            _prefs.ShowNews = news.IsOn;
            _prefs.Save();
            InitNews();
        };
        panel.Children.Add(news);

        var outcomes = new ToggleSwitch { Header = "Show area outcomes", IsOn = _prefs.ShowOutcomes };
        outcomes.Toggled += (_, _) =>
        {
            _prefs.ShowOutcomes = outcomes.IsOn;
            _prefs.Save();
            Render();
        };
        panel.Children.Add(outcomes);

        var edge = new ToggleSwitch { Header = "Show app when the mouse hits the right screen edge", IsOn = _prefs.RevealAtRightEdge };
        edge.Toggled += (_, _) => SetEdgeReveal(edge.IsOn);
        panel.Children.Add(edge);

        // Which AI agent does research and fetches the news.
        var engines = new (AiEngine Engine, string Label)[]
        {
            (AiEngine.Claude, "Claude Code (claude)"),
            (AiEngine.Copilot, "GitHub Copilot CLI (copilot)"),
            (AiEngine.Custom, "Custom command…"),
        };
        var engineBox = new ComboBox { Header = "AI engine", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (_, label) in engines) engineBox.Items.Add(label);
        engineBox.SelectedIndex = Math.Max(0, Array.FindIndex(engines, e => e.Engine == AiCli.Engine));
        var custom = new TextBox
        {
            PlaceholderText = "e.g. mytool ask --file {prompt_file}",
            Text = _prefs.AiCustomCommand,
            Visibility = AiCli.Engine == AiEngine.Custom ? Visibility.Visible : Visibility.Collapsed,
        };
        ToolTipService.SetToolTip(custom, "{prompt_file} is replaced by a file holding the prompt, {prompt} by the prompt text; with neither, the prompt is sent on standard input.");
        var found = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        void UpdateFound()
        {
            string? exe = AiCli.Engine switch
            {
                AiEngine.Claude => "claude",
                AiEngine.Copilot => "copilot",
                _ => AiCli.SplitCommandLine(_prefs.AiCustomCommand).FirstOrDefault(),
            };
            found.Text = exe == null ? "" : AiCli.Resolve(exe) is { } path ? $"Found: {path}" : $"'{exe}' isn't on PATH on this PC.";
        }
        engineBox.SelectionChanged += (_, _) =>
        {
            var engine = engines[Math.Max(0, engineBox.SelectedIndex)].Engine;
            _prefs.AiEngine = engine.ToString();
            _prefs.Save();
            ApplyAiEngine();
            custom.Visibility = engine == AiEngine.Custom ? Visibility.Visible : Visibility.Collapsed;
            UpdateFound();
            Render();
        };
        custom.TextChanged += (_, _) =>
        {
            _prefs.AiCustomCommand = custom.Text.Trim();
            _prefs.Save();
            ApplyAiEngine();
            UpdateFound();
        };
        UpdateFound();
        panel.Children.Add(engineBox);
        panel.Children.Add(custom);
        panel.Children.Add(found);

        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.TopEdgeAlignedRight };
        flyout.Closed += (_, _) => BuildFooter();
        return flyout;
    }

    /// <summary>A small preview of the theme: its background with a mock card and an accent check.</summary>
    private static Button ThemeTile(ThemeDef theme, bool selected)
    {
        var surface = new Grid { Height = 78, CornerRadius = new CornerRadius(8) };
        if (theme.Banner is { } image)
        {
            surface.Background = new SolidColorBrush(theme.Base);
            surface.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(ThemeImagePath(image))) { DecodePixelWidth = 280 },
                Stretch = Stretch.UniformToFill,
                VerticalAlignment = VerticalAlignment.Top,
                Height = 52,
            });
            var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fade.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(theme.Base, 0), Offset = 0.3 });
            fade.GradientStops.Add(new GradientStop { Color = theme.Base, Offset = 0.68 });
            surface.Children.Add(new Rectangle { Fill = fade });
        }
        else
        {
            var basis = theme.DarkCards ? Palette.Hex("#171923") : Palette.Hex("#FAFAFC");
            var bg = new LinearGradientBrush { StartPoint = new Point(1, 0), EndPoint = new Point(0, 1) };
            bg.GradientStops.Add(new GradientStop { Color = theme.GlowA, Offset = 0 });
            bg.GradientStops.Add(new GradientStop { Color = basis, Offset = 0.62 });
            bg.GradientStops.Add(new GradientStop { Color = Palette.WithAlpha(theme.GlowB, 0xCC), Offset = 1 });
            surface.Background = bg;
        }

        // Mock card: a circle, a bar of text, and the accent.
        var card = new Grid
        {
            Margin = new Thickness(10, 26, 10, 10),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(theme.DarkCards ? Color.FromArgb(0xC0, 16, 18, 26) : Color.FromArgb(0xE0, 255, 255, 255)),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var lines = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(theme.Accent) });
        lines.Children.Add(new Rectangle { Width = 46, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = new SolidColorBrush(theme.DarkCards ? Color.FromArgb(0xB0, 255, 255, 255) : Color.FromArgb(0x90, 20, 21, 26)) });
        var ai = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        ai.GradientStops.Add(new GradientStop { Color = theme.Accent, Offset = 0 });
        ai.GradientStops.Add(new GradientStop { Color = theme.Accent2, Offset = 1 });
        lines.Children.Add(new Rectangle { Width = 16, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = ai });
        card.Children.Add(lines);
        surface.Children.Add(card);

        surface.Children.Add(new TextBlock
        {
            Text = theme.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 6, 0, 0),
            Foreground = new SolidColorBrush(theme.HeaderInk),
        });

        var button = new Button
        {
            Content = surface,
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(selected ? theme.Accent : Color.FromArgb(0, 0, 0, 0)),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        ToolTipService.SetToolTip(button, theme.Name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{theme.Name} theme");
        return button;
    }
}
