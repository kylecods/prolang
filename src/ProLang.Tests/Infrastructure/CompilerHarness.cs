using System.Collections.Immutable;
using ProLang.Compiler;
using ProLang.Parse;
using ProLang.Syntax;

namespace ProLang.Tests.Infrastructure;

/// <summary>
/// The result of driving the compiler over one or more ProLang source files.
/// </summary>
/// <param name="Diagnostics">Diagnostics produced by any phase. Empty means success.</param>
/// <param name="AssemblyPath">
/// Path of the emitted assembly, or <see langword="null"/> when emission did not happen.
/// </param>
internal sealed record CompileResult(ImmutableArray<Diagnostic> Diagnostics, string? AssemblyPath)
{
    /// <summary>True when every phase completed without diagnostics.</summary>
    public bool Succeeded => Diagnostics.IsEmpty;

    /// <summary>Diagnostics rendered one per line, for assertion failure messages.</summary>
    public string DiagnosticText =>
        Diagnostics.IsEmpty
            ? "(none)"
            : string.Join(Environment.NewLine, Diagnostics.Select(d => $"  {d.Location.FileName}: {d.Message}"));
}

/// <summary>
/// Drives the compiler in-process for tests.
/// </summary>
/// <remarks>
/// This mirrors what <c>Program.Main</c> does for the default emit path, so that tests exercise
/// the same code the CLI does. It deliberately does not swallow exceptions — an emitter crash
/// should fail a test loudly rather than be reported as a diagnostic.
/// </remarks>
internal static class CompilerHarness
{
    /// <summary>
    /// Compiles <paramref name="sourcePaths"/> to a real assembly inside
    /// <paramref name="outputDirectory"/>.
    /// </summary>
    public static CompileResult CompileToFile(string outputDirectory, params string[] sourcePaths)
    {
        if (sourcePaths.Length == 0)
        {
            throw new ArgumentException("At least one source path is required.", nameof(sourcePaths));
        }

        Directory.CreateDirectory(outputDirectory);

        var moduleName = Path.GetFileNameWithoutExtension(sourcePaths[0]);
        var outputPath = Path.Combine(outputDirectory, moduleName + ".dll");

        var compilation = CreateCompilation(sourcePaths);
        var diagnostics = compilation.Emit(moduleName, [], outputPath);

        return new CompileResult(diagnostics, diagnostics.IsEmpty ? outputPath : null);
    }

    /// <summary>
    /// Compiles <paramref name="sourcePaths"/> into memory. Used where only the metadata matters
    /// and touching the disk would be waste.
    /// </summary>
    public static (ImmutableArray<Diagnostic> Diagnostics, byte[] Assembly) CompileToMemory(params string[] sourcePaths)
    {
        var compilation = CreateCompilation(sourcePaths);
        var program = compilation.GetBoundProgram();

        if (!program.Diagnostics.IsEmpty)
        {
            return (program.Diagnostics, []);
        }

        using var stream = new MemoryStream();
        var moduleName = Path.GetFileNameWithoutExtension(sourcePaths[0]);
        var diagnostics = Emitter.Emit(program, moduleName, [], stream);

        return (diagnostics, diagnostics.IsEmpty ? stream.ToArray() : []);
    }

    /// <summary>
    /// Compiles inline ProLang source text. The text is written to a temp file first because
    /// import resolution is relative to the source file's directory.
    /// </summary>
    public static CompileResult CompileSource(string outputDirectory, string source, string name = "inline")
    {
        Directory.CreateDirectory(outputDirectory);
        var sourcePath = Path.Combine(outputDirectory, name + ".prl");
        File.WriteAllText(sourcePath, source);

        return CompileToFile(outputDirectory, sourcePath);
    }

    private static ProLangCompilation CreateCompilation(string[] sourcePaths)
    {
        var syntaxTrees = new SyntaxTree[sourcePaths.Length];

        for (var i = 0; i < sourcePaths.Length; i++)
        {
            if (!File.Exists(sourcePaths[i]))
            {
                throw new FileNotFoundException($"Test source file not found: {sourcePaths[i]}", sourcePaths[i]);
            }

            syntaxTrees[i] = SyntaxTree.Load(sourcePaths[i]);
        }

        return ProLangCompilation.Create(syntaxTrees);
    }
}
