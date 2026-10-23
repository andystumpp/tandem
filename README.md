# TaskList

A native Windows task app (WinUI 3, .NET 10) that keeps everything in one Markdown file, `backlog.md`.
Edit the file in any editor and the app picks up the change within two seconds; edits made in the
app never overwrite a newer outside edit.


## Run

```
dotnet run --project src/TaskList.App -p:Platform=x64
```

On first launch the app creates `Documents\TaskList\backlog.md` from a starter template.

## backlog.md format

```markdown
# Tasks

- [ ] Fix the flaky login test
- [ ] [P:1] Ship the 1.2 release

## [Active:true, P:1, Size:M] API Performance
[Outcome]: p95 latency under 200 ms on every endpoint
- [ ] [Research:true] Compare Redis and Memcached for caching
- [x] Add request tracing

## [Active:false] Side projects
[Outcome]: Ideas worth keeping for later
- [ ] Sketch ideas
```

- Tasks before the first `##` are standalone **Focus** tasks.
- `## [attributes] Name` starts an **area**, followed by one `[Outcome]: …` line.
- Attributes are optional, comma-separated `Key:Value` pairs in one bracket block. `Active` decides whether
  an area counts, `P` is priority (lower comes first), `Research:true` asks Claude to research a task.
  Unknown attributes are kept.
- If a line doesn't fit, the app shows the line number and stops saving until it's fixed, so nothing is lost.
- Today's plan, the NEXT task, completion history, settings and research results live in
  `%LOCALAPPDATA%\TaskList\`, not in the file.

## In the app

- Click the circle to complete a task; double-click the text to edit; right-click for more (Make NEXT,
  plan for today, priority, move to another area, research, delete). Right-click an area header for area options.
- Hover a task for the ☀ (plan for today) and ↑/↓ buttons. The NEXT task always shows them.
- Footer, left to right: add a task (Ctrl+N), research (results, unread badge, auto-research on/off),
  pin to the bottom-right corner (drag the window to unpin), keep on top, theme, reload, open another
  file, edit in VS Code.
- Each area has a + next to its outcome for adding tasks. Inactive areas are listed at the end, collapsed
  until you open them. Long Focus lists fold behind "Show N more".
- **Themes:** ten looks. Pearl, Frost, Blush, Graphite and Nebula are tinted glass (the desktop shows
  through; the Glass slider sets how frosted). Alpine, Lagoon, Aurora, Sakura and Dunes show a scene across
  the top that fades into a matching color. Each theme pairs its accent with a second hue for the
  iridescent ring, the NEXT marker and the sparkle on Claude-powered research. Scenes are drawn by
  `tools/gen_backgrounds.py`.
- **NEXT** is automatic: the first task planned for today (sun), in list order; with nothing planned,
  the first open task. Planned rows are highlighted.

- **Research with Claude** runs `claude -p` with web search in the background (two at a time) and shows a
  notification when the result is ready. Tasks marked `[Research:true]` are researched automatically.
- The title bar shows an **AI news ticker**, refreshed through Claude every 3 hours (turn it off in the palette menu).
  Both need the [Claude Code CLI](https://claude.com/claude-code) on your PATH.

## Layout

- `src/TaskList.Core` – Markdown parser/writer, file store with sync, prefs, research runner
- `src/TaskList.App` – WinUI 3 window
- `tests/TaskList.Core.Tests` – xUnit tests for the format and sync rules
