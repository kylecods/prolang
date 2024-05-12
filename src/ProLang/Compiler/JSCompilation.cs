using ProLang.Syntax;

namespace ProLang.Compiler;

public sealed class JSCompilation : Compilation
{
    public override void EmitTree(TextWriter writer)
    {
        var globalHtmlStatements = 
            SyntaxTrees.SelectMany(st => st.Root.Declarations).OfType<HtmlDeclarationSyntax>();

        foreach (var htmlStatement in globalHtmlStatements)
        {
        }
    }
}