using ProLang.Compiler;
using ProLang.Intermediate;
using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Symbols.Modules;
using ProLang.Syntax;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// Everything that answers "what is this, and where else is it".
/// </summary>
/// <remarks>
/// All of it is one query over the occurrences the binder recorded, which is why these are short.
/// The old server implemented the same features by matching identifiers as text, and so could not
/// tell two same-named locals apart, could not follow an import, and offered to rename keywords.
/// </remarks>
internal static class NavigationHandlers
{
    public static Hover? Hover(Analysis analysis, Protocol.Position position)
    {
        var offset = LspConversions.ToOffset(analysis.Text, position);

        // An import path is not a symbol, but it is the thing most worth explaining: it is how
        // someone discovers what a library module is for.
        if (ImportAt(analysis, offset) is { } import)
        {
            return new Hover
            {
                Contents = new MarkupContent { Value = ModuleDocumentation.Describe(analysis, import.Path!) },
                Range = LspConversions.ToRange(analysis.Text, import.PathToken.Span),
            };
        }

        var occurrence = analysis.Model?.OccurrenceAt(analysis.Text.FileName, offset);

        if (occurrence == null)
        {
            return null;
        }

        var symbol = occurrence.Value.Symbol;

        return new Hover
        {
            Contents = new MarkupContent { Value = LspConversions.ToMarkdown(symbol, OriginOf(symbol, analysis)) },
            Range = LspConversions.ToRange(analysis.Text, occurrence.Value.Span),
        };
    }

    public static List<Protocol.Location> Definition(Analysis analysis, Protocol.Position position)
    {
        var offset = LspConversions.ToOffset(analysis.Text, position);

        // Go to definition on an import opens the file it names — which is how the standard
        // library becomes navigable rather than merely mentioned.
        if (ImportAt(analysis, offset) is { } import
            && ModuleDocumentation.ResolveToFile(analysis, import.Path!) is { } file)
        {
            return
            [
                new Protocol.Location
                {
                    Uri = DocumentUri.FromPath(file),
                    Range = new Protocol.Range(),
                },
            ];
        }

        var symbol = analysis.Model?.SymbolAt(analysis.Text.FileName, offset);

        if (symbol == null)
        {
            return [];
        }

        var definition = analysis.Model!.FindDefinition(symbol);

        // A builtin has no definition to go to: it is declared in the compiler, not in a .prl
        // file. Returning nothing is honest — its documentation is on hover instead.
        return definition == null ? [] : [LspConversions.ToLocation(definition.Value)];
    }

    public static List<Protocol.Location> References(Analysis analysis, Protocol.Position position, bool includeDeclaration)
    {
        var model = analysis.Model;
        var offset = LspConversions.ToOffset(analysis.Text, position);
        var symbol = model?.SymbolAt(analysis.Text.FileName, offset);

        if (symbol == null)
        {
            return [];
        }

        var definition = model!.FindDefinition(symbol);

        return model.FindReferences(symbol)
            .Where(location => includeDeclaration
                || definition == null
                || location.Span.Start != definition.Value.Span.Start
                || location.FileName != definition.Value.FileName)
            .Select(LspConversions.ToLocation)
            .ToList();
    }

    public static List<DocumentHighlight> Highlight(Analysis analysis, Protocol.Position position)
    {
        var model = analysis.Model;
        var offset = LspConversions.ToOffset(analysis.Text, position);
        var symbol = model?.SymbolAt(analysis.Text.FileName, offset);

        if (symbol == null)
        {
            return [];
        }

        return model!.FindReferences(symbol)
            .Where(location => string.Equals(location.FileName, analysis.Text.FileName, StringComparison.OrdinalIgnoreCase))
            .Select(location => new DocumentHighlight { Range = LspConversions.ToRange(location) })
            .ToList();
    }

