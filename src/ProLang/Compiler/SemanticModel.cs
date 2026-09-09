using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Symbols;
using ProLang.Symbols.Modules;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Compiler;

/// <summary>
/// What the compiler knows about a program, asked by position rather than by walking a tree.
/// </summary>
/// <remarks>
/// <para>
/// Every editor navigation feature reduces to one of four questions, and this answers all four:
/// what is at this position, where is it declared, where else is it used, and what is in scope
/// here. It is a view over what the binder recorded while binding — see
/// <see cref="BindingRecorder"/> — so it costs a dictionary build, not a second analysis.
/// </para>
/// <para>
/// Because a compilation is the whole import graph bound together, the answers cross files
/// without anything extra: a call in a program and the declaration it resolves to in
/// <c>std/ui/shape.prl</c> are two occurrences of one symbol.
/// </para>
/// </remarks>
public sealed class SemanticModel
{
    private readonly ProLangCompilation _compilation;
    private readonly BindingRecorder _recorder;

    private readonly Dictionary<string, List<SymbolOccurrence>> _byFile;
    private readonly Dictionary<Symbol, List<SymbolOccurrence>> _bySymbol;

    internal SemanticModel(ProLangCompilation compilation, BindingRecorder recorder)
    {
        _compilation = compilation;
        _recorder = recorder;

        _byFile = new Dictionary<string, List<SymbolOccurrence>>(StringComparer.OrdinalIgnoreCase);

        // Reference equality for everything except TypeSymbol, which compares by name and type
        // arguments. That mixture is deliberate and is what makes both halves right: two locals
        // called `i` in different functions are different symbols, while `array<int>` built twice
        // is one type.
        _bySymbol = new Dictionary<Symbol, List<SymbolOccurrence>>();

        foreach (var occurrence in recorder.Occurrences)
        {
            if (!_byFile.TryGetValue(occurrence.FileName, out var inFile))
            {
                inFile = new List<SymbolOccurrence>();
                _byFile[occurrence.FileName] = inFile;
            }

            inFile.Add(occurrence);

            if (!_bySymbol.TryGetValue(occurrence.Symbol, out var ofSymbol))
            {
                ofSymbol = new List<SymbolOccurrence>();
                _bySymbol[occurrence.Symbol] = ofSymbol;
            }

            ofSymbol.Add(occurrence);
        }

        // Innermost first. Spans nest — a struct's name sits inside its declaration — so a
        // narrower span is always the more specific answer to "what is at this position".
        foreach (var inFile in _byFile.Values)
        {
            inFile.Sort(static (a, b) => a.Span.Length.CompareTo(b.Span.Length));
        }
    }

    /// <summary>The symbol named at <paramref name="position"/>, if any.</summary>
    public Symbol? SymbolAt(string fileName, int position) => OccurrenceAt(fileName, position)?.Symbol;

    /// <summary>The whole occurrence at <paramref name="position"/> — symbol, span and what it is doing.</summary>
    internal SymbolOccurrence? OccurrenceAt(string fileName, int position)
    {
        if (!_byFile.TryGetValue(fileName, out var inFile))
        {
            return null;
        }

        foreach (var occurrence in inFile)
        {
            // Inclusive of the end, so that a cursor resting immediately after a name still
            // selects it. That is where the caret sits when someone finishes typing one.
            if (position >= occurrence.Span.Start && position <= occurrence.Span.End)
            {
                return occurrence;
            }
        }

        return null;
    }

    /// <summary>Every symbol named in one file, innermost span first.</summary>
    /// <remarks>Semantic colouring and document highlighting both want a whole file at once.</remarks>
    internal IReadOnlyList<SymbolOccurrence> OccurrencesIn(string fileName) =>
        _byFile.TryGetValue(fileName, out var occurrences)
            ? occurrences
            : Array.Empty<SymbolOccurrence>();

    /// <summary>Every place <paramref name="symbol"/> is named, declaration included.</summary>
    public ImmutableArray<TextLocation> FindReferences(Symbol symbol)
    {
        if (!_bySymbol.TryGetValue(symbol, out var occurrences))
        {
            return ImmutableArray<TextLocation>.Empty;
        }

        return occurrences.Select(o => o.Location).ToImmutableArray();
    }

