using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Intermediate;

internal sealed class BoundUnaryOperator
{
    private BoundUnaryOperator(SyntaxKind syntaxKind, BoundUnaryOperatorKind kind, TypeSymbol operandType)
        : this(syntaxKind, kind, operandType, operandType)
    {
    }

    private BoundUnaryOperator(SyntaxKind syntaxKind, BoundUnaryOperatorKind kind, TypeSymbol operandType, TypeSymbol resultType)
    {
        SyntaxKind = syntaxKind;
        Kind = kind;
        OperandType = operandType;
        Type = resultType;
    }
    
    public SyntaxKind SyntaxKind { get; }
    public BoundUnaryOperatorKind Kind { get; }
    public TypeSymbol OperandType { get; }
    public TypeSymbol Type { get; }
    
    private static readonly BoundUnaryOperator[] Operators =
    [
        new (SyntaxKind.BangToken, BoundUnaryOperatorKind.LogicalNegation, TypeSymbol.Bool),
        new (SyntaxKind.PlusToken, BoundUnaryOperatorKind.Identity, TypeSymbol.Int),
        new (SyntaxKind.MinusToken, BoundUnaryOperatorKind.Negation, TypeSymbol.Int),
        new (SyntaxKind.TildeToken, BoundUnaryOperatorKind.OnesComplement, TypeSymbol.Int),

        new (SyntaxKind.PlusToken, BoundUnaryOperatorKind.Identity, TypeSymbol.Int64),
        new (SyntaxKind.MinusToken, BoundUnaryOperatorKind.Negation, TypeSymbol.Int64),

        new (SyntaxKind.PlusToken, BoundUnaryOperatorKind.Identity, TypeSymbol.Float32),
        new (SyntaxKind.MinusToken, BoundUnaryOperatorKind.Negation, TypeSymbol.Float32),

        new (SyntaxKind.PlusToken, BoundUnaryOperatorKind.Identity, TypeSymbol.Float64),
        new (SyntaxKind.MinusToken, BoundUnaryOperatorKind.Negation, TypeSymbol.Float64),

        new (SyntaxKind.PlusToken, BoundUnaryOperatorKind.Identity, TypeSymbol.Float),
        new (SyntaxKind.MinusToken, BoundUnaryOperatorKind.Negation, TypeSymbol.Float),
    ];

    public static BoundUnaryOperator? Bind(SyntaxKind syntaxKind, TypeSymbol operandType)
    {
        foreach (var op in Operators)
        {
            if (op.SyntaxKind == syntaxKind && op.OperandType == operandType)
                return op;
        }

        return null;
    }
}