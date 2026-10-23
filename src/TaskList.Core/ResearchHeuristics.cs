using System.Text.RegularExpressions;

namespace TaskList.Core;

/// <summary>
/// Decides which tasks get researched automatically. A task qualifies when it is phrased as a question,
/// or uses words that signal open-ended work (compare, investigate, find out, …). [Research:true] always
/// qualifies and [Research:false] never does.
/// </summary>
public static partial class ResearchHeuristics
{
    // Whole-word cues, matched case-insensitively anywhere in the task.
    [GeneratedRegex(@"\b(research|investigate|explore|evaluate|compare|comparison|analy[sz]e|assess|benchmark|" +
        @"look\s+into|find\s+out|figure\s+out|learn\s+about|read\s+up|dig\s+into|options?\s+for|alternatives?|" +
        @"pros\s+and\s+cons|trade-?offs?|best\s+(way|practice|practices|approach|tool|tools)|recommendations?|" +
        @"how\s+(to|do|does|can|should|would)|what\s+(is|are|does|should|would)|why|which|whether)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Cues();

    public static bool ShouldResearch(TaskItem task)
    {
        var flag = task.Attributes.Get("Research");
        if (flag != null && bool.TryParse(flag, out var explicitly)) return explicitly;
        var text = task.Text.Trim();
        if (text.Length < 8) return false;
        return text.Contains('?') || Cues().IsMatch(text);
    }

    /// <summary>The words that triggered it, for a tooltip ("Researched because it asks a question").</summary>
    public static string? Reason(TaskItem task)
    {
        if (task.WantsResearch) return "marked [Research:true]";
        if (!ShouldResearch(task)) return null;
        if (task.Text.Contains('?')) return "it asks a question";
        var m = Cues().Match(task.Text);
        return m.Success ? $"it says “{m.Value.ToLowerInvariant()}”" : null;
    }
}
