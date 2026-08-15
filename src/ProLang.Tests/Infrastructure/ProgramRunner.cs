using System.Diagnostics;
using System.Text;

namespace ProLang.Tests.Infrastructure;

/// <summary>The outcome of executing a compiled ProLang assembly.</summary>
/// <param name="ExitCode">Process exit code, or -1 if the process had to be killed on timeout.</param>
/// <param name="StandardOutput">Everything written to stdout.</param>
/// <param name="StandardError">Everything written to stderr.</param>
/// <param name="TimedOut">True when the program was still running when the timeout elapsed.</param>
internal sealed record RunResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    /// <summary>True when the program exited cleanly and produced nothing on stderr.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0 && StandardError.Length == 0;

    /// <summary>A combined description used in assertion failure messages.</summary>
    public string Describe() =>
        $"exit code {(TimedOut ? "(timed out)" : ExitCode.ToString())}" +
        (StandardError.Length > 0 ? $"{Environment.NewLine}stderr:{Environment.NewLine}{StandardError}" : string.Empty);
}

/// <summary>
/// Runs a compiled ProLang assembly in a child <c>dotnet</c> process.
/// </summary>
/// <remarks>
/// Out-of-process rather than <c>Assembly.Load</c> because emitted programs call
/// <c>Console.WriteLine</c>, set console colours and cursor positions, and may throw unhandled
/// exceptions — none of which should be able to disturb the test host.
/// </remarks>
internal static class ProgramRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Executes <paramref name="assemblyPath"/> with stdin closed.</summary>
    public static RunResult Run(string assemblyPath, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
        };
        startInfo.ArgumentList.Add(assemblyPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start 'dotnet {assemblyPath}'.");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Close stdin so anything that reads input sees EOF immediately instead of blocking.
        process.StandardInput.Close();

        if (!process.WaitForExit((int)(timeout ?? DefaultTimeout).TotalMilliseconds))
        {
            TryKill(process);
            return new RunResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true);
        }

        // WaitForExit(int) can return before the async output handlers have drained; the
        // parameterless overload flushes them.
        process.WaitForExit();

        return new RunResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout expiring and the kill.
        }
    }
}
