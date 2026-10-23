namespace TaskList.Core;

/// <summary>
/// The "[Key:Value, Key:Value]" block in front of an area or task. Order and unknown keys are kept
/// exactly, so writing the file back doesn't reshuffle what the user typed.
/// </summary>
public sealed class Attributes
{
    private readonly List<KeyValuePair<string, string>> _items = new();

    public IReadOnlyList<KeyValuePair<string, string>> Items => _items;
    public int Count => _items.Count;

    public string? Get(string key) =>
        _items.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    public void Set(string key, string? value)
    {
        int i = _items.FindIndex(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
        if (value == null)
        {
            if (i >= 0) _items.RemoveAt(i);
        }
        else if (i >= 0)
        {
            _items[i] = new(_items[i].Key, value);
        }
        else
        {
            _items.Add(new(key, value));
        }
    }

    public bool GetBool(string key, bool fallback) =>
        bool.TryParse(Get(key), out var b) ? b : fallback;

    public int? GetInt(string key) => int.TryParse(Get(key), out var n) ? n : null;

    /// <summary>Parses "Active:true, P:1". Returns null if any part isn't Key:Value (so "[WIP]" stays task text).</summary>
    public static Attributes? TryParse(string inner)
    {
        var attributes = new Attributes();
        foreach (var part in inner.Split(','))
        {
            var kv = part.Split(':', 2);
            if (kv.Length != 2 || kv[0].Trim().Length == 0 || kv[0].Trim().Contains(' ')) return null;
            attributes._items.Add(new(kv[0].Trim(), kv[1].Trim()));
        }
        return attributes;
    }

    public override string ToString() =>
        _items.Count == 0 ? "" : "[" + string.Join(", ", _items.Select(kv => $"{kv.Key}:{kv.Value}")) + "]";
}

/// <summary>A single checkbox line in backlog.md.</summary>
public sealed class TaskItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public Attributes Attributes { get; } = new();

    /// <summary>Indented lines under the task, kept verbatim.</summary>
    public List<string> NoteLines { get; } = new();

    /// <summary>P: lower comes first; tasks without one sort last.</summary>
    public int? Priority
    {
        get => Attributes.GetInt("P");
        set => Attributes.Set("P", value?.ToString());
    }

    /// <summary>Research:true asks for background research on this task.</summary>
    public bool WantsResearch => Attributes.GetBool("Research", false);

    /// <summary>Owner:Name: someone else is doing this one, so you're waiting on them.</summary>
    public string? Owner
    {
        get => Attributes.Get("Owner") is { Length: > 0 } owner ? owner : null;
        set => Attributes.Set("Owner", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public TaskItem() { }

    public TaskItem(string text) => Text = text;
}

/// <summary>A "## [Active:true, P:1] Area" with one "[Outcome]: …" line and its tasks.</summary>
public sealed class Area
{
    public string Title { get; set; } = "";
    public Attributes Attributes { get; } = new();
    public string Outcome { get; set; } = "";
    public List<TaskItem> Tasks { get; } = new();

    public bool Active
    {
        get => Attributes.GetBool("Active", true);
        set => Attributes.Set("Active", value ? "true" : "false");
    }

    public int? Priority => Attributes.GetInt("P");

    public Area() { }

    public Area(string title) => Title = title;
}

public sealed record ParseError(int Line, string Message);

/// <summary>The whole backlog.md file.</summary>
public sealed class BacklogDocument
{
    /// <summary>The optional "# Title" line.</summary>
    public string? Title { get; set; }

    /// <summary>Standalone tasks before the first area: the Focus list.</summary>
    public List<TaskItem> Focus { get; } = new();

    public List<Area> Areas { get; } = new();

    /// <summary>Lines that don't fit the format. While there are any, the app won't write the file.</summary>
    public List<ParseError> Errors { get; } = new();

    public Area? FindArea(string title) =>
        Areas.FirstOrDefault(a => string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<TaskItem> AllTasks => Focus.Concat(Areas.SelectMany(a => a.Tasks));

    /// <summary>Tasks that count towards progress: focus plus active areas.</summary>
    public IEnumerable<TaskItem> CountedTasks => Focus.Concat(Areas.Where(a => a.Active).SelectMany(a => a.Tasks));

    public (int Done, int Open) Progress
    {
        get
        {
            int done = 0, open = 0;
            foreach (var t in CountedTasks)
            {
                if (t.Done) done++; else open++;
            }
            return (done, open);
        }
    }

    /// <summary>Stable sort by priority (P), keeping file order for ties.</summary>
    public static List<(TaskItem Task, int Index)> Ordered(IEnumerable<TaskItem> tasks) =>
        tasks.Select((t, i) => (t, i)).OrderBy(x => x.t.Priority ?? int.MaxValue).ThenBy(x => x.i).ToList();
}
