using System.Diagnostics;

namespace ProLang.Tests.Infrastructure;

/// <summary>
/// Locates a host C compiler and builds a generated <c>.c</c> file with it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in this repository used to compile the C backend's output. CI ran the transpiler over
/// two programs and checked only that it did not crash — its own comment admits the risk — so a
/// change could emit C that does not compile and every test would stay green. One already did:
/// <c>EmitArrayNew</c> called <c>prl_array_new_&lt;Struct&gt;</c> for struct element types while the
/// runtime defined constructors for the twelve builtin ones only.
/// </para>
/// <para>
/// Absence of a compiler is a skip, not a failure, so a checkout on a machine without one still
/// runs green. <see cref="IsAvailable"/> says which it is.
/// </para>
/// </remarks>
internal static class CToolchain
{
    private static readonly Lazy<Toolchain?> Discovered = new(Discover);

    public static bool IsAvailable => Discovered.Value != null;

    public static string? Description => Discovered.Value?.Description;

    /// <summary>
    /// Compiles <paramref name="sourceFile"/> to an executable beside it.
    /// </summary>
    /// <returns>The executable's path, or null with <paramref name="output"/> explaining why.</returns>
    public static string? TryCompile(string sourceFile, out string output)
    {
        var toolchain = Discovered.Value;

        if (toolchain == null)
        {
            output = "No C compiler was found.";
            return null;
        }

        var directory = Path.GetDirectoryName(sourceFile)!;
        var executable = Path.Combine(directory, Path.GetFileNameWithoutExtension(sourceFile) + ".exe");

        var (exitCode, text) = toolchain.Compile(sourceFile, executable, directory);
        output = text;

        return exitCode == 0 && File.Exists(executable) ? executable : null;
    }

    /// <summary>Runs a built executable and returns its combined output, trimmed per line.</summary>
    public static (int ExitCode, string Output) Run(string executable)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        process.WaitForExit(milliseconds: 60_000);

        return (process.ExitCode, stdout + stderr);
    }

    private sealed record Toolchain(string Description, Func<string, string, string, (int, string)> Compile);

    private static Toolchain? Discover()
    {
        foreach (var name in new[] { "gcc", "clang" })
        {
            if (FindOnPath(name) is { } path)
            {
                return new Toolchain(
                    name,
                    (source, executable, workingDirectory) => RunProcess(
                        path,
                        // -w because the runtime headers are not warning-clean today; the point here
                        // is whether the generated program compiles and answers correctly.
                        $"-std=c99 -w \"{source}\" -o \"{executable}\"",
                        workingDirectory));
            }
        }

        if (FindMsvc() is { } vcvars)
        {
            return new Toolchain(
                "msvc",
                (source, executable, workingDirectory) => RunProcess(
                    "cmd.exe",
                    $"/c \"\"{vcvars}\" >nul 2>&1 && cl /nologo /w /TC \"{source}\" /Fe:\"{executable}\"\"",
                    workingDirectory));
        }

        return null;
    }

    private static string? FindOnPath(string executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");

        if (pathVariable == null)
        {
            return null;
        }

        var candidates = OperatingSystem.IsWindows()
            ? new[] { executableName + ".exe" }
            : [executableName];

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var full = Path.Combine(directory, candidate);

                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }

    /// <summary>Finds <c>vcvars64.bat</c>, which is what puts <c>cl.exe</c> and its headers in scope.</summary>
    private static string? FindMsvc()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var programFiles = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
        };

        foreach (var root in programFiles.Where(p => !string.IsNullOrEmpty(p)))
        {
            var visualStudio = Path.Combine(root!, "Microsoft Visual Studio");

            if (!Directory.Exists(visualStudio))
            {
                continue;
            }

            // Newest first, so a machine with several installs uses the current one.
            var found = Directory
                .EnumerateFiles(visualStudio, "vcvars64.bat", SearchOption.AllDirectories)
                .OrderByDescending(p => p, StringComparer.Ordinal)
                .FirstOrDefault();

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static (int, string) RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        process.WaitForExit(milliseconds: 180_000);

        return (process.ExitCode, stdout + stderr);
    }
}
