using System.Text.Json;

namespace TaskList.Core;

/// <summary>
/// Day-to-day state that isn't work data and so stays out of backlog.md: which tasks you planned for
/// today, which one is NEXT, and when tasks were completed. Stored in %LOCALAPPDATA%\TaskList\state.json.
/// Tasks are identified by their text.
/// </summary>
public sealed class LocalState
{
    public string TodayDate { get; set; } = "";
    public List<string> Today { get; set; } = new();
    public string? Next { get; set; }

    /// <summary>Task text → when it was ticked off.</summary>
    public Dictionary<string, DateTime> Completed { get; set; } = new();

    /// <summary>Research results (file names) the user has already opened; the rest count as unread.</summary>
    public List<string> SeenResearch { get; set; } = new();

    /// <summary>"Research ready" banners still on screen (task text → when it finished); each lasts an hour.</summary>
    public Dictionary<string, DateTime> ResearchBanners { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(Preferences.DefaultPath)!, "state.json");

    public static LocalState Load(string? path = null)
    {
        path ??= DefaultPath;
        LocalState state = new();
        try
        {
            if (File.Exists(path)) state = JsonSerializer.Deserialize<LocalState>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        state.RollOverIfNewDay();
        return state;
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>"Today" is a daily plan: a new day starts with an empty one.</summary>
    public bool RollOverIfNewDay()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (TodayDate == today) return false;
        TodayDate = today;
        Today.Clear();
        return true;
    }

    public bool IsToday(TaskItem t) => Today.Contains(t.Text);

    public void SetToday(TaskItem t, bool on)
    {
        Today.Remove(t.Text);
        if (on) Today.Add(t.Text);
    }

    public bool IsNext(TaskItem t) => Next == t.Text;

    /// <summary>Keeps today/next/history attached when a task is renamed.</summary>
    public void Rename(string oldText, string newText)
    {
        int i = Today.IndexOf(oldText);
        if (i >= 0) Today[i] = newText;
        if (Next == oldText) Next = newText;
        if (Completed.Remove(oldText, out var when)) Completed[newText] = when;
    }
}
