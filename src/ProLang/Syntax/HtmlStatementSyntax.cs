using System.Collections.Immutable;

namespace ProLang.Syntax;

internal class HtmlStatementSyntax : StatementSyntax
{
    public HtmlStatementSyntax(HtmlStartTagSyntax startTag, ImmutableArray<StatementSyntax> statements, HtmlEndTagSyntax endTag)
    {
        StartTag = startTag;
        
        Statements = statements;
        
        EndTag = endTag;
    }

    public HtmlStartTagSyntax StartTag { get; }

    public ImmutableArray<StatementSyntax> Statements { get; }
    
    public HtmlEndTagSyntax EndTag { get; }


    public override SyntaxKind Kind => SyntaxKind.HtmlStatement;
    

}