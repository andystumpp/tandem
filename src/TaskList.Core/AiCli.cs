using System.Diagnostics;
using System.Text;

namespace TaskList.Core;

public enum AiEngine { Claude, Copilot, Custom }

/// <summary>
/// The app's single door to an AI agent: research and the news ticker both call <see cref="RunAsync"/>,
/// which runs the engine picked in settings as a hidden command-line process.
/// <list type="bullet">
/// <item>Claude Code: <c>claude -p</c>, prompt on stdin.</item>
/// <item>GitHub Copilot CLI: the prompt is written to a file and
/// the agent is asked to read and follow it, which avoids quoting long multi-line prompts on a command line.
/// Shell and file-writing tools are denied.</item>
/// <item>Custom: any command; <c>{prompt_file}</c> or <c>{prompt}</c> is filled in, otherwise the prompt
/// goes to stdin.</item>
/// </list>
/// </summary>
public static partial class AiCli
{
    public static AiEngine Engine { get; set; } = AiEngine.Claude;
    public static string CustomCommand { get; set; } = "";

    public static string DisplayName => Engine switch
    {
        AiEngine.Claude => "Claude",
        AiEngine.Copilot => "Copilot",
        _ => "your AI tool",
    };

    /// <param name="claudeTools">Tool names to allow when the engine is Claude (e.g. WebSearch).</param>
    public static async Task<string> RunAsync(string prompt, string workingDirectory, IEnumerable<string> claudeTools,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        CleanOutput(await RunRawAsync(prompt, workingDirectory, claudeTools, timeout, cancellationToken));

    private static Task<string> RunRawAsync(string prompt, string workingDirectory, IEnumerable<string> claudeTools,
        TimeSpan? timeout, CancellationToken cancellationToken) => Engine switch
    {
        AiEngine.Claude => ClaudeCli.RunAsync(prompt, workingDirectory, claudeTools, timeout, cancellationToken),
        AiEngine.Copilot => RunCopilotAsync([], "copilot", prompt, workingDirectory, timeout, cancellationToken),
        _ => RunCustomAsync(prompt, workingDirectory, timeout, cancellationToken),
    };

    [System.Text.RegularExpressions.GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial System.Text.RegularExpressions.Regex Ansi();

    private static readonly string[] ToolLogPrefixes = ["●", "✓", "✗", "⎿", "└", "│", "$ ", "→ ", "Running tool", "Tool call", "Calling tool"];
    private static readonly string[] StatsPrefixes = ["Total usage est", "Total duration", "Total code changes", "Usage by model", "API time spent", "Session time", "Total session time"];

    /// <summary>
    /// Agent CLIs (Copilot in particular) print tool activity and usage stats around the answer. This keeps
    /// just the answer: color codes, tool-log lines and the trailing stats block are dropped.
    /// </summary>
    public static string CleanOutput(string output)
    {
        var lines = Ansi().Replace(output, "").Replace("\r\n", "\n").Split('\n').ToList();
        int stats = lines.FindIndex(l => StatsPrefixes.Any(p => l.TrimStart().StartsWith(p, StringComparison.OrdinalIgnoreCase)));
        if (stats >= 0) lines = lines.Take(stats).ToList();
        var kept = lines.Where(l => !ToolLogPrefixes.Any(p => l.TrimStart().StartsWith(p, StringComparison.Ordinal))).ToList();
        return string.Join("\n", kept).Trim();
    }

    private static async Task<string> RunCopilotAsync(string[] prefix, string exe, string prompt, string workingDirectory,
        TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var file = WritePromptFile(prompt);
        try
        {
            var args = new List<string>(prefix)
            {
                "-p",
                $"Read the file \"{file}\" and do exactly what it asks. Reply with only your answer, no preamble.",
                "--allow-all-tools",
                "--deny-tool", "shell",
                "--deny-tool", "write",
            };
            return await RunProcessAsync(exe, args, stdin: null, workingDirectory, timeout, cancellationToken);
        }
        finally
        {
            TryDelete(file);
        }
    }

    private static async Task<string> RunCustomAsync(string prompt, string workingDirectory, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var parts = SplitCommandLine(CustomCommand);
        if (parts.Count == 0) throw new InvalidOperationException("Set a custom AI command in settings first.");
        var target = Resolve(parts[0]) ?? parts[0];
        if (CustomCommand.Contains("{prompt}") && (target.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // cmd.exe would read characters like & | % inside a task as commands; a file is safe.
            throw new InvalidOperationException($"{parts[0]} is a .cmd script: use {{prompt_file}} instead of {{prompt}}.");
        }
        string? file = null;
        bool usesStdin = !CustomCommand.Contains("{prompt}") && !CustomCommand.Contains("{prompt_file}");
        if (CustomCommand.Contains("{prompt_file}")) file = WritePromptFile(prompt);
        try
        {
            var args = parts.Skip(1)
                .Select(a => a.Replace("{prompt_file}", file ?? "").Replace("{prompt}", prompt))
                .ToList();
            return await RunProcessAsync(parts[0], args, usesStdin ? prompt : null, workingDirectory, timeout, cancellationToken);
        }
        finally
        {
            if (file != null) TryDelete(file);
        }
    }

    private static string WritePromptFile(string prompt)
    {
        var folder = Path.Combine(Path.GetDirectoryName(Preferences.DefaultPath)!, "prompts");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"prompt-{Guid.NewGuid():N}.md");
        File.WriteAllText(file, prompt, new UTF8Encoding(false));
        return file;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Runs a command found on PATH; npm-style .cmd shims are started through cmd.exe.</summary>
    internal static async Task<string> RunProcessAsync(string command, IReadOnlyList<string> args, string? stdin,
        string workingDirectory, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var resolved = Resolve(command) ?? throw new System.ComponentModel.Win32Exception($"'{command}' was not found on PATH.");
        ProcessStartInfo psi;
        if (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // cmd.exe needs the raw line: /s strips exactly the outer quotes and keeps the rest verbatim.
            psi = new ProcessStartInfo("cmd.exe")
            {
                Arguments = "/d /s /c \"" + string.Join(" ", new[] { resolved }.Concat(args).Select(Quote)) + "\"",
            };
        }
        else
        {
            psi = new ProcessStartInfo(resolved);
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        psi.RedirectStandardInput = stdin != null;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.WorkingDirectory = workingDirectory;

        using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {command}.");
        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }
        var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderr = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (timeoutSource.IsCancellationRequested) throw new InvalidOperationException($"{DisplayName} took too long and was stopped.");
            throw;
        }
        var output = (await stdout).Trim();
        if (process.ExitCode != 0 || output.Length == 0)
        {
            var error = (await stderr).Trim();
            throw new InvalidOperationException(error.Length > 0 ? error : $"{command} exited with code {process.ExitCode}.");
        }
        return output;
    }

    private static string Quote(string arg) =>
        arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '&' || c == '|' || c == '<' || c == '>' || c == '^')
            ? arg
            : "\"" + arg.Replace("\"", "\"\"") + "\"";

    /// <summary>Finds a command on PATH, trying the usual Windows extensions.</summary>
    public static string? Resolve(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command) ? command : null;
        var extensions = Path.HasExtension(command) ? new[] { "" } : new[] { ".exe", ".cmd", ".bat", "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir.Trim(), command + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Splits a command line into parts, honoring double quotes.</summary>
    public static List<string> SplitCommandLine(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        foreach (var c in line)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
