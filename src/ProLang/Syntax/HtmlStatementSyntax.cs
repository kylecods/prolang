using System.Collections.Immutable;

namespace ProLang.Syntax;

internal class HtmlStatementSyntax : StatementSyntax
{
    public HtmlStatementSyntax(SyntaxTree syntaxTree,HtmlStartTagSyntax startTag, ImmutableArray<StatementSyntax> statements, HtmlEndTagSyntax endTag)
     : base(syntaxTree)
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