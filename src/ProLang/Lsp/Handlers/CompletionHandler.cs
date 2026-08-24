using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using ProLang.Symbols;
using ProLang.Symbols.Modules;
using ProLang.Syntax;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// The completion list, chosen by what the cursor is in the middle of.
/// </summary>
internal static class CompletionHandler
{
    /// <summary>
    /// Every keyword the lexer recognises.
    /// </summary>
    /// <remarks>
    /// Taken from <see cref="SyntaxFacts"/> rather than typed out again, which is how the old
    /// server's list came to be missing <c>struct</c>, <c>enum</c>, <c>as</c> and <c>null</c>.
    /// <c>script</c> is excluded: it belongs to the vestigial HTML mode and is not part of the
    /// language anyone writes.
    /// </remarks>
    private static readonly string[] Keywords =
    [
        "let", "func", "struct", "enum", "import", "return",
        "if", "elif", "else", "while", "for", "to", "break", "continue",
        "true", "false", "null", "as", "void",
    ];

    public static List<CompletionItem> Complete(Analysis analysis, Protocol.Position position)
    {
        var offset = LspConversions.ToOffset(analysis.Text, position);
        var context = CompletionContext.Determine(analysis.Text, offset);

        return context.Kind switch
        {
            CompletionKind.None => [],
            CompletionKind.ImportPath => ImportPaths(analysis, context),
            CompletionKind.Member => Members(analysis, context),
            CompletionKind.Type => Types(analysis),
            _ => Expressions(analysis, context, offset),
        };
    }

    /// <summary>
    /// Every module that can be imported, each with what it is for.
    /// </summary>
    /// <remarks>
    /// This is the standard library made discoverable. Until now nothing in the editor knew the
    /// library existed: the old server refused to resolve any import path that did not end in
    /// <c>.prl</c>, which is every one of them.
    /// </remarks>
    private static List<CompletionItem> ImportPaths(Analysis analysis, CompletionContext context)
    {
        var items = new List<CompletionItem>();

        foreach (var (path, summary, isBuiltIn) in ModuleDocumentation.Available(analysis))
        {
            items.Add(new CompletionItem
            {
                Label = path,
                Kind = Protocol.CompletionItemKind.Module,
                Detail = isBuiltIn ? "built-in module" : "standard library",
                Documentation = string.IsNullOrEmpty(summary)
                    ? null
                    : new MarkupContent { Value = summary },

                // Built-in modules first: they are the ones a program is likelier to need, and
                // there are only seven of them against twenty-five library files.
                SortText = (isBuiltIn ? "0" : "1") + path,
            });
        }

        items.Add(new CompletionItem
        {
            Label = "dotnet:",
            Kind = Protocol.CompletionItemKind.Module,
            Detail = ".NET namespace",
            Documentation = new MarkupContent
            {
                Value = "Makes the public types of a .NET namespace available, e.g. `dotnet:System.Text.Json`.",
            },
            SortText = "2dotnet",
        });

        items.Add(new CompletionItem
        {
            Label = "assembly:",
            Kind = Protocol.CompletionItemKind.Module,
            Detail = ".NET assembly",
            Documentation = new MarkupContent
            {
                Value = "References a .NET assembly by name, `.dll` path or `.csproj` path.",
            },
            SortText = "2assembly",
        });

        return items;
    }

    /// <summary>What the expression to the left of the dot actually has.</summary>
    private static List<CompletionItem> Members(Analysis analysis, CompletionContext context)
    {
        var model = analysis.Model;

        if (model == null)
        {
            return [];
        }

        var type = model.TypeOfExpression(analysis.Text.FileName, context.ReceiverSpan);

        // `Colour.` is not an expression — the name on the left is a type, not a value — so the
        // type map has nothing for it and the enum has to be looked up by name.
        if (type == null && context.ReceiverName != null)
        {
            var enumType = analysis.Compilation?.GetSymbols()
                .OfType<EnumSymbol>()
                .FirstOrDefault(e => e.Name == context.ReceiverName);

            if (enumType != null)
            {
                return enumType.Members.Select(m => new CompletionItem
                {
                    Label = m.Name,
                    Kind = Protocol.CompletionItemKind.EnumMember,
                    Detail = $"{enumType.Name}.{m.Name} = {m.Value}",
                }).ToList();
            }
        }

        return type switch
        {
            StructSymbol structType => structType.Fields.Select(f => new CompletionItem
            {
                Label = f.Name,
                Kind = Protocol.CompletionItemKind.Field,
                Detail = $"{f.Name}: {f.Type}",
            }).ToList(),

            EnumSymbol enumType => enumType.Members.Select(m => new CompletionItem
            {
                Label = m.Name,
                Kind = Protocol.CompletionItemKind.EnumMember,
                Detail = $"{enumType.Name}.{m.Name} = {m.Value}",
            }).ToList(),

            not null when type == TypeSymbol.String => IntrinsicMethods(
            [
                BuiltInFunctions.StringLength,
                BuiltInFunctions.StringCharAt,
                BuiltInFunctions.StringCharCode,
                BuiltInFunctions.StringSubstring,
                BuiltInFunctions.StringIndexOf,
            ]),

            not null when type.Name == "array" => IntrinsicMethods([BuiltInFunctions.ArrayLength]),

            _ => [],
        };
    }

