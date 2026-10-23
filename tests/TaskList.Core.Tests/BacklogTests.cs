using TaskList.Core;

namespace TaskList.Core.Tests;

public class BacklogMarkdownTests
{
    private const string Sample = """
        # Tasks

        - [ ] Fix the flaky login test
        - [x] [P:1] Ship the 1.2 release
          - a note

        ## [Active:true, P:1, Size:M] API Performance
        [Outcome]: p95 latency under 200 ms on every endpoint
        - [ ] [P:2, Research:true] Compare Redis and Memcached for caching
        - [x] Add request tracing
        - [ ] [WIP] not attributes

        ## [Active:false, Owner:me] Side projects
        [Outcome]: Ideas for later
        - [ ] Sketch ideas
        """;

    [Fact]
    public void Parses_focus_areas_outcomes_and_attributes()
    {
        var doc = BacklogMarkdown.Parse(Sample);

        Assert.Empty(doc.Errors);
        Assert.Equal("Tasks", doc.Title);
        Assert.Equal(2, doc.Focus.Count);
        Assert.Equal(1, doc.Focus[1].Priority);
        Assert.True(doc.Focus[1].Done);
        Assert.Single(doc.Focus[1].NoteLines);

        var area = doc.Areas[0];
        Assert.Equal("API Performance", area.Title);
        Assert.True(area.Active);
        Assert.Equal(1, area.Priority);
        Assert.Equal("M", area.Attributes.Get("Size"));
        Assert.Equal("p95 latency under 200 ms on every endpoint", area.Outcome);
        Assert.True(area.Tasks[0].WantsResearch);
        Assert.Equal("[WIP] not attributes", area.Tasks[2].Text);

        Assert.False(doc.Areas[1].Active);
        Assert.Equal("me", doc.Areas[1].Attributes.Get("Owner"));
    }

    [Fact]
    public void Round_trips_exactly()
    {
        var once = BacklogMarkdown.Serialize(BacklogMarkdown.Parse(Sample));
        Assert.Equal(Sample.Replace("\r\n", "\n") + "\n", once);
    }

    [Fact]
    public void Reports_malformed_lines_with_line_numbers()
    {
        var doc = BacklogMarkdown.Parse("# Tasks\n\n- [ ] ok\nrandom text\n## Area\n[Outcome]: a\n[Outcome]: b\n");
        Assert.Equal(2, doc.Errors.Count);
        Assert.Equal(4, doc.Errors[0].Line);
        Assert.Equal(7, doc.Errors[1].Line);
    }

    [Fact]
    public void Progress_counts_focus_and_active_areas_only()
    {
        var doc = BacklogMarkdown.Parse(Sample);
        Assert.Equal((2, 3), doc.Progress);
    }

    [Fact]
    public void Orders_by_priority_then_file_order()
    {
        var doc = BacklogMarkdown.Parse("- [ ] a\n- [ ] [P:2] b\n- [ ] [P:1] c\n");
        Assert.Equal(["c", "b", "a"], BacklogDocument.Ordered(doc.Focus).Select(x => x.Task.Text));
    }

    [Fact]
    public void Template_parses_cleanly()
    {
        var doc = BacklogMarkdown.Parse(BacklogMarkdown.Template());
        Assert.Empty(doc.Errors);
        Assert.Equal(3, doc.Areas.Count);
    }
}

public class BacklogStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tasklist-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "backlog.md");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static void Touch(string path) => File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

    [Fact]
    public void Creates_the_file_from_the_template()
    {
        var store = new BacklogStore(FilePath);
        store.Load();
        Assert.True(File.Exists(FilePath));
        Assert.NotEmpty(store.Document.Areas);
    }

    [Fact]
    public void Mutate_does_not_overwrite_an_external_edit()
    {
        var store = new BacklogStore(FilePath);
        store.Load();

        File.WriteAllText(FilePath, File.ReadAllText(FilePath).Replace("# Tasks\n\n", "# Tasks\n\n- [ ] Added elsewhere\n"));
        Touch(FilePath);

        store.Mutate(doc => { doc.Focus.Add(new TaskItem("Added in app")); return true; });

        var saved = File.ReadAllText(FilePath);
        Assert.Contains("Added elsewhere", saved);
        Assert.Contains("Added in app", saved);
    }

    [Fact]
    public void Refuses_to_write_a_file_with_errors()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "- [ ] ok\nthis line is not valid\n");
        var store = new BacklogStore(FilePath);
        store.Load();

        Assert.False(store.Mutate(doc => { doc.Focus.Add(new TaskItem("x")); return true; }));
        Assert.Equal("- [ ] ok\nthis line is not valid\n", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Poll_picks_up_external_changes()
    {
        var store = new BacklogStore(FilePath);
        store.Load();
        bool external = false;
        store.Changed += e => external = e;

        File.WriteAllText(FilePath, "- [ ] Only task\n");
        Touch(FilePath);

        Assert.True(store.PollForExternalChanges());
        Assert.True(external);
        Assert.Single(store.Document.Focus);
        Assert.False(store.PollForExternalChanges());
    }

    [Fact]
    public void TaskRef_finds_a_task_after_it_moved()
    {
        var doc = BacklogMarkdown.Parse("- [ ] A\n- [ ] B\n");
        var reference = TaskRef.InFocus(1, doc.Focus[1]);
        doc.Focus.Insert(0, new TaskItem("New"));
        Assert.Same(doc.Focus[2], reference.Resolve(doc));
    }

    [Fact]
    public void News_lines_parse()
    {
        var items = NewsService.Parse("- OpenAI ships X | https://a.example/1\nNo url here\n* Bad | notaurl\n");
        Assert.Equal(2, items.Count);
        Assert.Equal("https://a.example/1", items[0].Url);
    }
}

