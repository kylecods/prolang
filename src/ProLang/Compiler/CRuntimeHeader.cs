using System.Reflection;

namespace ProLang.Compiler;

/// <summary>
/// Exposes the ProLang C runtime headers (embedded resources from the native/ folder)
/// so the C/PSP emitters can write them out next to the generated program.
/// The runtime is split into modular headers: base.h, prl_types.h, prl_array.h,
/// prl_string.h, prl_io.h, prl_console.h, prl_psp.h, prolang_runtime.h.
/// </summary>
internal static class CRuntimeHeader
{
    private const string ResourcePrefix = "ProLang.native.";

    private static readonly string[] RuntimeFiles =
    [
        "base.h",
        "arena.h",
        "strings.h",
        "vector.h",
        "prl_memory.h",
        "prl_types.h",
        "prl_array.h",
        "prl_string.h",
        "prl_io.h",
        "prl_console.h",
        "prl_psp.h",
        "prl_test.h",
        "prolang_runtime.h",
    ];

    public static string Content => Load("prolang_runtime.h");

    /// <summary>
    /// Writes every runtime header into <paramref name="outputDir"/> so the generated
    /// program can #include "prolang_runtime.h" and build standalone.
    /// </summary>
    public static void WriteAll(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        foreach (var file in RuntimeFiles)
        {
            File.WriteAllText(Path.Combine(outputDir, file), Load(file));
        }
    }

    private static string Load(string fileName)
    {
        var name = ResourcePrefix + fileName;
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded runtime resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
