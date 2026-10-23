using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace TaskList.App;

/// <summary>
/// A complete look. Glass themes let the desktop show through tinted acrylic with two soft glows.
/// Banner themes show a scene across the top (Assets/Themes) that fades into a matching base color.
/// Every theme pairs its accent with a second hue; together they form the iridescent "AI" gradient
/// used on the ring, the NEXT marker and Claude-powered features.
/// </summary>
/// <param name="DarkCards">Cards and their text use the dark scheme.</param>
/// <param name="HeaderInk">Text drawn straight on the background: title, date, summary.</param>
/// <param name="Base">Banner themes: the color the picture fades into.</param>
public sealed record ThemeDef(
    string Name,
    bool DarkCards,
    Color Accent,
    Color Accent2,
    Color GlowA,
    Color GlowB,
    Color HeaderInk,
    string? Banner = null,
    Color Base = default)
{
    public bool IsGlass => Banner == null;
}

/// <summary>Design tokens for one render, derived from the theme.</summary>
public sealed class Palette
{
    private static readonly Color Ink = Hex("#15161C");
    private static readonly Color Paper = Hex("#F6F6F9");

    // Accents are kept mid-saturation and glows pale, so color reads as a tint rather than a fill.
    public static readonly ThemeDef[] Themes =
    [
        new("Pearl", false, Hex("#5B50D6"), Hex("#2E8FBF"), Hex("#CFCBF5"), Hex("#C4E6EF"), Ink),
        new("Frost", false, Hex("#2563C9"), Hex("#2B8C88"), Hex("#C7D8F2"), Hex("#C6E7E2"), Ink),
        new("Blush", false, Hex("#B8436A"), Hex("#7A5BC4"), Hex("#EFCFD9"), Hex("#DCD2F2"), Ink),
        new("Mint", false, Hex("#0E8F70"), Hex("#2F7BD8"), Hex("#BDEFDC"), Hex("#C3DAFA"), Ink),
        new("Nebula", true, Hex("#B39DF2"), Hex("#5CC6DE"), Hex("#4A2F8F"), Hex("#155E75"), Paper),
        new("Prism", false, Hex("#5B4FE0"), Hex("#1A9BD7"), Hex("#C9BEFF"), Hex("#A6F0DA"), Hex("#1A1A3A"), "prism_banner.png", Hex("#F3F5FD")),
        new("Flow", false, Hex("#4F5BE8"), Hex("#16A89D"), Hex("#6C7CFF"), Hex("#FF8E9E"), Hex("#1B1D3A"), "flow_banner.png", Hex("#F6F4FD")),
        new("Momentum", false, Hex("#E2512F"), Hex("#D23A7A"), Hex("#FFB547"), Hex("#FF7A59"), Hex("#3D1F10"), "momentum_banner.png", Hex("#FFF4EC")),
        new("Sakura", false, Hex("#D02670"), Hex("#8A55D6"), Hex("#FF9EC2"), Hex("#FFD3E2"), Hex("#4A1430"), "sakura_banner.png", Hex("#FCF1F5")),
        new("Dunes", false, Hex("#C8501A"), Hex("#2F86C8"), Hex("#FF9C6B"), Hex("#7FC4F5"), Hex("#3A2410"), "dunes_banner.png", Hex("#FBF3EA")),
    ];

    public static ThemeDef Find(string name) =>
        Themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    public ThemeDef Theme { get; }
    public bool Dark => Theme.DarkCards;

    public SolidColorBrush Text { get; }
    public SolidColorBrush TextSecondary { get; }
    public SolidColorBrush TextTertiary { get; }
    public SolidColorBrush HeaderText { get; }
    public SolidColorBrush HeaderSecondary { get; }
    public SolidColorBrush Track { get; }
    public SolidColorBrush HeaderTrack { get; }
    public SolidColorBrush Panel { get; }
    public Brush PanelStroke { get; }
    public SolidColorBrush Hover { get; }
    public SolidColorBrush AccentBrush { get; }
    public SolidColorBrush AccentSoft { get; }
    public Brush NextBand { get; }
    public Brush TodayBand { get; }
    public SolidColorBrush Today { get; }

