using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Intermediate;

internal sealed class BoundBinaryOperator
{
    private BoundBinaryOperator(SyntaxKind syntaxKind, BoundBinaryOperatorKind kind, TypeSymbol type)
         : this(syntaxKind, kind, type, type, type)
        {

        }

        private BoundBinaryOperator(SyntaxKind syntaxKind, BoundBinaryOperatorKind kind, TypeSymbol operandType, TypeSymbol resultType)
         : this(syntaxKind, kind, operandType, operandType, resultType)
        {

        }

        private BoundBinaryOperator(SyntaxKind syntaxKind, BoundBinaryOperatorKind kind, TypeSymbol leftType, TypeSymbol rightType, TypeSymbol resultType)
        {
            SyntaxKind = syntaxKind;
            Kind = kind;
            LeftType = leftType;
            RightType = rightType;
            Type = resultType;
        }

        public SyntaxKind SyntaxKind { get; }
        public BoundBinaryOperatorKind Kind { get; }
        public TypeSymbol LeftType { get; }
        public TypeSymbol RightType { get; }
        public TypeSymbol Type { get; }

        private static BoundBinaryOperator[] _operators =
        {
            new (SyntaxKind.PlusToken, BoundBinaryOperatorKind.Addition, TypeSymbol.Int),
            new (SyntaxKind.MinusToken, BoundBinaryOperatorKind.Subtraction, TypeSymbol.Int),
            new (SyntaxKind.StarToken, BoundBinaryOperatorKind.Multiplication, TypeSymbol.Int),
            new (SyntaxKind.SlashToken, BoundBinaryOperatorKind.Division, TypeSymbol.Int),
            new (SyntaxKind.AmpersandToken,BoundBinaryOperatorKind.BitwiseAnd,TypeSymbol.Int),
            new(SyntaxKind.PipeToken, BoundBinaryOperatorKind.BitwiseOr, TypeSymbol.Int),
            new (SyntaxKind.HatToken, BoundBinaryOperatorKind.BitwiseXor,TypeSymbol.Int),
            
            new (SyntaxKind.EqualsEqualsToken, BoundBinaryOperatorKind.Equals, TypeSymbol.Int,TypeSymbol.Bool),
            new (SyntaxKind.EqualsEqualsToken, BoundBinaryOperatorKind.Equals, TypeSymbol.String,TypeSymbol.Bool),
            new (SyntaxKind.EqualsEqualsToken, BoundBinaryOperatorKind.Equals, TypeSymbol.Bool),

            
            
            new (SyntaxKind.BangEqualsToken, BoundBinaryOperatorKind.NotEquals, TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken, BoundBinaryOperatorKind.NotEquals, TypeSymbol.Int,TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken, BoundBinaryOperatorKind.NotEquals, TypeSymbol.String,TypeSymbol.Bool),
            
            new (SyntaxKind.LessThanToken, BoundBinaryOperatorKind.LessThan, TypeSymbol.Int,TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken, BoundBinaryOperatorKind.LessEqual, TypeSymbol.Int,TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken, BoundBinaryOperatorKind.GreaterThan, TypeSymbol.Int,TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken, BoundBinaryOperatorKind.GreaterEqual, TypeSymbol.Int,TypeSymbol.Bool),
            
            new (SyntaxKind.PercentageToken, BoundBinaryOperatorKind.Modulo, TypeSymbol.Int),

            new (SyntaxKind.AmpersandAmpersandToken, BoundBinaryOperatorKind.LogicalAnd, TypeSymbol.Bool),
            new (SyntaxKind.PipePipeToken, BoundBinaryOperatorKind.LogicalOr, TypeSymbol.Bool),
            
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition,TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition,TypeSymbol.String, TypeSymbol.Any, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.Any, TypeSymbol.String, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.String, TypeSymbol.Int, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.Int, TypeSymbol.String, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.String, TypeSymbol.Bool, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.Bool, TypeSymbol.String, TypeSymbol.String),

            // float32 arithmetic
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.Float32),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.Float32),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.Float32),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.Float32),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.Float32),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Float32, TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Float32, TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.Float32, TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.Float32, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.Float32, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.Float32, TypeSymbol.Bool),

            // float64 arithmetic
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.Float64),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.Float64),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.Float64),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.Float64),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.Float64),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Float64, TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Float64, TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.Float64, TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.Float64, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.Float64, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.Float64, TypeSymbol.Bool),

            // float (alias for float64) arithmetic
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.Float),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.Float),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.Float),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.Float),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.Float),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Float, TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Float, TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.Float, TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.Float, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.Float, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.Float, TypeSymbol.Bool),
        };

        public static BoundBinaryOperator? Bind(SyntaxKind syntaxKind, TypeSymbol leftType, TypeSymbol rightType)
        {
            foreach (var op in _operators)
            {
                if (op.SyntaxKind == syntaxKind && op.LeftType == leftType && op.RightType == rightType)
                {
                    return op;
                }
            }

            return null;
        }
    }
