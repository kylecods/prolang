﻿using ProLang.Syntax;

namespace ProLang.Intermediate;

internal sealed class BoundBinaryOperator
{
    private BoundBinaryOperator(SyntaxKind syntaxKind, BoundBinaryOperatorKind kind, Type type)
         : this(syntaxKind, kind, type, type, type)
        {

        }

        private BoundBinaryOperator(SyntaxKind syntaxKind, BoundBinaryOperatorKind kind, Type operandType, Type resultType)
         : this(syntaxKind, kind, operandType, operandType, resultType)
        {

        }

        private BoundBinaryOperator(SyntaxKind syntaxKind, BoundBinaryOperatorKind kind, Type leftType, Type rightType, Type resultType)
        {
            SyntaxKind = syntaxKind;
            Kind = kind;
            LeftType = leftType;
            RightTye = rightType;
            Type = resultType;
        }

        public SyntaxKind SyntaxKind { get; }
        public BoundBinaryOperatorKind Kind { get; }
        public Type LeftType { get; }
        public Type RightTye { get; }
        public Type Type { get; }

        private static BoundBinaryOperator[] _operators =
        {
            new BoundBinaryOperator(SyntaxKind.PlusToken, BoundBinaryOperatorKind.Addition, typeof(int)),
            new BoundBinaryOperator(SyntaxKind.MinusToken, BoundBinaryOperatorKind.Subtraction, typeof(int)),
            new BoundBinaryOperator(SyntaxKind.StarToken, BoundBinaryOperatorKind.Multiplication, typeof(int)),
            new BoundBinaryOperator(SyntaxKind.SlashToken, BoundBinaryOperatorKind.Division, typeof(int)),
            new BoundBinaryOperator(SyntaxKind.EqualsEqualsToken, BoundBinaryOperatorKind.Equals, typeof(int), typeof(bool)),
            new BoundBinaryOperator(SyntaxKind.BangEqualsToken, BoundBinaryOperatorKind.NotEquals, typeof(int), typeof(bool)),
            new BoundBinaryOperator(SyntaxKind.PercentageToken, BoundBinaryOperatorKind.Modulo, typeof(int)),

            new BoundBinaryOperator(SyntaxKind.AmpersandAmpersandToken, BoundBinaryOperatorKind.LogicalAnd, typeof(bool)),
            new BoundBinaryOperator(SyntaxKind.PipePipeToken, BoundBinaryOperatorKind.LogicalOr, typeof(bool)),
            new BoundBinaryOperator(SyntaxKind.LessThanToken, BoundBinaryOperatorKind.LessThan, typeof(int),typeof(bool)),
            new BoundBinaryOperator(SyntaxKind.LessThanEqualToken, BoundBinaryOperatorKind.LessEqual, typeof(int),typeof(bool)),
            
            new BoundBinaryOperator(SyntaxKind.GreaterThanToken, BoundBinaryOperatorKind.GreaterThan, typeof(int),typeof(bool)),
            new BoundBinaryOperator(SyntaxKind.GreaterThanEqualToken, BoundBinaryOperatorKind.LessEqual, typeof(int),typeof(bool)),
        };

        public static BoundBinaryOperator? Bind(SyntaxKind syntaxKind, Type leftType, Type rightType)
        {
            foreach (var op in _operators)
            {
                if (op.SyntaxKind == syntaxKind && op.LeftType == leftType && op.RightTye == rightType)
                    return op;
            }

            return null;
        }
    }