public class ResearchHeuristicsTests
{
    [Theory]
    [InlineData("Which logging library should we use for the service?", true)]
    [InlineData("Compare Redis and Memcached for caching", true)]
    [InlineData("Find out how connection pooling works", true)]
    [InlineData("How do I know whether a query uses the index", true)]
    [InlineData("Look into HTTP/3 support", true)]
    [InlineData("Rotate the API keys", false)]
    [InlineData("Fix the flaky login test", false)]
    [InlineData("Bump version", false)]
    public void Detects_research_style_tasks(string text, bool expected) =>
        Assert.Equal(expected, ResearchHeuristics.ShouldResearch(new TaskItem(text)));

    [Fact]
    public void Explicit_attribute_wins()
    {
        Assert.True(ResearchHeuristics.ShouldResearch(BacklogMarkdown.ParseTask("[Research:true] Rotate the API keys")));
        Assert.False(ResearchHeuristics.ShouldResearch(BacklogMarkdown.ParseTask("[Research:false] Which logging library should we use?")));
    }
}

public class NewsExtrasTests
{
    [Theory]
    [InlineData("OpenAI races to raise $30B before Anthropic's IPO", "Funding")]
    [InlineData("Hacker used AI agents to breach websites of 100+ companies", "Security")]
    [InlineData("DHS bans DeepSeek from federal civilian agencies", "Policy")]
    [InlineData("Amazon open-sources TorchNeuron for PyTorch on Trainium", "Chips")]
    [InlineData("Microsoft 365 E7 adds controls to corral rogue AI agents", "Security")]
    [InlineData("Google releases Gemini 3 with stronger reasoning", "Models")]
    public void Classifies_headlines(string headline, string topic) =>
        Assert.Equal(topic, NewsTopics.Classify(headline));

    [Fact]
    public void Finds_og_image()
    {
        var html = "<head><meta property=\"og:image\" content=\"/img/cover.jpg?a=1&amp;b=2\"></head>";
        Assert.Equal("https://news.example/img/cover.jpg?a=1&b=2", NewsImages.FindImageUrl(html, "https://news.example/story/1"));
    }
}

[Collection("AiCli")]
public class AiCliTests
{
    [Fact]
    public async Task Custom_command_gets_the_prompt_file()
    {
        AiCli.Engine = AiEngine.Custom;
        AiCli.CustomCommand = "cmd /c type {prompt_file}";
        try
        {
            var output = await AiCli.RunAsync("hello from the prompt", Path.GetTempPath(), []);
            Assert.Equal("hello from the prompt", output);
        }
        finally { AiCli.Engine = AiEngine.Claude; }
    }

    [Fact]
    public async Task Copilot_engine_runs_a_cmd_shim_with_a_quoted_instruction()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aicli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // A stand-in for copilot.cmd: prints the -p instruction it was given.
        File.WriteAllText(Path.Combine(dir, "copilot.cmd"), "@echo off\r\necho %~2\r\n");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + oldPath);
        AiCli.Engine = AiEngine.Copilot;
        try
        {
            var output = await AiCli.RunAsync("line one\nline two", Path.GetTempPath(), []);
            Assert.StartsWith("Read the file \"", output.Replace("\"\"", "\""));
            Assert.Contains("prompt-", output);
        }
        finally
        {
            AiCli.Engine = AiEngine.Claude;
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Directory.Delete(dir, true);
        }
    }
}

public class OwnerTests
{
    [Fact]
    public void Owner_round_trips_in_the_file()
    {
        var doc = BacklogMarkdown.Parse("- [ ] [Owner:Alex Kim] Review the PR\n");
        Assert.Equal("Alex Kim", doc.Focus[0].Owner);
        doc.Focus[0].Owner = null;
        Assert.Equal("- [ ] Review the PR\n", BacklogMarkdown.Serialize(doc));
        doc.Focus[0].Owner = "Sam";
        Assert.Contains("[Owner:Sam] Review the PR", BacklogMarkdown.Serialize(doc));
    }
}

public class AiOutputTests
{
    [Fact]
    public void Strips_tool_logs_colors_and_stats()
    {
        var raw = "\u001b[32m●\u001b[0m Read prompt-1.md\n  └ 12 lines read\n● Fetching web page https://example.com\n**Summary**\n\nThe answer.\n\n- point one\n\nTotal usage est: 1 Premium request\nTotal duration (API): 12s\nUsage by model: ...";
        Assert.Equal("**Summary**\n\nThe answer.\n\n- point one", AiCli.CleanOutput(raw));
    }
}
