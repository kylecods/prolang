using System.Runtime.InteropServices;
using System.Text;

namespace ProLang.Compiler;

/// <summary>
/// Produces a native launcher executable beside a compiled program.
/// </summary>
/// <remarks>
/// <para>
/// The compiler emits a managed <c>.dll</c> plus a <c>.runtimeconfig.json</c>, which is a complete
/// program but not one anyone can double-click: it has to be started as <c>dotnet app.dll</c>.
/// That is fine for a compiler test and wrong for something being handed to a user, and for a
/// Windows Forms program it is worse than wrong — <c>dotnet</c> is a console application, so
/// starting a GUI that way opens a console window behind the form and keeps it there.
/// </para>
/// <para>
/// The fix is the same one the .NET SDK uses for every <c>dotnet build</c>: take the *apphost*
/// template shipped with the SDK — a tiny native executable whose only job is to locate
/// <c>hostfxr</c> and hand it an assembly name — and patch that name in. Nothing here compiles or
/// links; it is a byte edit of a prebuilt binary.
/// </para>
/// <para>
/// Two edits are made. The assembly name replaces a placeholder string embedded in the template,
/// and for a GUI program the PE subsystem field is switched from console to windows, which is what
/// stops the console window appearing. Both are exactly what
/// <c>Microsoft.NET.HostModel.AppHost.HostWriter</c> does; that package is not referenced because
/// this is the whole of what would be used from it.
/// </para>
/// </remarks>
internal static class AppHost
{
    /// <summary>
    /// The placeholder the template carries where the assembly name belongs.
    /// </summary>
    /// <remarks>
    /// It is the SHA-256 of the string "foobar", chosen by the SDK because a hash cannot occur in
    /// the binary by accident. Finding it is how the write offset is located; there is no header
    /// or table pointing at it.
    /// </remarks>
    private const string AssemblyNamePlaceholder =
        "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2";

    /// <summary>IMAGE_SUBSYSTEM_WINDOWS_GUI, the value that suppresses the console window.</summary>
    private const ushort SubsystemWindowsGui = 2;

