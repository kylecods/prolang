using ProLang.Documentation;
using ProLang.Lsp.Workspace;
using ProLang.Symbols;
using ProLang.Symbols.Modules;
using ProLang.Text;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// What each importable module is, and where it lives.
/// </summary>
/// <remarks>
/// <para>
/// ProLang has two kinds of module and they are documented in two different places. A builtin
/// module — <c>io</c>, <c>math</c>, <c>fs</c> — is a set of compiler intrinsics with no source
/// file, and says what it is through <see cref="BuiltInModule.Summary"/>. A library module is a
/// <c>.prl</c> file under <c>std/</c>, and says what it is in the comment block it opens with.
/// </para>
/// <para>
/// This is what turns "the standard library exists" into something an editor can show: the list of
/// modules to import, a description of each, and the file to open for any of them.
/// </para>
/// </remarks>
internal static class ModuleDocumentation
{
    /// <summary>Everything that can follow <c>import "</c>, with what each one is.</summary>
    public static IEnumerable<(string Path, string Summary, bool IsBuiltIn)> Available(Analysis analysis)
    {
        // Intrinsic only. The registry also holds .NET namespaces registered by whatever
        // `dotnet:` imports have been seen, and those are not modules anyone should be offered
        // here — they exist because some other file asked for them.
        foreach (var module in BuiltInModule.GetIntrinsic().OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            yield return (module.Name, module.Summary, true);
        }

        var root = StdRootOf(analysis);

        if (root == null || !Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.prl", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var name = relative[..^".prl".Length];

            yield return (name, FirstLineOfModuleDoc(file), false);
        }
    }

    /// <summary>Markdown describing one import path, for a hover over it.</summary>
    public static string Describe(Analysis analysis, string path)
    {
        if (path.StartsWith("dotnet:", StringComparison.OrdinalIgnoreCase))
        {
            return $"```prolang\nimport \"{path}\"\n```\n\nThe .NET namespace `{path["dotnet:".Length..]}`, "
                + "with its public types available as ProLang types.";
        }

        if (path.StartsWith("assembly:", StringComparison.OrdinalIgnoreCase))
        {
            return $"```prolang\nimport \"{path}\"\n```\n\nA .NET assembly, referenced by name, path or project file.";
        }

        var header = $"```prolang\nimport \"{path}\"\n```";

        if (BuiltInModule.TryGetModule(path, out var module) && module != null)
        {
            var functions = string.Join(", ", module.Functions.Select(f => $"`{f.Name}`").Distinct());

            return $"{header}\n\n{module.Summary}\n\n**Provides** {functions}";
        }

        if (ResolveToFile(analysis, path) is { } file)
        {
            var documentation = ModuleDoc(file);

            return documentation == null ? header : $"{header}\n\n{documentation}";
        }

        return $"{header}\n\nNo module or file of this name could be found.";
    }

    /// <summary>The file an import resolves to, or null for a builtin module or an unresolvable path.</summary>
    public static string? ResolveToFile(Analysis analysis, string path)
    {
        if (path.Contains(':'))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(analysis.Text.FileName);

        if (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, path));

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var root = StdRootOf(analysis);

        if (root != null)
        {
            var name = path.EndsWith(".prl", StringComparison.OrdinalIgnoreCase) ? path : path + ".prl";
            var candidate = Path.GetFullPath(Path.Combine(root, name));

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>How to refer to a file in prose: a library module by its import path.</summary>
    public static string DisplayName(Analysis analysis, string file)
    {
        var root = StdRootOf(analysis);

        if (root != null && file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            return relative.EndsWith(".prl", StringComparison.OrdinalIgnoreCase)
                ? relative[..^".prl".Length]
                : relative;
        }

        return Path.GetFileName(file);
    }

    private static string? ModuleDoc(string file)
    {
        try
        {
            return DocumentationExtractor.ForModule(SourceText.From(File.ReadAllText(file), file));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string FirstLineOfModuleDoc(string file)
    {
        var documentation = ModuleDoc(file);

        if (string.IsNullOrEmpty(documentation))
        {
            return string.Empty;
        }

        // The first line of a std module's header is its title — "Pixel editor — vector shapes" —
        // which is exactly the length that belongs beside a name in a completion list.
        var newline = documentation.IndexOf('\n');

        return newline < 0 ? documentation : documentation[..newline];
    }

    private static string? StdRootOf(Analysis analysis) => analysis.StdRoot;
}
