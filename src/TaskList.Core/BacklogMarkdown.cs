using System.Text;
using System.Text.RegularExpressions;

namespace TaskList.Core;

/// <summary>
/// Reads and writes backlog.md:
/// <code>
/// # Tasks
///
/// - [ ] Call the dentist
/// - [ ] [P:1] Plan the weekend trip
///
/// ## [Active:true, P:1, Size:M] Home Garden
/// [Outcome]: Grow our own vegetables by summer
/// - [ ] [Research:true] Compare raised bed kits
/// - [x] Buy seeds
/// </code>
/// Tasks before the first "##" are standalone Focus tasks. Attribute blocks are optional and unknown keys
/// are kept. Lines that don't fit are reported with their line number, and the file is then left alone.
/// </summary>
public static partial class BacklogMarkdown
{
    [GeneratedRegex(@"^[-*] \[( |x|X)\](?: +(.*))?$")]
    private static partial Regex TaskLine();

    [GeneratedRegex(@"^## +(.+)$")]
    private static partial Regex AreaLine();

    [GeneratedRegex(@"^# +(.+)$")]
    private static partial Regex TitleLine();

    [GeneratedRegex(@"^\[Outcome\]\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex OutcomeLine();

    [GeneratedRegex(@"^\[([^\]]*)\]\s*(.*)$")]
    private static partial Regex LeadingBracket();

    public static BacklogDocument Parse(string markdown)
    {
        var doc = new BacklogDocument();
        Area? area = null;
        TaskItem? lastTask = null;
        bool seenContent = false;

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            var raw = lines[n].TrimEnd();
            int lineNo = n + 1;
            if (raw.Length == 0) continue;

            if (lastTask != null && (raw.StartsWith("  ") || raw.StartsWith('\t')))
            {
                lastTask.NoteLines.Add(raw);
                continue;
            }
            var line = raw.Trim();

            var title = TitleLine().Match(line);
            if (title.Success)
            {
                if (seenContent || doc.Title != null)
                {
                    doc.Errors.Add(new(lineNo, "A \"# Title\" is only allowed once, at the top."));
                }
                else
                {
                    doc.Title = title.Groups[1].Value.Trim();
                }
                lastTask = null;
                continue;
            }
            seenContent = true;

            var areaMatch = AreaLine().Match(line);
            if (areaMatch.Success)
            {
                area = new Area();
                var (attributes, text) = SplitAttributes(areaMatch.Groups[1].Value);
                area.Title = text;
                if (attributes != null) CopyAttributes(attributes, area.Attributes);
                if (area.Title.Length == 0) doc.Errors.Add(new(lineNo, "The area needs a name after \"##\"."));
                doc.Areas.Add(area);
                lastTask = null;
                continue;
            }

            var outcome = OutcomeLine().Match(line);
            if (outcome.Success)
            {
                if (area == null) doc.Errors.Add(new(lineNo, "[Outcome] must come right after an area (\"## …\")."));
                else if (area.Outcome.Length > 0 || area.Tasks.Count > 0) doc.Errors.Add(new(lineNo, $"\"{area.Title}\" already has an outcome; keep it to one line."));
                else area.Outcome = outcome.Groups[1].Value.Trim();
                lastTask = null;
                continue;
            }

            var taskMatch = TaskLine().Match(line);
            if (taskMatch.Success)
            {
                var task = ParseTask(taskMatch.Groups[2].Value, taskMatch.Groups[1].Value is "x" or "X");
                (area?.Tasks ?? doc.Focus).Add(task);
                lastTask = task;
                continue;
            }

            doc.Errors.Add(new(lineNo, $"Not a task, area or outcome: \"{Shorten(line)}\""));
            lastTask = null;
        }
        return doc;
    }

    public static string Serialize(BacklogDocument doc)
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append('\n');
        void Tasks(IEnumerable<TaskItem> tasks)
        {
            foreach (var t in tasks)
            {
                Line(FormatTask(t));
                foreach (var note in t.NoteLines) Line(note);
            }
        }

        if (doc.Title != null)
        {
            Line($"# {doc.Title}");
            Line();
        }
        if (doc.Focus.Count > 0)
        {
            Tasks(doc.Focus);
            Line();
        }
        foreach (var area in doc.Areas)
        {
            var attributes = area.Attributes.ToString();
            Line($"## {(attributes.Length > 0 ? attributes + " " : "")}{area.Title}");
            if (area.Outcome.Length > 0) Line($"[Outcome]: {area.Outcome}");
            Tasks(area.Tasks);
            Line();
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    public static string FormatTask(TaskItem t)
    {
        var attributes = t.Attributes.ToString();
        return $"- [{(t.Done ? "x" : " ")}] {(attributes.Length > 0 ? attributes + " " : "")}{t.Text.Trim()}".TrimEnd();
    }

    /// <summary>Turns user-typed text (which may start with "[P:1]") into a task.</summary>
    public static TaskItem ParseTask(string text, bool done = false)
    {
        var task = new TaskItem { Done = done };
        var (attributes, rest) = SplitAttributes(text.Trim());
        task.Text = rest;
        if (attributes != null) CopyAttributes(attributes, task.Attributes);
        return task;
    }

    private static (Attributes? Attributes, string Text) SplitAttributes(string text)
    {
        var m = LeadingBracket().Match(text.Trim());
        if (!m.Success) return (null, text.Trim());
        var attributes = Attributes.TryParse(m.Groups[1].Value);
        return attributes == null ? (null, text.Trim()) : (attributes, m.Groups[2].Value.Trim());
    }

    private static void CopyAttributes(Attributes from, Attributes to)
    {
        foreach (var kv in from.Items) to.Set(kv.Key, kv.Value);
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..57] + "…";

    public static string Template() =>
        """
        # Tasks

        - [ ] Pick the one thing that matters most today
        - [ ] Add a few tasks you want to keep in view

        ## [Active:true, P:1, Size:M] Work
        [Outcome]: The Q4 plan is agreed and shared with the team
        - [ ] [P:1] Draft the outline
        - [ ] Review with the team
        - [ ] Compare planning templates other teams use

        ## [Active:true, P:2, Size:S] Health
        [Outcome]: Run a half marathon by April
        - [ ] Book a physio session
        - [x] Buy running shoes

        ## [Active:false] Side projects
        [Outcome]: Ideas worth keeping for later
        - [ ] Sketch ideas

        """.Replace("\r\n", "\n");
}
