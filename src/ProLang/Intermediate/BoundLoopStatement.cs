namespace ProLang.Intermediate;

internal abstract class BoundLoopStatement : BoundStatement
{
    protected BoundLoopStatement(BoundLabel breakLabel, BoundLabel continueLabel)
    {
        BreakLabel = breakLabel;
        ContinueLabel = continueLabel;
    }

    public BoundLabel ContinueLabel { get;  }

    public BoundLabel BreakLabel { get; }
}