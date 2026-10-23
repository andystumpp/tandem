using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskList.Core;

/// <summary>Topic tags for headlines, guessed from their wording.</summary>
public static partial class NewsTopics
{
    private static readonly (string Topic, Regex Pattern)[] Rules =
    [
        ("Security", Rx(@"\b(breach\w*|hack\w*|vulnerab\w*|exploit\w*|rogue|attack\w*|security|jailbreak\w*|malware|phishing|leak\w*)\b")),
        ("Policy", Rx(@"\b(ban\w*|regulat\w*|law\w*|government|federal|congress|senate|eu|policy|court|lawsuit\w*|sue\w*|antitrust|executive order|dhs|ftc)\b")),
        ("Funding", Rx(@"\b(rais\w*|funding|valuation|ipo|shares|acqui\w*|invest\w*|billion|\$\d+\w*|deal|merger|stake)\b|\$\d")),
        ("Chips", Rx(@"\b(chip\w*|gpu\w*|nvidia|tpu\w*|trainium|data ?cent\w*|semiconductor\w*|amd|tsmc|compute)\b")),
        ("Models", Rx(@"\b(model\w*|gpt[-\w]*|claude|gemini|llama|mistral|llm\w*|weights|open[- ]source|reasoning|agent\w*)\b")),
        ("Research", Rx(@"\b(paper\w*|study|studies|researchers?|benchmark\w*|breakthrough|scientists?)\b")),
        ("Products", Rx(@"\b(app\w*|feature\w*|launch\w*|rolls? out|copilot|assistant|update\w*|release\w*|unveil\w*|tool\w*)\b")),
    ];

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Classify(string headline)
    {
        foreach (var (topic, pattern) in Rules)
        {
            if (pattern.IsMatch(headline)) return topic;
        }
        return "Industry";
    }
}

/// <summary>
/// Preview images for news articles: reads the page's og:image (what link previews use) and keeps the
/// picture in %LOCALAPPDATA%\TaskList\news-images. No Claude usage; failures are remembered so they
/// aren't retried every time.
/// </summary>
public static partial class NewsImages
{
    [GeneratedRegex(@"<meta[^>]+(?:property|name)\s*=\s*[""'](?:og:image(?::secure_url)?|twitter:image(?::src)?)[""'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ContentAttr();

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(4);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 5 * 1024 * 1024,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) TaskList/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,image/*;q=0.9,*/*;q=0.8");
        return client;
    }

    public static string Folder => Path.Combine(Path.GetDirectoryName(Preferences.DefaultPath)!, "news-images");

    private static string MissesPath => Path.Combine(Folder, "misses.txt");

    private static string Key(string url) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();

    /// <summary>The cached image for an article, if we already have one.</summary>
    public static string? Cached(string articleUrl)
    {
        if (!Directory.Exists(Folder)) return null;
        return Directory.EnumerateFiles(Folder, Key(articleUrl) + ".*").FirstOrDefault();
    }

    /// <summary>Finds and downloads the article's preview image. Returns its local path, or null.</summary>
    public static async Task<string?> GetAsync(string articleUrl, CancellationToken cancellationToken = default)
    {
        if (!NewsService.IsWebUrl(articleUrl)) return null;
        if (Cached(articleUrl) is { } hit) return hit;
        var key = Key(articleUrl);
        Directory.CreateDirectory(Folder);
        if (File.Exists(MissesPath) && File.ReadAllLines(MissesPath).Contains(key)) return null;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var html = await Http.GetStringAsync(articleUrl, cancellationToken);
            var imageUrl = FindImageUrl(html, articleUrl);
            if (imageUrl == null || !NewsService.IsWebUrl(imageUrl))
            {
                RememberMiss(key);
                return null;
            }
            using var response = await Http.GetAsync(imageUrl, cancellationToken);
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!response.IsSuccessStatusCode || !type.StartsWith("image/"))
            {
                RememberMiss(key);
                return null;
            }
            var ext = type switch { "image/png" => ".png", "image/webp" => ".webp", "image/gif" => ".gif", _ => ".jpg" };
            var path = Path.Combine(Folder, key + ext);
            await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UriFormatException or InvalidOperationException)
        {
            RememberMiss(key);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static string? FindImageUrl(string html, string pageUrl)
    {
        foreach (Match tag in MetaTag().Matches(html))
        {
            var content = ContentAttr().Match(tag.Value);
            if (!content.Success) continue;
            var value = WebUtility.HtmlDecode(content.Groups[1].Value.Trim());
            if (Uri.TryCreate(new Uri(pageUrl), value, out var absolute) && absolute.Scheme.StartsWith("http")) return absolute.ToString();
        }
        return null;
    }

    private static void RememberMiss(string key)
    {
        try { File.AppendAllText(MissesPath, key + Environment.NewLine); } catch (IOException) { }
    }
}
