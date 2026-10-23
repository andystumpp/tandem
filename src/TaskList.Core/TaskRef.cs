namespace TaskList.Core;

/// <summary>
/// Points at a task in a way that survives the file being reloaded: its list (focus or an area),
/// its position in the file, and its text.
/// </summary>
public sealed record TaskRef(string? AreaTitle, int Index, string Text)
{
    public static TaskRef InFocus(int index, TaskItem task) => new(null, index, task.Text);

    public bool IsFocus => AreaTitle == null;

    public List<TaskItem>? ResolveList(BacklogDocument doc) =>
        AreaTitle == null ? doc.Focus : doc.FindArea(AreaTitle)?.Tasks;

    /// <summary>Same position and text first, otherwise the nearest task with the same text.</summary>
    public TaskItem? Resolve(BacklogDocument doc)
    {
        var list = ResolveList(doc);
        if (list == null) return null;
        if (Index >= 0 && Index < list.Count && list[Index].Text == Text) return list[Index];

        TaskItem? best = null;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Text != Text) continue;
            int distance = Math.Abs(i - Index);
            if (distance < bestDistance) { best = list[i]; bestDistance = distance; }
        }
        return best;
    }
}
