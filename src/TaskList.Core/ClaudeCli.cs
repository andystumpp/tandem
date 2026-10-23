using System.Diagnostics;
using System.Text;

namespace TaskList.Core;

/// <summary>Runs one-shot prompts through the Claude Code CLI (<c>claude -p</c>) as a hidden background process.</summary>
public static class ClaudeCli
{
    /// <summary>Names of the MCP servers configured for Claude Code (from <c>claude mcp list</c>), read once.</summary>
    private static readonly Lazy<Task<List<string>>> McpServers = new(ListMcpServersAsync);

    private static async Task<List<string>> ListMcpServersAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("claude")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("mcp");
            psi.ArgumentList.Add("list");
            using var process = Process.Start(psi);
            if (process == null) return new();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            output = output.Replace("\r", "");
            // Lines look like "name: command args - ✓ Connected".
            return output.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Contains(':') && !l.StartsWith("Checking", StringComparison.OrdinalIgnoreCase))
                .Select(l => l[..l.IndexOf(':')].Trim())
                // Tool names use the server name with anything but letters, digits, _ and - turned into _
                // ("claude.ai Gmail" -> "claude_ai_Gmail").
                .Select(n => new string(n.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray()))
                .Where(n => n.Length > 0)
                .Distinct()
                .ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new();
        }
    }

    public static async Task<string> RunAsync(string prompt, string workingDirectory, IEnumerable<string> allowedTools,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo("claude")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("text");
        // Your MCP servers stay available to research: each configured server is allowed alongside the
        // requested built-in tools. Shell and file edits are never allowed.
        var tools = allowedTools.ToList();
        if (tools.Count > 0) tools.AddRange((await McpServers.Value).Select(name => "mcp__" + name));
        psi.ArgumentList.Add("--disallowedTools");
        foreach (var tool in new[] { "Bash", "Write", "Edit", "NotebookEdit" }) psi.ArgumentList.Add(tool);
        if (tools.Count > 0)
        {
            psi.ArgumentList.Add("--allowedTools");
            foreach (var tool in tools) psi.ArgumentList.Add(tool);
        }

        using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the claude CLI.");
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderr = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (timeoutSource.IsCancellationRequested) throw new InvalidOperationException("Claude took too long and was stopped.");
            throw;
        }

        var output = (await stdout).Trim();
        if (process.ExitCode != 0 || output.Length == 0)
        {
            var error = (await stderr).Trim();
            throw new InvalidOperationException(error.Length > 0 ? error : $"claude exited with code {process.ExitCode}.");
        }
        return output;
    }
}
