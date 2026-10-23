using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using FontWeight = Windows.UI.Text.FontWeight;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace TaskList.App;

/// <summary>
/// Renders research results (Markdown) as readable, document-style blocks: headings with spacing,
/// paragraphs, bullet and numbered lists (nested), tables, code blocks, quotes and rules, with bold,
/// italics, inline code and links inside text. A line that is only bold text reads as a heading,
/// since agents often write sections that way.
/// </summary>
public static partial class MarkdownView
{
    [GeneratedRegex(@"(\*\*[^*]+\*\*|\*[^*\s][^*]*\*|`[^`]+`|\[[^\]]+\]\([^)\s]+\)|https?://[^\s)]+)")]
    private static partial Regex InlineToken();

    [GeneratedRegex(@"^(\d+)[.)]\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$")]
    private static partial Regex TableDivider();

    [GeneratedRegex(@"^\*\*([^*]+)\*\*:?$")]
    private static partial Regex BoldOnlyLine();

    private const double BodySize = 14;
    private const double BodyLine = 22;

    public static FrameworkElement Render(string markdown, Palette palette)
    {
        var root = new StackPanel { Spacing = 0 };
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            root.Children.Add(TextBlockFor(string.Join(" ", paragraph), palette, BodySize, FontWeights.Normal, new Thickness(0, 4, 0, 8)));
            paragraph.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            // Fenced code block.
            if (trimmed.StartsWith("```"))
            {
                FlushParagraph();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Add(lines[i]);
                root.Children.Add(CodeBlock(string.Join("\n", code), palette));
                continue;
            }

            // Table: a header row followed by a |---|---| divider.
            if (trimmed.StartsWith('|') && i + 1 < lines.Length && TableDivider().IsMatch(lines[i + 1]))
            {
                FlushParagraph();
                var rows = new List<string[]> { Cells(trimmed) };
                for (i += 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++) rows.Add(Cells(lines[i].Trim()));
                i--;
                root.Children.Add(Table(rows, palette));
                continue;
            }

            // Headings (# …) and bold-only lines used as headings.
            if (trimmed.StartsWith('#') || BoldOnlyLine().IsMatch(trimmed))
            {
                FlushParagraph();
                string text;
                double size;
                if (trimmed.StartsWith('#'))
                {
                    int level = trimmed.TakeWhile(c => c == '#').Count();
                    text = trimmed[level..].Trim();
                    size = level <= 1 ? 19 : level == 2 ? 17 : 15;
                }
                else
                {
                    text = BoldOnlyLine().Match(trimmed).Groups[1].Value;
                    size = 16;
                }
                var heading = TextBlockFor(text, palette, size, FontWeights.SemiBold, new Thickness(0, root.Children.Count == 0 ? 0 : 14, 0, 6));
                heading.FontFamily = new FontFamily("Segoe UI Variable Display");
                root.Children.Add(heading);
                continue;
            }

            // Horizontal rule.
            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                root.Children.Add(new Border { Height = 1, Background = palette.Track, Margin = new Thickness(0, 10, 0, 10) });
                continue;
            }