    /// <summary>The range a rename would replace, or a refusal.</summary>
    /// <remarks>
    /// Refusing early is the point of this request: it is what stops the editor opening a rename
    /// box over a keyword or a builtin and only failing once the user has typed a new name.
    /// </remarks>
    public static (Protocol.Range? Range, string? Refusal) PrepareRename(Analysis analysis, Protocol.Position position)
    {
        var model = analysis.Model;
        var offset = LspConversions.ToOffset(analysis.Text, position);
        var occurrence = model?.OccurrenceAt(analysis.Text.FileName, offset);

        if (occurrence == null)
        {
            return (null, "There is nothing to rename here.");
        }

        var symbol = occurrence.Value.Symbol;
        var definition = model!.FindDefinition(symbol);

        if (definition == null)
        {
            return (null, $"'{symbol.Name}' is built into the compiler and cannot be renamed.");
        }

        // Renaming edits every file the symbol appears in, and a compilation reaches beyond the
        // workspace — into an installed standard library, for instance. Editing files the user
        // never opened, in a directory they may not own, is not a rename anyone asked for.
        if (!analysis.IsInWorkspace(definition.Value.FileName))
        {
            return (null,
                $"'{symbol.Name}' is declared in {Path.GetFileName(definition.Value.FileName)}, which is "
                + "outside this workspace. Open the file's project to rename it.");
        }

        return (LspConversions.ToRange(analysis.Text, occurrence.Value.Span), null);
    }

    public static (WorkspaceEdit? Edit, string? Refusal) Rename(Analysis analysis, Protocol.Position position, string newName)
    {
        var (_, refusal) = PrepareRename(analysis, position);

        if (refusal != null)
        {
            return (null, refusal);
        }

        if (!IsValidIdentifier(newName))
        {
            return (null, $"'{newName}' is not a valid ProLang name.");
        }

        var model = analysis.Model!;
        var symbol = model.SymbolAt(analysis.Text.FileName, LspConversions.ToOffset(analysis.Text, position))!;

        var edit = new WorkspaceEdit();

        foreach (var location in model.FindReferences(symbol))
        {
            var uri = DocumentUri.FromPath(location.FileName);

            if (!edit.Changes.TryGetValue(uri, out var edits))
            {
                edits = [];
                edit.Changes[uri] = edits;
            }

            edits.Add(new TextEdit
            {
                Range = LspConversions.ToRange(location),
                NewText = newName,
            });
        }

        return (edit, null);
    }

    /// <summary>Import paths, as links to the files they resolve to.</summary>
    public static List<DocumentLink> DocumentLinks(Analysis analysis)
    {
        var links = new List<DocumentLink>();

        foreach (var declaration in analysis.SyntaxTree.Root.Declarations)
        {
            if (declaration is not ImportDeclarationSyntax import || import.Path == null)
                continue;

            if (ModuleDocumentation.ResolveToFile(analysis, import.Path) is not { } file)
                continue;

            links.Add(new DocumentLink
            {
                Range = LspConversions.ToRange(analysis.Text, import.PathToken.Span),
                Target = DocumentUri.FromPath(file),
                Tooltip = import.Path,
            });
        }

        return links;
    }

    private static ImportDeclarationSyntax? ImportAt(Analysis analysis, int offset)
    {
        foreach (var declaration in analysis.SyntaxTree.Root.Declarations)
        {
            if (declaration is ImportDeclarationSyntax import
                && import.Path != null
                && offset >= import.PathToken.Span.Start
                && offset <= import.PathToken.Span.End)
            {
                return import;
            }
        }

        return null;
    }

    /// <summary>Where a symbol came from, when that is not the file being looked at.</summary>
    private static string? OriginOf(Symbol symbol, Analysis analysis)
    {
        var definition = analysis.Model?.FindDefinition(symbol);

        if (definition == null)
        {
            return symbol is FunctionSymbol function && BuiltInModule.ModuleOf(function) is { } module
                ? $"*Built in — `import \"{module}\"`*"
                : null;
        }

        var file = definition.Value.FileName;

        if (string.Equals(file, analysis.Text.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"*Declared in `{ModuleDocumentation.DisplayName(analysis, file)}`*";
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || (!char.IsLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.IdentifierToken)
        {
            return false;
        }

        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
