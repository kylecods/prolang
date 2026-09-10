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

        /// <summary>
        /// An <c>==</c> or <c>!=</c> comparing references, built on demand rather than looked up.
        /// </summary>
        /// <remarks>
        /// The table below pairs exact operand types, which works for the primitives it was written
        /// for but cannot express "any reference type, against null" — every class a program declares
        /// would need its own row. The binder decides that a comparison is legal and asks for the
        /// operator here.
        /// </remarks>
        public static BoundBinaryOperator ReferenceEquality(SyntaxKind syntaxKind, TypeSymbol leftType, TypeSymbol rightType)
        {
            var kind = syntaxKind == SyntaxKind.EqualsEqualsToken
                ? BoundBinaryOperatorKind.Equals
                : BoundBinaryOperatorKind.NotEquals;

            return new BoundBinaryOperator(syntaxKind, kind, leftType, rightType, TypeSymbol.Bool);
        }

        private static readonly BoundBinaryOperator[] _operators =
        [
            // ── int (32-bit signed) ──────────────────────────────────────────
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.Int),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.Int),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.Int),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.Int),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.Int),
            new (SyntaxKind.AmpersandToken,       BoundBinaryOperatorKind.BitwiseAnd,     TypeSymbol.Int),
            new (SyntaxKind.PipeToken,            BoundBinaryOperatorKind.BitwiseOr,      TypeSymbol.Int),
            new (SyntaxKind.HatToken,             BoundBinaryOperatorKind.BitwiseXor,     TypeSymbol.Int),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Int,    TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Int,    TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.Int,    TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.Int,    TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.Int,    TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.Int,    TypeSymbol.Bool),

            // ── int8 ─────────────────────────────────────────────────────────
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.Int8),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.Int8),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.Int8),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.Int8),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.Int8),
            new (SyntaxKind.AmpersandToken,       BoundBinaryOperatorKind.BitwiseAnd,     TypeSymbol.Int8),
            new (SyntaxKind.PipeToken,            BoundBinaryOperatorKind.BitwiseOr,      TypeSymbol.Int8),
            new (SyntaxKind.HatToken,             BoundBinaryOperatorKind.BitwiseXor,     TypeSymbol.Int8),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Int8,   TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Int8,   TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.Int8,   TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.Int8,   TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.Int8,   TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.Int8,   TypeSymbol.Bool),

            // ── uint8 ────────────────────────────────────────────────────────
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.UInt8),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.UInt8),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.UInt8),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.UInt8),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.UInt8),
            new (SyntaxKind.AmpersandToken,       BoundBinaryOperatorKind.BitwiseAnd,     TypeSymbol.UInt8),
            new (SyntaxKind.PipeToken,            BoundBinaryOperatorKind.BitwiseOr,      TypeSymbol.UInt8),
            new (SyntaxKind.HatToken,             BoundBinaryOperatorKind.BitwiseXor,     TypeSymbol.UInt8),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.UInt8,  TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.UInt8,  TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.UInt8,  TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.UInt8,  TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.UInt8,  TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.UInt8,  TypeSymbol.Bool),

            // ── int16 ────────────────────────────────────────────────────────
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.Int16),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.Int16),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.Int16),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.Int16),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.Int16),
            new (SyntaxKind.AmpersandToken,       BoundBinaryOperatorKind.BitwiseAnd,     TypeSymbol.Int16),
            new (SyntaxKind.PipeToken,            BoundBinaryOperatorKind.BitwiseOr,      TypeSymbol.Int16),
            new (SyntaxKind.HatToken,             BoundBinaryOperatorKind.BitwiseXor,     TypeSymbol.Int16),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Int16,  TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Int16,  TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.Int16,  TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.Int16,  TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.Int16,  TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.Int16,  TypeSymbol.Bool),

            // ── uint16 ───────────────────────────────────────────────────────
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.UInt16),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.UInt16),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.UInt16),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.UInt16),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.UInt16),
            new (SyntaxKind.AmpersandToken,       BoundBinaryOperatorKind.BitwiseAnd,     TypeSymbol.UInt16),
            new (SyntaxKind.PipeToken,            BoundBinaryOperatorKind.BitwiseOr,      TypeSymbol.UInt16),
            new (SyntaxKind.HatToken,             BoundBinaryOperatorKind.BitwiseXor,     TypeSymbol.UInt16),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.UInt16, TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.UInt16, TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.UInt16, TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.UInt16, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.UInt16, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.UInt16, TypeSymbol.Bool),

            // ── char ─────────────────────────────────────────────────────────
            // Comparisons and equality only. Arithmetic is not table-listed: a char promotes to
            // int through the C-like rank below, so 'a' + 1 is an int without a (char, int) row.
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Char,   TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Char,   TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.Char,   TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.Char,   TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.Char,   TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.Char,   TypeSymbol.Bool),

            // ── uint32 ───────────────────────────────────────────────────────
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.UInt32),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.UInt32),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.UInt32),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.UInt32),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.UInt32),
            new (SyntaxKind.AmpersandToken,       BoundBinaryOperatorKind.BitwiseAnd,     TypeSymbol.UInt32),
            new (SyntaxKind.PipeToken,            BoundBinaryOperatorKind.BitwiseOr,      TypeSymbol.UInt32),
            new (SyntaxKind.HatToken,             BoundBinaryOperatorKind.BitwiseXor,     TypeSymbol.UInt32),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.UInt32, TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.UInt32, TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.UInt32, TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.UInt32, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.UInt32, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.UInt32, TypeSymbol.Bool),

            // ── int64 ────────────────────────────────────────────────────────
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.Int64),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.Int64),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.Int64),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.Int64),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.Int64),
            new (SyntaxKind.AmpersandToken,       BoundBinaryOperatorKind.BitwiseAnd,     TypeSymbol.Int64),
            new (SyntaxKind.PipeToken,            BoundBinaryOperatorKind.BitwiseOr,      TypeSymbol.Int64),
            new (SyntaxKind.HatToken,             BoundBinaryOperatorKind.BitwiseXor,     TypeSymbol.Int64),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Int64,  TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Int64,  TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.Int64,  TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.Int64,  TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.Int64,  TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.Int64,  TypeSymbol.Bool),

            // ── uint64 ───────────────────────────────────────────────────────
            new (SyntaxKind.PlusToken,            BoundBinaryOperatorKind.Addition,       TypeSymbol.UInt64),
            new (SyntaxKind.MinusToken,           BoundBinaryOperatorKind.Subtraction,    TypeSymbol.UInt64),
            new (SyntaxKind.StarToken,            BoundBinaryOperatorKind.Multiplication, TypeSymbol.UInt64),
            new (SyntaxKind.SlashToken,           BoundBinaryOperatorKind.Division,       TypeSymbol.UInt64),
            new (SyntaxKind.PercentageToken,      BoundBinaryOperatorKind.Modulo,         TypeSymbol.UInt64),
            new (SyntaxKind.AmpersandToken,       BoundBinaryOperatorKind.BitwiseAnd,     TypeSymbol.UInt64),
            new (SyntaxKind.PipeToken,            BoundBinaryOperatorKind.BitwiseOr,      TypeSymbol.UInt64),
            new (SyntaxKind.HatToken,             BoundBinaryOperatorKind.BitwiseXor,     TypeSymbol.UInt64),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.UInt64, TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.UInt64, TypeSymbol.Bool),
            new (SyntaxKind.LessThanToken,        BoundBinaryOperatorKind.LessThan,       TypeSymbol.UInt64, TypeSymbol.Bool),
            new (SyntaxKind.LessThanEqualToken,   BoundBinaryOperatorKind.LessEqual,      TypeSymbol.UInt64, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanToken,     BoundBinaryOperatorKind.GreaterThan,    TypeSymbol.UInt64, TypeSymbol.Bool),
            new (SyntaxKind.GreaterThanEqualToken,BoundBinaryOperatorKind.GreaterEqual,   TypeSymbol.UInt64, TypeSymbol.Bool),

            // ── bool / string ────────────────────────────────────────────────
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.String, TypeSymbol.Bool),
            new (SyntaxKind.EqualsEqualsToken,    BoundBinaryOperatorKind.Equals,         TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.Bool),
            new (SyntaxKind.BangEqualsToken,      BoundBinaryOperatorKind.NotEquals,      TypeSymbol.String, TypeSymbol.Bool),

            new (SyntaxKind.AmpersandAmpersandToken, BoundBinaryOperatorKind.LogicalAnd,  TypeSymbol.Bool),
            new (SyntaxKind.PipePipeToken,           BoundBinaryOperatorKind.LogicalOr,   TypeSymbol.Bool),
            
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition,TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition,TypeSymbol.String, TypeSymbol.Any, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.Any, TypeSymbol.String, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.String, TypeSymbol.Int, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.Int, TypeSymbol.String, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.String, TypeSymbol.Bool, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.Bool, TypeSymbol.String, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.String, TypeSymbol.Char, TypeSymbol.String),
            new(SyntaxKind.PlusToken,BoundBinaryOperatorKind.Addition, TypeSymbol.Char, TypeSymbol.String, TypeSymbol.String),

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

            // Left shift: T << int → int (C-like: shift amount is always int)
            new (SyntaxKind.LessThanLessThanToken, BoundBinaryOperatorKind.BitwiseLeftShift, TypeSymbol.UInt8,  TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.LessThanLessThanToken, BoundBinaryOperatorKind.BitwiseLeftShift, TypeSymbol.Int8,   TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.LessThanLessThanToken, BoundBinaryOperatorKind.BitwiseLeftShift, TypeSymbol.UInt16, TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.LessThanLessThanToken, BoundBinaryOperatorKind.BitwiseLeftShift, TypeSymbol.Int16,  TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.LessThanLessThanToken, BoundBinaryOperatorKind.BitwiseLeftShift, TypeSymbol.Int,    TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.LessThanLessThanToken, BoundBinaryOperatorKind.BitwiseLeftShift, TypeSymbol.UInt32, TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.LessThanLessThanToken, BoundBinaryOperatorKind.BitwiseLeftShift, TypeSymbol.Int64,  TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.LessThanLessThanToken, BoundBinaryOperatorKind.BitwiseLeftShift, TypeSymbol.UInt64, TypeSymbol.Int, TypeSymbol.Int),

            // Right shift: T >> int → int
            new (SyntaxKind.GreaterThanGreaterThanToken, BoundBinaryOperatorKind.BitwiseRightShift, TypeSymbol.UInt8,  TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.GreaterThanGreaterThanToken, BoundBinaryOperatorKind.BitwiseRightShift, TypeSymbol.Int8,   TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.GreaterThanGreaterThanToken, BoundBinaryOperatorKind.BitwiseRightShift, TypeSymbol.UInt16, TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.GreaterThanGreaterThanToken, BoundBinaryOperatorKind.BitwiseRightShift, TypeSymbol.Int16,  TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.GreaterThanGreaterThanToken, BoundBinaryOperatorKind.BitwiseRightShift, TypeSymbol.Int,    TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.GreaterThanGreaterThanToken, BoundBinaryOperatorKind.BitwiseRightShift, TypeSymbol.UInt32, TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.GreaterThanGreaterThanToken, BoundBinaryOperatorKind.BitwiseRightShift, TypeSymbol.Int64,  TypeSymbol.Int, TypeSymbol.Int),
            new (SyntaxKind.GreaterThanGreaterThanToken, BoundBinaryOperatorKind.BitwiseRightShift, TypeSymbol.UInt64, TypeSymbol.Int, TypeSymbol.Int),

            // Bitwise OR for mixed int/uint8/uint16 operands (chip-8 opcode construction)
            new (SyntaxKind.PipeToken, BoundBinaryOperatorKind.BitwiseOr, TypeSymbol.Int,    TypeSymbol.UInt8,  TypeSymbol.Int),
            new (SyntaxKind.PipeToken, BoundBinaryOperatorKind.BitwiseOr, TypeSymbol.UInt8,  TypeSymbol.Int,    TypeSymbol.Int),
            new (SyntaxKind.PipeToken, BoundBinaryOperatorKind.BitwiseOr, TypeSymbol.Int,    TypeSymbol.UInt16, TypeSymbol.Int),
            new (SyntaxKind.PipeToken, BoundBinaryOperatorKind.BitwiseOr, TypeSymbol.UInt16, TypeSymbol.Int,    TypeSymbol.Int),
            new (SyntaxKind.PipeToken, BoundBinaryOperatorKind.BitwiseOr, TypeSymbol.UInt8,  TypeSymbol.UInt8,  TypeSymbol.Int),
            new (SyntaxKind.PipeToken, BoundBinaryOperatorKind.BitwiseOr, TypeSymbol.UInt16, TypeSymbol.UInt16, TypeSymbol.Int),

            // Bitwise AND for mixed int/uint types (chip-8 opcode masking)
            new (SyntaxKind.AmpersandToken, BoundBinaryOperatorKind.BitwiseAnd, TypeSymbol.Int,    TypeSymbol.UInt8,  TypeSymbol.Int),
            new (SyntaxKind.AmpersandToken, BoundBinaryOperatorKind.BitwiseAnd, TypeSymbol.UInt8,  TypeSymbol.Int,    TypeSymbol.Int),
            new (SyntaxKind.AmpersandToken, BoundBinaryOperatorKind.BitwiseAnd, TypeSymbol.Int,    TypeSymbol.UInt16, TypeSymbol.Int),
            new (SyntaxKind.AmpersandToken, BoundBinaryOperatorKind.BitwiseAnd, TypeSymbol.UInt16, TypeSymbol.Int,    TypeSymbol.Int),
            new (SyntaxKind.AmpersandToken, BoundBinaryOperatorKind.BitwiseAnd, TypeSymbol.UInt8,  TypeSymbol.UInt8,  TypeSymbol.Int),
            new (SyntaxKind.AmpersandToken, BoundBinaryOperatorKind.BitwiseAnd, TypeSymbol.UInt16, TypeSymbol.UInt16, TypeSymbol.Int),

            // uint16 arithmetic with int (chip-8 PC operations)
            new (SyntaxKind.PlusToken,  BoundBinaryOperatorKind.Addition,    TypeSymbol.UInt16, TypeSymbol.Int, TypeSymbol.UInt16),
            new (SyntaxKind.MinusToken, BoundBinaryOperatorKind.Subtraction, TypeSymbol.UInt16, TypeSymbol.Int, TypeSymbol.UInt16),
        ];

        public static BoundBinaryOperator? Bind(SyntaxKind syntaxKind, TypeSymbol leftType, TypeSymbol rightType)
        {
            foreach (var op in _operators)
            {
                if (op.SyntaxKind == syntaxKind && op.LeftType == leftType && op.RightType == rightType)
                    return op;
            }
            return null;
        }

        // Numeric promotion rank — lower index = narrower type.
        // Follows C integer-promotion rules: any type narrower than int gets promoted to int.
        // A char sits at the very front: it has no arithmetic of its own, so 'a' + 1 promotes
        // to int, while 'a' == 'a' binds exactly against the char comparison rows above.
        private static readonly TypeSymbol[] PromotionRank =
        [
            TypeSymbol.Char,
            TypeSymbol.Int8, TypeSymbol.UInt8,
            TypeSymbol.Int16, TypeSymbol.UInt16,
            TypeSymbol.Int,   TypeSymbol.UInt32,
            TypeSymbol.Int64, TypeSymbol.UInt64,
            TypeSymbol.Float32, TypeSymbol.Float64, TypeSymbol.Float,
        ];

        /// <summary>
        /// Tries to find an operator by promoting both operands to their common wider type.
        /// Sets <paramref name="promotedLeft"/> / <paramref name="promotedRight"/> to the
        /// target type when a conversion is needed; null means no conversion required.
        /// </summary>
        public static BoundBinaryOperator? BindWithPromotion(
            SyntaxKind syntaxKind,
            TypeSymbol leftType,
            TypeSymbol rightType,
            out TypeSymbol? promotedLeft,
            out TypeSymbol? promotedRight)
        {
            promotedLeft  = null;
            promotedRight = null;

            int idxL = Array.IndexOf(PromotionRank, leftType);
            int idxR = Array.IndexOf(PromotionRank, rightType);

            // Both types must be in the numeric promotion rank.
            if (idxL < 0 || idxR < 0)
                return null;

            // Determine the wider type.
            var wider = idxL >= idxR ? leftType : rightType;

            // C rule: integers narrower than int are promoted to int.
            int intIdx = Array.IndexOf(PromotionRank, TypeSymbol.Int);
            if (Array.IndexOf(PromotionRank, wider) < intIdx)
                wider = TypeSymbol.Int;

            // Look for an exact operator for (wider, wider).
            var op = Bind(syntaxKind, wider, wider);
            if (op == null)
                return null;

            promotedLeft  = wider != leftType  ? wider : null;
            promotedRight = wider != rightType ? wider : null;
            return op;
        }
    }