            // Quote.
            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var quote = new List<string> { trimmed.TrimStart('>').Trim() };
                while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('>')) quote.Add(lines[++i].TrimStart().TrimStart('>').Trim());
                var text = TextBlockFor(string.Join(" ", quote), palette, BodySize, FontWeights.Normal, new Thickness(0));
                text.Foreground = palette.TextSecondary;
                root.Children.Add(new Border
                {
                    BorderBrush = palette.AccentBrush,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 2, 0, 2),
                    Margin = new Thickness(0, 4, 0, 10),
                    Child = text,
                });
                continue;
            }

            // List items (bullets and numbers), nested by indent.
            string? marker = null;
            string content = trimmed;
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ ") || trimmed.StartsWith("• "))
            {
                marker = "•";
                content = trimmed[2..];
            }
            else if (Numbered().Match(trimmed) is { Success: true } m)
            {
                marker = m.Groups[1].Value + ".";
                content = m.Groups[2].Value;
            }
            if (marker != null)
            {
                FlushParagraph();
                int indent = Math.Min(3, (line.Length - trimmed.Length) / 2);
                // Continuation lines of the same item are joined to it.
                while (i + 1 < lines.Length && lines[i + 1].StartsWith("   ") && !IsListItem(lines[i + 1].TrimStart())) content += " " + lines[++i].Trim();
                root.Children.Add(ListItem(marker, content, indent, palette));
                continue;
            }

            paragraph.Add(trimmed);
        }
        FlushParagraph();
        return root;
    }

    private static bool IsListItem(string trimmed) =>
        trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ ") || Numbered().IsMatch(trimmed);

    private static string[] Cells(string row) =>
        row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    private static RichTextBlock TextBlockFor(string text, Palette palette, double size, FontWeight weight, Thickness margin)
    {
        var block = new RichTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = size,
            LineHeight = size <= BodySize ? BodyLine : size + 6,
            Foreground = palette.Text,
            Margin = margin,
        };
        var paragraph = new Paragraph();
        AddInlines(paragraph.Inlines, text, palette);
        if (weight != FontWeights.Normal) foreach (var inline in paragraph.Inlines) inline.FontWeight = weight;
        block.Blocks.Add(paragraph);
        return block;
    }

    private static Grid ListItem(string marker, string content, int indent, Palette palette)
    {
        var grid = new Grid { Margin = new Thickness(4 + indent * 18, 2, 0, 4), ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(marker == "•" ? 12 : 22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = marker,
            FontSize = BodySize,
            LineHeight = BodyLine,
            Foreground = palette.AccentBrush,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        var text = TextBlockFor(content, palette, BodySize, FontWeights.Normal, new Thickness(0));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static Border CodeBlock(string code, Palette palette) => new()
    {
        Background = new SolidColorBrush(Palette.WithAlpha(palette.Text.Color, 0x0C)),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 10, 12, 10),
        Margin = new Thickness(0, 4, 0, 10),
        Child = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = palette.Text,
        },
    };

    private static Border Table(List<string[]> rows, Palette palette)
    {
        int columns = rows.Max(r => r.Length);
        var grid = new Grid();
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < columns; c++)
            {
                var text = TextBlockFor(c < rows[r].Length ? rows[r][c] : "", palette, 13, r == 0 ? FontWeights.SemiBold : FontWeights.Normal, new Thickness(0));
                text.LineHeight = 19;
                var cell = new Border
                {
                    Padding = new Thickness(10, 7, 10, 7),
                    BorderBrush = palette.Track,
                    BorderThickness = new Thickness(0, 0, c < columns - 1 ? 1 : 0, r < rows.Count - 1 ? 1 : 0),
                    Background = r == 0 ? new SolidColorBrush(Palette.WithAlpha(palette.Theme.Accent, 0x14)) : null,
                    Child = text,
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return new Border
        {
            Child = grid,
            BorderBrush = palette.Track,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 4, 0, 12),
        };
    }

    private static void AddInlines(InlineCollection inlines, string text, Palette palette)
    {
        int last = 0;
        foreach (Match m in InlineToken().Matches(text))
        {
            if (m.Index > last) inlines.Add(new Run { Text = text[last..m.Index] });
            var token = m.Value;
            if (token.StartsWith("**"))
            {
                inlines.Add(new Run { Text = token[2..^2], FontWeight = FontWeights.SemiBold });
            }
            else if (token.StartsWith('`'))
            {
                inlines.Add(new Run { Text = token[1..^1], FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = palette.AccentBrush });
            }
            else if (token.StartsWith('*'))
            {
                inlines.Add(new Run { Text = token[1..^1], FontStyle = Windows.UI.Text.FontStyle.Italic });
            }
            else
            {
                string label = token, url = token;
                if (token.StartsWith('['))
                {
                    int split = token.IndexOf("](", StringComparison.Ordinal);
                    label = token[1..split];
                    url = token[(split + 2)..^1];
                }
                // Only web links: research text comes from the web, so a file: or custom-protocol link
                // must never become clickable.
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                {
                    var link = new Hyperlink { NavigateUri = uri, Foreground = palette.AccentBrush, UnderlineStyle = UnderlineStyle.None };
                    link.Inlines.Add(new Run { Text = label == url ? uri.Host.Replace("www.", "") : label });
                    inlines.Add(link);
                }
                else
                {
                    inlines.Add(new Run { Text = label });
                }
            }
            last = m.Index + m.Length;
        }
        if (last < text.Length) inlines.Add(new Run { Text = text[last..] });
    }
}
