using ProLang.Parse;

namespace ProLang.Syntax;

internal sealed class SyntaxTree
{
    public SyntaxTree(IEnumerable<string> diagnostics, SyntaxNode root, SyntaxToken eofToken)
    {
        Diagnostics = diagnostics.ToArray();
        Root = root;
        EndOfFileToken = eofToken;
    }
    
    public IReadOnlyList<string> Diagnostics { get; }
    
    public SyntaxNode Root { get; }
    
    public SyntaxToken EndOfFileToken { get; }

    public static SyntaxTree Parse(string text)
    {
        var parser = new Parser(text);

        return parser.Parse();
    }
}