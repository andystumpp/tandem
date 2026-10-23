using System.Text.Json;

namespace TaskList.Core;

/// <summary>Per-machine settings, stored in %LOCALAPPDATA%\TaskList\prefs.json (never in backlog.md).</summary>
public sealed class Preferences
{
    public string BacklogPath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaskList", "backlog.md");

    public bool HideDone { get; set; }
    public bool TodayOnly { get; set; }
    public List<string> CollapsedAreas { get; set; } = new();

    /// <summary>Inactive areas start collapsed; these are the ones the user opened.</summary>
    public List<string> ExpandedAreas { get; set; } = new();

    /// <summary>Name of one of the app's themes (Crystal, Glacier, …, Dunes).</summary>
    public string Theme { get; set; } = "Crystal";

    /// <summary>0 = fully see-through glass, 1 = frosted.</summary>
    public double Frost { get; set; } = 0.25;
    public bool AlwaysOnTop { get; set; }

    /// <summary>Keeps the window docked to the bottom-right corner of the screen until it is dragged.</summary>
    public bool PinnedToCorner { get; set; }

    /// <summary>Research [Research:true] tasks automatically in the background.</summary>
    public bool AutoResearch { get; set; } = true;
    public bool ShowNews { get; set; } = true;

    /// <summary>Show each area's "[Outcome]:" line under its title.</summary>
    public bool ShowOutcomes { get; set; } = true;

    /// <summary>Bring the app to the front when the mouse rests against the right edge of the screen.</summary>
    public bool RevealAtRightEdge { get; set; } = true;

    /// <summary>Which AI agent does research and fetches news: Claude, Copilot or Custom.</summary>
    public string AiEngine { get; set; } = "Claude";

    /// <summary>For the Custom engine: a command line; {prompt_file} or {prompt} is filled in, else stdin.</summary>
    public string AiCustomCommand { get; set; } = "";

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskList", "prefs.json");

    public static Preferences Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), JsonOptions) ?? new Preferences();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken prefs file shouldn't stop the app; fall back to defaults.
        }
        return new Preferences();
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
}
