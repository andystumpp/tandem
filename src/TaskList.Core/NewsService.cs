using System.Text.Json;

namespace TaskList.Core;

public sealed record NewsItem(string Headline, string? Url);

/// <summary>
/// The AI-news ticker: asks Claude (with web search) for today's top AI headlines a few times a day
/// and caches them in %LOCALAPPDATA%\TaskList\news.json so restarts don't trigger new fetches.
/// </summary>
public static class NewsService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(3);

    private sealed class Cache
    {
        public DateTime FetchedAtUtc { get; set; }
        public List<NewsItem> Items { get; set; } = new();
    }

    private static string CachePath =>
        Path.Combine(Path.GetDirectoryName(Preferences.DefaultPath)!, "news.json");

    public static (List<NewsItem> Items, DateTime FetchedAtUtc) LoadCached()
    {
        try
        {
            if (File.Exists(CachePath))
            {
                var cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(CachePath));
                if (cache != null) return (cache.Items, cache.FetchedAtUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return (new List<NewsItem>(), DateTime.MinValue);
    }

    public static async Task<List<NewsItem>> FetchAsync(CancellationToken cancellationToken = default)
    {
        const string prompt =
            "Search the web for the 6 most important AI news stories from the last 24 hours. " +
            "Reply with exactly one story per line in the form: headline | url. " +
            "Keep each headline under 70 characters. No numbering, no other text.";
        var output = await AiCli.RunAsync(prompt, Path.GetTempPath(), ["WebSearch", "WebFetch"],
            TimeSpan.FromMinutes(5), cancellationToken);
        var items = Parse(output);
        if (items.Count > 0)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(new Cache { FetchedAtUtc = DateTime.UtcNow, Items = items }));
                AddToHistory(items, DateTime.Now);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return items;
    }

    // ---------- History (for the news panel) ----------

    public sealed record HistoryItem(string Headline, string? Url, DateTime SeenAt);

    public static readonly TimeSpan HistoryLength = TimeSpan.FromDays(14);

    private static string HistoryPath =>
        Path.Combine(Path.GetDirectoryName(Preferences.DefaultPath)!, "news-history.json");

    /// <summary>Every headline seen in the last two weeks, newest first, each once.</summary>
    public static List<HistoryItem> LoadHistory()
    {
        var history = new List<HistoryItem>();
        try
        {
            if (File.Exists(HistoryPath)) history = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(HistoryPath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        // Seed from the current cache, so the panel isn't empty before the next fetch.
        var (cached, fetched) = LoadCached();
        if (cached.Count > 0 && fetched > DateTime.MinValue) history = Merge(history, cached, fetched.ToLocalTime());
        return history;
    }

    public static void AddToHistory(IEnumerable<NewsItem> items, DateTime seenAt)
    {
        var history = Merge(LoadHistory(), items, seenAt);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(history));
    }

    private static List<HistoryItem> Merge(List<HistoryItem> history, IEnumerable<NewsItem> items, DateTime seenAt)
    {
        var known = new HashSet<string>(history.Select(h => h.Headline), StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (known.Add(item.Headline)) history.Add(new HistoryItem(item.Headline, item.Url, seenAt));
        }
        return history.Where(h => DateTime.Now - h.SeenAt < HistoryLength).OrderByDescending(h => h.SeenAt).ToList();
    }

    /// <summary>True only for absolute http(s) URLs, so nothing else is ever opened or fetched.</summary>
    public static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static List<NewsItem> Parse(string output)
    {
        var items = new List<NewsItem>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim().TrimStart('-', '*', '•', ' ');
            if (line.Length == 0) continue;
            var parts = line.Split('|', 2);
            var headline = parts[0].Trim().Trim('*');
            var url = parts.Length > 1 ? parts[1].Trim().Trim('<', '>') : null;
            if (url != null && !IsWebUrl(url)) url = null;
            if (headline.Length > 0 && (url != null || parts.Length == 1)) items.Add(new NewsItem(headline, url));
        }
        return items.Take(8).ToList();
    }
}