    /// <summary>
    /// Writes a launcher for <paramref name="assemblyPath"/> next to it.
    /// </summary>
    /// <param name="assemblyPath">The emitted <c>.dll</c>. Must already exist.</param>
    /// <param name="windowsGui">
    /// True for a program that opens a window, which suppresses the console.
    /// </param>
    /// <param name="executablePath">The launcher that was written, on success.</param>
    /// <param name="error">Why no launcher was written, on failure.</param>
    /// <returns>False if the template could not be found or patched.</returns>
    public static bool TryCreate(string assemblyPath, bool windowsGui, out string executablePath, out string error)
    {
        executablePath = "";
        error = "";

        var templatePath = FindTemplate();

        if (templatePath == null)
        {
            error = "could not find the .NET apphost template. It ships with the SDK, at "
                  + "packs/Microsoft.NETCore.App.Host.<rid>/ or sdk/<version>/AppHostTemplate/. "
                  + "Set DOTNET_ROOT if the SDK is installed somewhere unusual.";
            return false;
        }

        var assemblyName = Path.GetFileName(assemblyPath);
        var nameBytes = Encoding.UTF8.GetBytes(assemblyName);
        var placeholderBytes = Encoding.UTF8.GetBytes(AssemblyNamePlaceholder);

        // The name is written into the placeholder's own bytes and must leave room for the
        // terminator. A longer one would run past the reserved region into whatever follows it.
        if (nameBytes.Length >= placeholderBytes.Length)
        {
            error = $"the assembly name '{assemblyName}' is too long for a launcher "
                  + $"(limit {placeholderBytes.Length - 1} bytes).";
            return false;
        }

        byte[] host;

        try
        {
            host = File.ReadAllBytes(templatePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"could not read the apphost template at '{templatePath}': {e.Message}";
            return false;
        }

        var offset = IndexOf(host, placeholderBytes);

        if (offset < 0)
        {
            error = $"the apphost template at '{templatePath}' does not carry the expected "
                  + "placeholder, so this version of it cannot be patched.";
            return false;
        }

        // Overwrite the placeholder, then blank the rest of it. Leaving the tail in place would
        // append the remainder of the hash to the name the host goes looking for.
        nameBytes.CopyTo(host, offset);
        Array.Clear(host, offset + nameBytes.Length, placeholderBytes.Length - nameBytes.Length);

        if (windowsGui && !TrySetWindowsSubsystem(host, out var subsystemError))
        {
            error = subsystemError;
            return false;
        }

        var destination = Path.ChangeExtension(assemblyPath, ExecutableExtension());

        try
        {
            File.WriteAllBytes(destination, host);
            MakeExecutable(destination);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"could not write the launcher to '{destination}': {e.Message}";
            return false;
        }

        executablePath = destination;
        return true;
    }

    /// <summary>
    /// Switches the PE image from the console subsystem to the windows one.
    /// </summary>
    /// <remarks>
    /// The subsystem is a <c>ushort</c> 68 bytes into the optional header, which itself starts 24
    /// bytes past the PE signature. The offset is the same for PE32 and PE32+ despite their
    /// headers differing in size — the extra eight bytes PE32+ spends on <c>ImageBase</c> are
    /// exactly the eight PE32 spends on <c>BaseOfData</c>.
    /// </remarks>
    private static bool TrySetWindowsSubsystem(byte[] image, out string error)
    {
        error = "";

        const int PeOffsetLocation = 0x3C;

        if (image.Length < PeOffsetLocation + 4)
        {
            error = "the apphost template is too small to be a PE image.";
            return false;
        }

        var peOffset = BitConverter.ToInt32(image, PeOffsetLocation);
        var subsystemOffset = peOffset + 24 + 68;

        if (peOffset <= 0 || subsystemOffset + 2 > image.Length)
        {
            error = "the apphost template's PE header is not where it should be.";
            return false;
        }

        if (image[peOffset] != (byte)'P' || image[peOffset + 1] != (byte)'E'
            || image[peOffset + 2] != 0 || image[peOffset + 3] != 0)
        {
            error = "the apphost template does not start with a PE signature.";
            return false;
        }

        BitConverter.TryWriteBytes(image.AsSpan(subsystemOffset), SubsystemWindowsGui);
        return true;
    }

    /// <summary>
    /// Finds an apphost template belonging to the running .NET installation.
    /// </summary>
    /// <remarks>
    /// Two places are searched, newest version first. The runtime *pack* is the one the SDK itself
    /// uses and is specific to the architecture being built for; the SDK's own
    /// <c>AppHostTemplate</c> is the fallback, and exists even in installations that carry no
    /// packs.
    /// </remarks>
    private static string? FindTemplate()
    {
        var root = FindDotnetRoot();

        if (root == null)
        {
            return null;
        }

        var rid = RuntimeInformation.RuntimeIdentifier;
        var hostFileName = ExecutableName("apphost");

        var packDirectory = Path.Combine(root, "packs", $"Microsoft.NETCore.App.Host.{rid}");

        foreach (var version in NewestFirst(packDirectory))
        {
            var candidate = Path.Combine(version, "runtimes", rid, "native", hostFileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var version in NewestFirst(Path.Combine(root, "sdk")))
        {
            var candidate = Path.Combine(version, "AppHostTemplate", hostFileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Subdirectories ordered so that the highest version number comes first.
    /// </summary>
    /// <remarks>
    /// Ordered by parsed version rather than by name, because an ordinal sort puts "10.0.100"
    /// before "9.0.100" — which is how a machine with both installed would silently build against
    /// the older one.
    /// </remarks>
    private static IEnumerable<string> NewestFirst(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(directory)
            .Select(path => (Path: path, Version: ParseVersion(Path.GetFileName(path))))
            .OrderByDescending(entry => entry.Version)
            .Select(entry => entry.Path);
    }

    /// <summary>A directory's version, or 0.0 for a name that is not one.</summary>
    /// <remarks>
    /// Preview directories carry a suffix — <c>10.0.100-rc.1.25451.107</c> — which
    /// <see cref="Version.TryParse"/> rejects outright, so the suffix is cut before parsing.
    /// </remarks>
    private static Version ParseVersion(string name)
    {
        var dash = name.IndexOf('-', StringComparison.Ordinal);
        var numeric = dash < 0 ? name : name[..dash];

        return Version.TryParse(numeric, out var version) ? version : new Version(0, 0);
    }

    /// <summary>
    /// The .NET installation directory.
    /// </summary>
    /// <remarks>
    /// <c>DOTNET_ROOT</c> wins when it is set, which is how a private or side-by-side install says
    /// where it is. Otherwise it is derived from the running runtime's own location, which is
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c> — three levels down.
    /// </remarks>
    private static string? FindDotnetRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);

        if (string.IsNullOrEmpty(runtimeDirectory))
        {
            return null;
        }

        var root = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));

        return Directory.Exists(root) ? root : null;
    }

    private static string ExecutableExtension()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

    private static string ExecutableName(string stem)
        => stem + ExecutableExtension();

    /// <summary>Sets the executable bit, which Windows does not have and Unix will not run without.</summary>
    private static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>First index of <paramref name="needle"/> in <paramref name="haystack"/>, or -1.</summary>
    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        var span = haystack.AsSpan();
        var index = span.IndexOf(needle);

        return index;
    }
}
