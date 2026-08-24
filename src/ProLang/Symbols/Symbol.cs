namespace ProLang.Symbols;

public abstract class Symbol
{
    private protected Symbol(string name)
    {
        Name = name;
    }
    
    public abstract SymbolKind Kind { get; }
    public string Name { get; }

    /// <summary>
    /// What this symbol is for, in prose, as Markdown. Null when nothing documents it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One place, read by everything: hover, the detail beside a completion, and the popup shown
    /// while typing a call all take their text from here, so none of them can be more out of date
    /// than the others.
    /// </para>
    /// <para>
    /// It arrives two ways. A builtin carries it because it is written at the declaration in
    /// <c>BuiltInFunctions</c> — there is nowhere else it could live, since a builtin has no
    /// source file. Everything declared in ProLang gets it from the comment written above the
    /// declaration, which is why the standard library needed no new documentation to be
    /// documented: it was already commented, and nothing was reading the comments.
    /// </para>
    /// </remarks>
    public string? Documentation { get; init; }

    public void WriteTo(TextWriter writer)
    {
        SymbolPrinter.WriteTo(this, writer);
    }

    public override string ToString()
    {
        using (var writer = new StringWriter())
        {
            WriteTo(writer);
            return writer.ToString();
        }
    }
}