    /// <summary>Where <paramref name="symbol"/> is declared, or null if it has no source of its own.</summary>
    /// <remarks>
    /// Null for a builtin, which is the honest answer: <c>print</c> is declared in the compiler,
    /// not in any <c>.prl</c> file, so there is nowhere for "go to definition" to go. Its
    /// documentation is what an editor should show instead.
    /// </remarks>
    public TextLocation? FindDefinition(Symbol symbol)
    {
        if (!_bySymbol.TryGetValue(symbol, out var occurrences))
        {
            return null;
        }

        foreach (var occurrence in occurrences)
        {
            if (occurrence.IsDefinition)
            {
                return occurrence.Location;
            }
        }

        return null;
    }

    /// <summary>The type an expression at <paramref name="span"/> was bound to.</summary>
    /// <remarks>
    /// This is what answers member completion on something that is not a plain name — the type of
    /// <c>getBox(i)</c> or <c>arr[0]</c> — since those have a type but no symbol of their own.
    /// </remarks>
    public TypeSymbol? TypeOfExpression(string fileName, TextSpan span) =>
        _recorder.TryGetExpressionType(fileName, span, out var type) ? type : null;

    /// <summary>
    /// Everything nameable at a position: locals and parameters in scope, then everything global.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Locals are found by span rather than by a retained scope chain. A local is in scope from
    /// where it is declared to the end of the function that declares it, so the declarations
    /// recorded inside the enclosing function that start before the cursor are exactly the ones a
    /// completion list should offer. That over-offers in one case — a variable declared inside an
    /// <c>if</c> block is still listed after the block closes — which is the right way to be
    /// wrong: the name is real, it is spelled correctly, and the binder will say so precisely if
    /// it is used out of scope.
    /// </para>
    /// <para>
    /// Builtins are included only for modules the program actually imported, because that is
    /// exactly what the binder's root scope does. Offering <c>print</c> to a file with no
    /// <c>import "io"</c> would be offering a program that does not compile.
    /// </para>
    /// </remarks>
    public ImmutableArray<Symbol> LookupSymbols(string fileName, int position)
    {
        var symbols = ImmutableArray.CreateBuilder<Symbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var enclosing = EnclosingFunctionSpan(fileName, position);

        if (enclosing != null && _byFile.TryGetValue(fileName, out var inFile))
        {
            foreach (var occurrence in inFile)
            {
                if (!occurrence.IsDefinition || occurrence.Span.Start > position)
                    continue;

                if (occurrence.Symbol.Kind is not (SymbolKind.LocalVariable or SymbolKind.Parameter))
                    continue;

                if (occurrence.Span.Start < enclosing.Value.Start || occurrence.Span.End > enclosing.Value.End)
                    continue;

                if (seen.Add(occurrence.Symbol.Name))
                {
                    symbols.Add(occurrence.Symbol);
                }
            }
        }

        foreach (var symbol in _compilation.GetSymbols())
        {
            if (!IsInScope(symbol))
            {
                continue;
            }

            if (seen.Add(symbol.Name))
            {
                symbols.Add(symbol);
            }
        }

        return symbols.ToImmutable();
    }

    /// <summary>
    /// Whether a symbol is actually usable, as opposed to merely existing.
    /// </summary>
    /// <remarks>
    /// The whole of the difference is import-gating. <c>GetSymbols</c> lists every builtin the
    /// compiler has, because the REPL it was written for imports everything; a program only has
    /// the ones it imported, and offering it the rest is offering code that will not compile.
    /// </remarks>
    private bool IsInScope(Symbol symbol)
    {
        if (symbol is not FunctionSymbol function)
        {
            return true;
        }

        var module = BuiltInModule.ModuleOf(function);

        return module == null || _compilation.ImportedModules.Contains(module);
    }

    /// <summary>The declaration whose body encloses <paramref name="position"/>.</summary>
    public FunctionDeclarationSyntax? EnclosingFunction(string fileName, int position)
    {
        foreach (var tree in _compilation.SyntaxTrees)
        {
            if (!string.Equals(tree.Text.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var declaration in tree.Root.Declarations)
            {
                // An imp block's members are functions too, and they are nested rather than
                // top-level, so a walk over declarations alone would find nothing inside one.
                var candidates = declaration is ImpDeclarationSyntax imp
                    ? imp.Functions.Cast<DeclarationSyntax>()
                    : [declaration];

                foreach (var candidate in candidates)
                {
                    if (candidate is FunctionDeclarationSyntax function
                        && position >= function.Span.Start
                        && position <= function.Span.End)
                    {
                        return function;
                    }
                }
            }
        }

        return null;
    }

    private TextSpan? EnclosingFunctionSpan(string fileName, int position) =>
        EnclosingFunction(fileName, position)?.Span;
}
