using System.Diagnostics;
using System.Text;

using ProLang.Compiler;
using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Emit;

/// <summary>
/// Covers the native launcher written beside a compiled program by <c>--apphost</c>.
/// </summary>
/// <remarks>
/// <para>
/// The launcher is a byte-patched copy of the apphost template that ships with the .NET SDK.
/// Patching a prebuilt binary is the kind of thing that keeps working until an SDK update moves
/// something, and then fails in a way no other test would notice: the compiler still emits a
/// perfectly good assembly, and only the packaging step is broken. So the test that matters is the
/// end-to-end one — build a program, run the launcher, read what it printed.
/// </para>
/// <para>
/// If the template cannot be found the tests pass without asserting. That is a deliberate choice
/// for one specific case: a machine with the .NET *runtime* but no SDK can run this suite and has
/// no template to find. It is not a way of tolerating a broken patcher, which would report a
/// template it found and could not use.
/// </para>
/// </remarks>
public sealed class AppHostTests
{
    private const string HelloSource = """
        import "io"

        func main()
        {
            print("hello from a launcher")
        }
        """;

    [Fact]
    public void Launcher_RunsTheProgramWithoutDotnet()
    {
        using var scratch = ScratchDirectory.Create("apphost-run");

        var assembly = CompileHello(scratch);

        if (!AppHost.TryCreate(assembly, windowsGui: false, out var launcher, out var error))
        {
            Assert.Contains("could not find the .NET apphost template", error);
            return;
        }

        Assert.True(File.Exists(launcher), $"no launcher at {launcher}");

        var result = RunDirectly(launcher);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardError);
        Assert.Contains("hello from a launcher", result.StandardOutput);
    }

    [Fact]
    public void Launcher_IsNamedAfterTheAssembly()
    {
        using var scratch = ScratchDirectory.Create("apphost-name");

        var assembly = CompileHello(scratch);

        if (!AppHost.TryCreate(assembly, windowsGui: false, out var launcher, out _))
        {
            return;
        }

        Assert.Equal(Path.ChangeExtension(assembly, ".exe"), launcher);
    }

    /// <summary>
    /// A console program keeps the console subsystem; a windowed one does not.
    /// </summary>
    /// <remarks>
    /// This is the field that decides whether a Windows Forms program opens a console window
    /// behind its form. It cannot be observed from a headless test any other way, and getting it
    /// wrong is not a crash — just a stray black rectangle nobody asked for.
    /// </remarks>
    [Theory]
    [InlineData(false, 3)] // IMAGE_SUBSYSTEM_WINDOWS_CUI
    [InlineData(true, 2)]  // IMAGE_SUBSYSTEM_WINDOWS_GUI
    public void Launcher_RecordsTheRequestedSubsystem(bool windowsGui, int expected)
    {
        using var scratch = ScratchDirectory.Create("apphost-subsystem");

        var assembly = CompileHello(scratch);

        if (!AppHost.TryCreate(assembly, windowsGui, out var launcher, out _))
        {
            return;
        }

        Assert.Equal(expected, ReadSubsystem(launcher));
    }

    /// <summary>
    /// The placeholder the template carries must be gone, and the assembly name in its place.
    /// </summary>
    /// <remarks>
    /// Written as its own check because a launcher that still carried the placeholder would fail
    /// at run time with "The application to execute does not exist", which says nothing about why.
    /// </remarks>
    [Fact]
    public void Launcher_NoLongerCarriesThePlaceholder()
    {
        using var scratch = ScratchDirectory.Create("apphost-placeholder");

        var assembly = CompileHello(scratch);

        if (!AppHost.TryCreate(assembly, windowsGui: false, out var launcher, out _))
        {
            return;
        }

        var bytes = File.ReadAllBytes(launcher);
        var placeholder = Encoding.UTF8.GetBytes("c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2");
        var assemblyName = Encoding.UTF8.GetBytes(Path.GetFileName(assembly));

        Assert.True(bytes.AsSpan().IndexOf(placeholder) < 0, "the launcher still carries the placeholder");
        Assert.True(bytes.AsSpan().IndexOf(assemblyName) >= 0, "the launcher does not name the assembly");
    }

    /// <summary>An assembly name too long for the reserved space is refused, not truncated.</summary>
    [Fact]
    public void Launcher_RefusesAnOverlongAssemblyName()
    {
        using var scratch = ScratchDirectory.Create("apphost-overlong");

        var assembly = CompileHello(scratch);
        var overlong = Path.Combine(scratch.Path, new string('n', 80) + ".dll");
        File.Copy(assembly, overlong);

        var created = AppHost.TryCreate(overlong, windowsGui: false, out _, out var error);

        // Only meaningful where a template exists; elsewhere the missing-template message wins.
        if (error.Contains("could not find the .NET apphost template", StringComparison.Ordinal))
        {
            return;
        }

        Assert.False(created);
        Assert.Contains("too long", error, StringComparison.Ordinal);
    }

    private static string CompileHello(ScratchDirectory scratch)
    {
        var source = scratch.File("hello.prl");
        File.WriteAllText(source, HelloSource);

        var result = CompilerHarness.CompileToFile(scratch.Path, source);

        Assert.True(result.Succeeded, result.DiagnosticText);

        return result.AssemblyPath!;
    }

    /// <summary>Runs an executable as itself, which is the whole point of having one.</summary>
    private static (int ExitCode, string StandardOutput, string StandardError) RunDirectly(string executablePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start '{executablePath}'");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        process.WaitForExit(milliseconds: 30_000);

        return (process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// The PE subsystem field: 24 bytes past the PE signature for the optional header, then 68
    /// more. The offset is the same for PE32 and PE32+.
    /// </summary>
    private static int ReadSubsystem(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var peOffset = BitConverter.ToInt32(bytes, 0x3C);

        return BitConverter.ToUInt16(bytes, peOffset + 24 + 68);
    }
}
