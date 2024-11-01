﻿using System.Collections;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Parse;

public sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = new();
    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void AddRange(DiagnosticBag diagnostics)
    {
        _diagnostics.AddRange(diagnostics._diagnostics);
    }

    private void Report(TextLocation location, string message)
    {
        var diagnostic = new Diagnostic(location, message);

        _diagnostics.Add(diagnostic);
    }

    public void ReportInvalidNumber(TextLocation location, string text, TypeSymbol type)
    {
        var message = $"The number {text} is not a valid {type}.";
        
        Report(location,message);
    }

    public void ReportBadCharacter(TextLocation location, char character)
    {
        var message = $"Bad character input: '{character}'";
        
        Report(location,message);
    }

    public void ReportUnexpectedToken(TextLocation location, SyntaxKind actualKind, SyntaxKind expectedKind)
    {
        var message = $"Unexpected token <{actualKind}>, expected <{expectedKind}>";
        
        Report(location,message);
    }

    public void ReportUndefinedUnaryOperator(TextLocation location, string operatorText, TypeSymbol operatorType)
    {
        var message = $"Unary operator '{operatorText}' is not defined for type {operatorType}";
        Report(location,message);
    }

    public void ReportUndefinedBinaryOperator(TextLocation location, string operatorText, TypeSymbol leftType, TypeSymbol rightType)
    {
        var message = $"Binary operator '{operatorText}' is not defined for types {leftType} and {rightType}";
        Report(location,message);
    }

    public void ReportUndefinedName(TextLocation location, string name)
    {
        var message = $"Variable '{name}' does not exist";
        Report(location,message);
    }

    public void ReportVariableAlreadyDeclared(TextLocation location, string name)
    {
        var message = $"Variable '{name}' is already declared";
        Report(location,message);
    }

    public void ReportCannotConvert(TextLocation location, TypeSymbol fromType, TypeSymbol toType)
    {
        var message = $"Cannot convert type '{fromType}' to '{toType}'.";
        Report(location, message);
    }

    public void ReportUnterminatedString(TextLocation location)
    {
        var message = "Unterminated string literal";
        var diagnostic = new Diagnostic(location, message);
        Report(location, message);
    }

    public void ReportUndefinedFunction(TextLocation location, string name)
    {
        var message = $"Function '{name}' doesn't exist.";
        Report(location,message);
    }

    public void ReportWrongArgumentCount(TextLocation location, string name, int expectedCount, int actualCount)
    {
        var message = $"Function '{name} requires {expectedCount}' arguments but was given {actualCount}";
        
        Report(location,message);
    }

    public void ReportWrongArgumentType(TextLocation location, string name, TypeSymbol expectedType, TypeSymbol actualType)
    {
        var message = $"Parameter '{name} requires a value of type '{expectedType}' but was given a value type '{actualType}'";
        
        Report(location,message);
    }

    public void ReportExpressionMustHaveValue(TextLocation location)
    {
        var message = "Expression must a value";
        
        Report(location,message);
    }

    public void ReportParameterAlreadyDeclared(TextLocation location, string parameterName)
    {
        var message = $"A parameter with the name '{parameterName}' already exists.";
        Report(location,message);
    }

    public void ReportUndefinedType(TextLocation location, string name)
    {
        var message = $"Type '{name}' doesn't exist";
        Report(location,message);
    }

    public void ReportCannotConvertImplicitly(TextLocation location, TypeSymbol fromType, TypeSymbol toType)
    {
        var message = $"Cannot convert type '{fromType}' to '{toType}'." +
                      $"An explicit conversion exists (are you missing a cast?)";
        Report(location, message);
    }

    public void ReportAllPathsMustReturn(TextLocation location)
    {
        var message = "Not all code paths return a value.";
        Report(location,message);
    }

    public void ReportSymbolAlreadyDeclared(TextLocation location, string name)
    {
        var message = $"'{name}' is already declared";

        Report(location, message);
    }

    public void ReportInvalidBreakOrContinue(TextLocation location, string text)
    {
        var message = $"The keyword '{text}' can only be used inside of loops";
        
        Report(location,message);
    }

    public void ReportInvalidReturn(TextLocation location)
    {
        var message = "The 'return' keyword can only be used inside of functions";
        Report(location,message);
    }

    public void ReportInvalidReturnExpression(TextLocation location, string functionName)
    {
        var message =
            $"Since the function '{functionName}'  does not return a value the 'return' keyword cannot be followed by an expression.";
        Report(location,message);
    }

    public void ReportMissingReturnExpression(TextLocation location, TypeSymbol returnType)
    {
        var message = $"An expression of type '{returnType}' expected.";
        
        Report(location,message);
    }
}