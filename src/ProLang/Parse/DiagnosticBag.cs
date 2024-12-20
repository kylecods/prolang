﻿using System.Collections;
using Mono.Cecil;
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

        Report(location, message);
    }

    public void ReportBadCharacter(TextLocation location, char character)
    {
        var message = $"Bad character input: '{character}'";

        Report(location, message);
    }

    public void ReportUnexpectedToken(TextLocation location, SyntaxKind actualKind, SyntaxKind expectedKind)
    {
        var message = $"Unexpected token <{actualKind}>, expected <{expectedKind}>";

        Report(location, message);
    }

    public void ReportUndefinedUnaryOperator(TextLocation location, string operatorText, TypeSymbol operatorType)
    {
        var message = $"Unary operator '{operatorText}' is not defined for type {operatorType}";
        Report(location, message);
    }

    public void ReportUndefinedBinaryOperator(TextLocation location, string operatorText, TypeSymbol leftType, TypeSymbol rightType)
    {
        var message = $"Binary operator '{operatorText}' is not defined for types {leftType} and {rightType}";
        Report(location, message);
    }

    public void ReportUndefinedName(TextLocation location, string name)
    {
        var message = $"Variable '{name}' does not exist";
        Report(location, message);
    }

    public void ReportVariableAlreadyDeclared(TextLocation location, string name)
    {
        var message = $"Variable '{name}' is already declared";
        Report(location, message);
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
        Report(location, message);
    }

    public void ReportWrongArgumentCount(TextLocation location, string name, int expectedCount, int actualCount)
    {
        var message = $"Function '{name} requires {expectedCount}' arguments but was given {actualCount}";

        Report(location, message);
    }


    public void ReportExpressionMustHaveValue(TextLocation location)
    {
        var message = "Expression must a value";

        Report(location, message);
    }

    public void ReportParameterAlreadyDeclared(TextLocation location, string parameterName)
    {
        var message = $"A parameter with the name '{parameterName}' already exists.";
        Report(location, message);
    }

    public void ReportUndefinedType(TextLocation location, string name)
    {
        var message = $"Type '{name}' doesn't exist";
        Report(location, message);
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
        Report(location, message);
    }

    public void ReportSymbolAlreadyDeclared(TextLocation location, string name)
    {
        var message = $"'{name}' is already declared";

        Report(location, message);
    }

    public void ReportInvalidBreakOrContinue(TextLocation location, string text)
    {
        var message = $"The keyword '{text}' can only be used inside of loops";

        Report(location, message);
    }


    public void ReportInvalidReturnExpression(TextLocation location, string functionName)
    {
        var message =
            $"Since the function '{functionName}'  does not return a value the 'return' keyword cannot be followed by an expression.";
        Report(location, message);
    }

    public void ReportMissingReturnExpression(TextLocation location, TypeSymbol returnType)
    {
        var message = $"An expression of type '{returnType}' expected.";

        Report(location, message);
    }

    public void ReportInvalidExpressionStatement(TextLocation location)
    {
        var message = "Only assignment and call expressions can be used as a statement.";

        Report(location, message);
    }

    public void ReportOnlyOneFileCanHaveGlobalStatements(TextLocation location)
    {
        var message = "At most one file can have global statements.";

        Report(location, message);
    }

    public void ReportMainFunctionMustHaveCorrectSignature(TextLocation location)
    {
        var message = "<main> must not take arguments and not return anything.";

        Report(location, message);
    }

    public void ReportCannotMixMainAndGlobalStatements(TextLocation location)
    {
        var message = "Cannot declare main function when global statements are used.";

        Report(location, message);
    }

    public void ReportInvalidReference(string path)
    {
        var message = $"The reference is not a valid .NET assembly: '{path}'";

        Report(default, message);
    }

    public void ReportRequiredTypeNotFound(string proLangName, string metaDataName)
    {
        var message = proLangName == null ? $"The required type '{metaDataName}' cannot be resolved among the given references." :
            $"The required type '{proLangName}' ('{metaDataName}') cannot be resolved among the given references";

        Report(default, message);
    }

    public void ReportRequiredTypeAmbiguous(string proLangName, string metaDataName, TypeDefinition[] foundTypes)
    {
        var assemblyNames = foundTypes.Select(t => t.Module.Assembly.Name.Name);

        var assemblyNameList = string.Join(", ", assemblyNames);

        var message = proLangName == null ?
            $"The required type '{metaDataName}' was found in multiple references: {assemblyNameList}."
            : $"The required type '{proLangName}' ('{metaDataName}') was found in multiple references : {assemblyNameList}";

        Report(default, message);
    }

    public void ReportRequiredMethodNotFound(string typeName, string methodName, string[] parameterTypeNames)
    {
        var parameterTypeNameList = string.Join(", ", parameterTypeNames);

        var message = $"The required method '{typeName}.{methodName}({parameterTypeNameList})' cannot be resolved among the given references.";

        Report(default, message);
    }
}