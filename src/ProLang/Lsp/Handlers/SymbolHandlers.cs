using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// The document outline and the workspace symbol search.
/// </summary>
/// <remarks>
/// Built from the syntax tree alone, with no binding, so both keep working in a file that does not
/// compile — which is exactly when an outline is most useful for finding your way back to the
/// thing you broke.
/// </remarks>
internal static class SymbolHandlers
{
    public static List<Protocol.DocumentSymbol> DocumentSymbols(Analysis analysis)
    {
        var symbols = new List<Protocol.DocumentSymbol>();
        var text = analysis.Text;

        foreach (var declaration in analysis.SyntaxTree.Root.Declarations)
        {
            switch (declaration)
            {
                case FunctionDeclarationSyntax function:
                    symbols.Add(new Protocol.DocumentSymbol
                    {
                        Name = function.Identifier.Text,
                        Detail = Signature(function),
                        Kind = SymbolKinds.Function,
                        Range = LspConversions.ToRange(text, function.Span),
                        SelectionRange = LspConversions.ToRange(text, function.Identifier.Span),
                    });
                    break;

                case StructDeclarationSyntax structure:
                    symbols.Add(new Protocol.DocumentSymbol
                    {
                        Name = structure.Identifier.Text,
                        Detail = "struct",
                        Kind = SymbolKinds.Struct,
                        Range = LspConversions.ToRange(text, structure.Span),
                        SelectionRange = LspConversions.ToRange(text, structure.Identifier.Span),

                        // Nested, so the outline shows a struct's shape rather than a flat list in
                        // which a field is indistinguishable from a top-level declaration.
                        Children = structure.Fields.Select(field => new Protocol.DocumentSymbol
                        {
                            Name = field.Identifier.Text,
                            Detail = field.Type?.ToString(),
                            Kind = SymbolKinds.Field,
                            Range = LspConversions.ToRange(text, field.Span),
                            SelectionRange = LspConversions.ToRange(text, field.Identifier.Span),
                        }).ToList(),
                    });
                    break;

                case ImpDeclarationSyntax imp:
                    symbols.Add(new Protocol.DocumentSymbol
                    {
                        Name = imp.Identifier.Text,
                        Detail = "imp",
                        Kind = SymbolKinds.Struct,
                        Range = LspConversions.ToRange(text, imp.Span),
                        SelectionRange = LspConversions.ToRange(text, imp.Identifier.Span),

                        // Nested for the same reason a struct's fields are: the outline should show
                        // which type a member belongs to, not a flat list of bare names.
                        Children = imp.Functions.Select(member => new Protocol.DocumentSymbol
                        {
                            Name = member.Identifier.Text,
                            Detail = Signature(member),
                            Kind = SymbolKinds.Method,
                            Range = LspConversions.ToRange(text, member.Span),
                            SelectionRange = LspConversions.ToRange(text, member.Identifier.Span),
                        }).ToList(),
                    });
                    break;

                case EnumDeclarationSyntax enumeration:
                    symbols.Add(new Protocol.DocumentSymbol
                    {
                        Name = enumeration.Identifier.Text,
                        Detail = "enum",
                        Kind = SymbolKinds.Enum,
                        Range = LspConversions.ToRange(text, enumeration.Span),
                        SelectionRange = LspConversions.ToRange(text, enumeration.Identifier.Span),
                        Children = enumeration.Members.Select(member => new Protocol.DocumentSymbol
                        {
                            Name = member.Identifier.Text,
                            Kind = SymbolKinds.EnumMember,
                            Range = LspConversions.ToRange(text, member.Span),
                            SelectionRange = LspConversions.ToRange(text, member.Identifier.Span),
                        }).ToList(),
                    });
                    break;
            }
        }

        return symbols;
    }

    /// <summary>
    /// Every declaration in the workspace whose name matches, plus the standard library.
    /// </summary>
    /// <remarks>
    /// Files are read and parsed rather than bound. The old server searched only files that
    /// happened to be open, which made the feature useless for its actual purpose: finding
    /// something you have not opened yet.
    /// </remarks>
    public static List<SymbolInformation> WorkspaceSymbols(
        string query, IEnumerable<string> roots, IEnumerable<string> includeFiles)
    {
        var results = new List<SymbolInformation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Files(roots).Concat(includeFiles))
        {
            if (!seen.Add(file))
                continue;

            string content;

            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            var text = SourceText.From(content, file);
            var tree = SyntaxTree.Parse(text);

            foreach (var declaration in tree.Root.Declarations)
            {
                var (name, kind, token) = Describe(declaration);

                if (name == null || !Matches(name, query))
                    continue;

                results.Add(new SymbolInformation
                {
                    Name = name,
                    Kind = kind,
                    Location = new Protocol.Location
                    {
                        Uri = DocumentUri.FromPath(file),
                        Range = LspConversions.ToRange(text, token.Span),
                    },
                    ContainerName = Path.GetFileName(file),
                });

                if (results.Count >= 512)
                {
                    return results;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Directories that hold copies of source rather than source.
    /// </summary>
    /// <remarks>
    /// Not an optimisation. <c>ProLang.csproj</c> copies the whole standard library into its build
    /// output, so a workspace that is the ProLang repository contains three copies of every
    /// library file — the real one, the one under <c>src/ProLang/bin/</c>, and the one under
    /// <c>src/ProLang.Tests/bin/</c>. Searching for a library function found each of them and
    /// offered the user a choice between three identical results, two of which open a file that
    /// is overwritten by the next build.
    /// </remarks>
    private static readonly string[] IgnoredDirectories = ["bin", "obj", ".git", "node_modules", "artifacts"];

    private static IEnumerable<string> Files(IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var file in FilesUnder(root))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> FilesUnder(string directory)
    {
        string[] files;
        string[] subdirectories;

        try
        {
            files = Directory.GetFiles(directory, "*.prl");
            subdirectories = Directory.GetDirectories(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }

        foreach (var subdirectory in subdirectories)
        {
            var name = Path.GetFileName(subdirectory);

            if (IgnoredDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            foreach (var file in FilesUnder(subdirectory))
            {
                yield return file;
            }
        }
    }

    private static (string? Name, int Kind, SyntaxToken Token) Describe(DeclarationSyntax declaration) =>
        declaration switch
        {
            FunctionDeclarationSyntax f => (f.Identifier.Text, SymbolKinds.Function, f.Identifier),
            StructDeclarationSyntax s => (s.Identifier.Text, SymbolKinds.Struct, s.Identifier),
            EnumDeclarationSyntax e => (e.Identifier.Text, SymbolKinds.Enum, e.Identifier),
            _ => (null, 0, null!),
        };

    /// <summary>An empty query matches everything, which is what the editor asks for first.</summary>
    private static bool Matches(string name, string query) =>
        query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string Signature(FunctionDeclarationSyntax function)
    {
        var parameters = string.Join(", ", function.Parameters.Select(p => p.Identifier.Text));

        return $"({parameters})";
    }
}