    /// <summary>
    /// A method written on a receiver, so its first parameter is the receiver and not an argument.
    /// </summary>
    private static List<CompletionItem> IntrinsicMethods(FunctionSymbol[] methods) =>
        methods.Select(m => new CompletionItem
        {
            Label = m.Name,
            Kind = Protocol.CompletionItemKind.Method,
            Detail = $"{m.Name}({string.Join(", ", m.Parameters.Skip(1).Select(p => $"{p.Name}: {p.Type}"))}): {m.Type}",
            Documentation = string.IsNullOrEmpty(m.Documentation)
                ? null
                : new MarkupContent { Value = m.Documentation },
            InsertText = m.Name,
        }).ToList();

    private static List<CompletionItem> Types(Analysis analysis)
    {
        var items = TypeSymbol.Primitives
            .Where(p => p.Key != "void")
            .Select(p => new CompletionItem
            {
                Label = p.Key,
                Kind = Protocol.CompletionItemKind.TypeParameter,
                Detail = "built-in type",
                SortText = "1" + p.Key,
            })
            .ToList();

        if (analysis.Compilation != null)
        {
            foreach (var symbol in analysis.Compilation.GetSymbols())
            {
                if (symbol.Kind is not (SymbolKind.Struct or SymbolKind.Enum))
                    continue;

                items.Add(new CompletionItem
                {
                    Label = symbol.Name,
                    Kind = LspConversions.ToCompletionKind(symbol),
                    Detail = symbol.Kind == SymbolKind.Struct ? "struct" : "enum",
                    Documentation = Documentation(symbol),
                    SortText = "0" + symbol.Name,
                });
            }
        }

        return items;
    }

    private static List<CompletionItem> Expressions(Analysis analysis, CompletionContext context, int offset)
    {
        var items = new List<CompletionItem>();
        var model = analysis.Model;

        if (model != null)
        {
            // Named arguments, offered first: at the start of an argument they are almost always
            // what is wanted, and nothing else in the list is specific to this call.
            items.AddRange(NamedArguments(analysis, context));

            foreach (var symbol in model.LookupSymbols(analysis.Text.FileName, offset))
            {
                items.Add(new CompletionItem
                {
                    Label = symbol.Name,
                    Kind = LspConversions.ToCompletionKind(symbol),
                    Detail = LspConversions.Signature(symbol),
                    Documentation = Documentation(symbol),
                    SortText = "1" + symbol.Name,
                });
            }
        }

        items.AddRange(UnimportedBuiltIns(analysis));

        foreach (var keyword in Keywords)
        {
            items.Add(new CompletionItem
            {
                Label = keyword,
                Kind = Protocol.CompletionItemKind.Keyword,
                Detail = "keyword",
                SortText = "3" + keyword,
            });
        }

        return items;
    }

    private static IEnumerable<CompletionItem> NamedArguments(Analysis analysis, CompletionContext context)
    {
        if (context.EnclosingCallName == null || analysis.Compilation == null)
        {
            yield break;
        }

        var function = analysis.Compilation.GetSymbols()
            .OfType<FunctionSymbol>()
            .FirstOrDefault(f => f.Name == context.EnclosingCallName);

        if (function == null)
        {
            yield break;
        }

        foreach (var parameter in function.Parameters)
        {
            yield return new CompletionItem
            {
                Label = parameter.Name + ":",
                Kind = Protocol.CompletionItemKind.Property,
                Detail = $"{parameter.Name}: {parameter.Type}"
                    + (parameter.IsOptional ? $" = {parameter.DefaultValue}" : string.Empty),
                InsertText = parameter.Name + ": ",
                SortText = "0" + parameter.Name,
            };
        }
    }

    /// <summary>
    /// Builtins of modules this file has not imported, offered with the import that would fix it.
    /// </summary>
    /// <remarks>
    /// ProLang gates builtins on imports, so <c>print</c> genuinely is not in scope until
    /// <c>import "io"</c> is written. Hiding it entirely is unhelpful — it is the first function
    /// anybody types — and offering it as though it worked produces a program that does not
    /// compile. Offering it with an edit that adds the import turns the rule into a convenience.
    /// </remarks>
    private static IEnumerable<CompletionItem> UnimportedBuiltIns(Analysis analysis)
    {
        var imported = analysis.Compilation?.ImportedModules;

        foreach (var module in BuiltInModule.GetIntrinsic())
        {
            if (imported?.Contains(module.Name) == true)
                continue;

            foreach (var function in module.Functions)
            {
                yield return new CompletionItem
                {
                    Label = function.Name,
                    Kind = Protocol.CompletionItemKind.Function,
                    Detail = $"{LspConversions.Signature(function)} — requires import \"{module.Name}\"",
                    Documentation = Documentation(function),
                    SortText = "2" + function.Name,
                    AdditionalTextEdits = [ImportEdit(analysis, module.Name)],
                };
            }
        }
    }

    /// <summary>An edit inserting an import, placed with the ones already there.</summary>
    private static TextEdit ImportEdit(Analysis analysis, string moduleName)
    {
        var line = 0;

        foreach (var declaration in analysis.SyntaxTree.Root.Declarations)
        {
            if (declaration is ImportDeclarationSyntax import)
            {
                line = analysis.Text.GetLineIndex(import.Span.Start) + 1;
            }
        }

        return new TextEdit
        {
            Range = new Protocol.Range
            {
                Start = new Protocol.Position { Line = line, Character = 0 },
                End = new Protocol.Position { Line = line, Character = 0 },
            },
            NewText = $"import \"{moduleName}\"\n",
        };
    }

    private static MarkupContent? Documentation(Symbol symbol) =>
        string.IsNullOrWhiteSpace(symbol.Documentation)
            ? null
            : new MarkupContent { Value = symbol.Documentation };
}