    /// <summary>Accent to its partner hue: the ring, the NEXT spine, sparkles and the AI badge.</summary>
    public LinearGradientBrush AiGradient { get; }
    public LinearGradientBrush AiGradientVertical { get; }
    public SolidColorBrush Base { get; }
    public Brush BannerFade { get; }
    public SolidColorBrush Transparent { get; } = new(Color.FromArgb(0, 0, 0, 0));
    public SolidColorBrush OnAccent { get; }

    public Palette(ThemeDef theme)
    {
        Theme = theme;
        bool dark = theme.DarkCards;
        var ink = dark ? Paper : Ink;
        Text = new(ink);
        TextSecondary = new(WithAlpha(ink, 0xA8));
        TextTertiary = new(WithAlpha(ink, 0x70));
        Track = new(WithAlpha(ink, 0x1A));

        HeaderText = new(theme.HeaderInk);
        HeaderSecondary = new(WithAlpha(theme.HeaderInk, 0xB8));
        HeaderTrack = new(WithAlpha(theme.HeaderInk, 0x24));

        var white = Color.FromArgb(255, 255, 255, 255);
        var night = Hex("#10121A");
        if (theme.IsGlass)
        {
            Panel = new(dark ? WithAlpha(white, 0x12) : WithAlpha(white, 0xA6));
            PanelStroke = Vertical(WithAlpha(white, (byte)(dark ? 0x36 : 0xF5)), WithAlpha(white, (byte)(dark ? 0x0A : 0x60)));
        }
        else
        {
            // Below the banner the cards sit on a calm base, so they can be lighter and still read crisply.
            Panel = new(dark ? WithAlpha(white, 0x0E) : WithAlpha(white, 0xCC));
            PanelStroke = Vertical(WithAlpha(white, (byte)(dark ? 0x2E : 0xFF)), WithAlpha(white, (byte)(dark ? 0x08 : 0x90)));
        }
        Hover = new(dark ? WithAlpha(white, 0x12) : WithAlpha(Ink, 0x08));

        AccentBrush = new(theme.Accent);
        OnAccent = new(Luminance(theme.Accent) > 0.6 ? Ink : white);
        AccentSoft = new(WithAlpha(theme.Accent, (byte)(dark ? 0x2E : 0x1E)));
        NextBand = Horizontal(WithAlpha(theme.Accent, (byte)(dark ? 0x2A : 0x1C)), WithAlpha(theme.Accent2, (byte)(dark ? 0x0A : 0x06)));
        TodayBand = Horizontal(WithAlpha(theme.Accent, (byte)(dark ? 0x14 : 0x0D)), WithAlpha(theme.Accent2, (byte)(dark ? 0x05 : 0x03)));
        Today = new(Hex(dark ? "#FFC23D" : "#D08A00"));

        AiGradient = Gradient(theme.Accent, theme.Accent2, new Point(0, 0), new Point(1, 1));
        AiGradientVertical = Vertical(theme.Accent, theme.Accent2);

        Base = new(theme.IsGlass ? Color.FromArgb(0, 0, 0, 0) : theme.Base);
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop { Color = WithAlpha(theme.Base, 0), Offset = 0.35 });
        fade.GradientStops.Add(new GradientStop { Color = WithAlpha(theme.Base, 0xB0), Offset = 0.75 });
        fade.GradientStops.Add(new GradientStop { Color = theme.Base, Offset = 1 });
        BannerFade = fade;
    }

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static LinearGradientBrush Vertical(Color top, Color bottom) => Gradient(top, bottom, new Point(0, 0), new Point(0, 1));

    private static LinearGradientBrush Horizontal(Color left, Color right) => Gradient(left, right, new Point(0, 0), new Point(1, 0));

    private static LinearGradientBrush Gradient(Color a, Color b, Point start, Point end)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        brush.GradientStops.Add(new GradientStop { Color = a, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = b, Offset = 1 });
        return brush;
    }

    public static Color Hex(string hex) => Color.FromArgb(255,
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
}
