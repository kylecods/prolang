using System.Text;
using System.Collections;
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

    private void ReportWarning(TextLocation location, string message)
    {
        _diagnostics.Add(new Diagnostic(location, message, DiagnosticSeverity.Warning));
    }

    /// <summary>
    /// An <c>import</c> nothing in the file uses.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error: the program builds and runs correctly with it. It is worth
    /// saying because ProLang gates builtins on imports — <c>print</c> is not in scope without
    /// <c>import "io"</c> — so imports accumulate as code moves around, and each one that is a
    /// module rather than a file is a whole namespace of names in scope for no reason.
    /// </remarks>
    public void ReportUnusedImport(TextLocation location, string path)
    {
        ReportWarning(location, $"Import '{path}' is not used.");
    }

    /// <summary>A local that is declared, and never read.</summary>
    public void ReportUnusedVariable(TextLocation location, string name)
    {
        ReportWarning(location, $"Variable '{name}' is declared but never used.");
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
        Report(location, "Unterminated string literal");
    }

    /// <summary>
    /// A <c>/*</c> with no <c>*/</c>.
    /// </summary>
    /// <remarks>
    /// Previously reported as an unterminated string literal, which sent anyone reading it looking
    /// for a quotation mark. The two are worth telling apart in an editor especially, where an
    /// unclosed block comment swallows the rest of the file and the message is the only clue.
    /// </remarks>
    public void ReportUnterminatedBlockComment(TextLocation location)
    {
        Report(location, "Unterminated block comment: '/*' has no matching '*/'");
    }

    public void ReportUndefinedFunction(TextLocation location, string name)
    {
        var message = $"Function '{name}' doesn't exist.";
        Report(location, message);
    }

    public void ReportUndefinedMethod(TextLocation location, string methodName, TypeSymbol receiverType)
    {
        var message = $"Method '{methodName}' does not exist on type '{receiverType}'.";
        Report(location, message);
    }

    public void ReportMethodNotAvailableOnFixedArray(TextLocation location, string methodName)
    {
        var message = $"'{methodName}' is not available on fixed-length array<T>. Use DynArray<T> from the std library for dynamic arrays.";
        Report(location, message);
    }

    public void ReportWrongMethodArgumentCount(TextLocation location, string methodName, int expectedCount, int actualCount)
    {
        var message = $"Method '{methodName}' requires {expectedCount} argument(s) but was given {actualCount}";
        Report(location, message);
    }

    public void ReportWrongArgumentCount(TextLocation location, string name, int expectedCount, int actualCount)
    {
        var message = $"Function '{name}' requires {expectedCount} arguments but was given {actualCount}";

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

    public void ReportDefaultValueMustBeConstant(TextLocation location, string parameterName)
    {
        var message =
            $"The default value for parameter '{parameterName}' must be a constant — a literal, "
            + "a negated literal, or an enum member.";

        Report(location, message);
    }

    public void ReportRequiredParameterAfterOptional(TextLocation location, string parameterName)
    {
        var message =
            $"Parameter '{parameterName}' has no default value, so it cannot follow one that does. "
            + "Move it before every optional parameter.";

        Report(location, message);
    }

    public void ReportPositionalArgumentAfterNamed(TextLocation location)
    {
        var message = "A positional argument cannot follow a named one.";

        Report(location, message);
    }

    public void ReportUndefinedArgumentName(TextLocation location, string functionName, string argumentName)
    {
        var message = $"Function '{functionName}' has no parameter named '{argumentName}'.";

        Report(location, message);
    }

    public void ReportArgumentAlreadyGiven(TextLocation location, string argumentName)
    {
        var message = $"Argument '{argumentName}' was already given a value in this call.";

        Report(location, message);
    }

    public void ReportMissingRequiredArgument(TextLocation location, string functionName, string parameterName)
    {
        var message = $"Function '{functionName}' requires an argument for parameter '{parameterName}'.";

        Report(location, message);
    }

    public void ReportFunctionValueTooManyParameters(TextLocation location, int maximum)
    {
        var message = $"A function value may take at most {maximum} parameters.";

        Report(location, message);
    }

    public void ReportCannotUseAsFunctionValue(TextLocation location, string functionName)
    {
        var message =
            $"'{functionName}' cannot be used as a function value. Only a non-generic ProLang "
            + "function can be, because a function value is a plain pointer with nothing captured.";

        Report(location, message);
    }

    public void ReportNamedArgumentThroughFunctionValue(TextLocation location, string calleeName)
    {
        var message =
            $"'{calleeName}' holds a function value, which records parameter types but not their "
            + "names or defaults. Pass its arguments positionally.";

        Report(location, message);
    }

    public void ReportNamedArgumentNotSupported(TextLocation location, string functionName)
    {
        var message =
            $"'{functionName}' is a .NET method, and named arguments are only supported for "
            + "ProLang functions. Pass its arguments positionally.";

        Report(location, message);
    }

    public void ReportUndefinedType(TextLocation location, string name)
    {
        var message = $"Type '{name}' doesn't exist";
        Report(location, message);
    }

    public void ReportCannotCompareWithNull(TextLocation location, TypeSymbol type)
    {
        var message =
            $"'{type}' is a value and can never be null, so comparing it with null is always "
            + "the same answer. Only a class, 'any', or a .NET type can be null.";

        Report(location, message);
    }

    public void ReportCannotInferTypeFromNull(TextLocation location, string name)
    {
        var message =
            $"Cannot infer the type of '{name}' from 'null'. Give it an explicit type, as in "
            + $"'let {name}: SomeClass = null'.";

        Report(location, message);
    }

    // ── imp blocks ───────────────────────────────────────────────────────────────

    public void ReportImpTargetNotAType(TextLocation location, string name)
    {
        var message = $"'{name}' is not a type, so there is nothing for an imp block to add functions to.";
        Report(location, message);
    }

    public void ReportImpMemberAlreadyDeclared(TextLocation location, string typeName, string memberName)
    {
        var message = $"'{typeName}->{memberName}' is already declared.";
        Report(location, message);
    }

    /// <summary>
    /// <c>self</c> in any position but the first.
    /// </summary>
    /// <remarks>
    /// The receiver is passed as argument 0, so a <c>self</c> anywhere else would silently be treated
    /// as an ordinary parameter and the function would not be callable on a value.
    /// </remarks>
    public void ReportSelfParameterMustBeFirst(TextLocation location)
    {
        var message = "'self' must be the first parameter.";
        Report(location, message);
    }

    public void ReportSelfParameterOutsideImpBlock(TextLocation location)
    {
        var message =
            "'self' only means anything inside an imp block. A top-level function has no receiver, so "
            + "name the parameter something else.";

        Report(location, message);
    }

    public void ReportSelfParameterTypeMismatch(TextLocation location, string typeName, TypeSymbol actual)
    {
        var message =
            $"'self' must have type '{typeName}' in an imp block for '{typeName}', but was declared "
            + $"'{actual}'.";

        Report(location, message);
    }

    public void ReportUndefinedImpFunction(TextLocation location, string typeName, string memberName)
    {
        var message = $"'{typeName}' has no function named '{memberName}'.";
        Report(location, message);
    }

    /// <summary>
    /// An instance method reached through the type rather than a value.
    /// </summary>
    /// <remarks>
    /// Phrased as the fix rather than the fault: during a migration this is the diagnostic that turns a
    /// wrong mechanical edit into a compile error carrying its own correction.
    /// </remarks>
    public void ReportInstanceMethodCalledOnType(TextLocation location, string typeName, string memberName)
    {
        var message =
            $"'{typeName}->{memberName}' takes 'self', so call it on a value: 'value->{memberName}(...)'. "
            + $"To pass the receiver explicitly, give it as the first argument.";

        Report(location, message);
    }

    public void ReportAssociatedFunctionCalledOnValue(TextLocation location, string typeName, string memberName)
    {
        var message =
            $"'{typeName}->{memberName}' has no 'self' parameter, so call it on the type: "
            + $"'{typeName}->{memberName}(...)'.";

        Report(location, message);
    }

    /// <summary>A method reference bound to a receiver.</summary>
    /// <remarks>
    /// A ProLang function value is a bare pointer with a null delegate target — there is nowhere to put
    /// the receiver. <c>Type-&gt;member</c> is a value; <c>value-&gt;member</c> is not.
    /// </remarks>
    public void ReportCannotTakeBoundMethodReference(TextLocation location, string typeName, string memberName)
    {
        var message =
            $"A function value cannot capture a receiver. Write '{typeName}->{memberName}' to take the "
            + $"function itself; it takes 'self' as its first argument.";

        Report(location, message);
    }

    public void ReportGenericImpNotSupported(TextLocation location, string typeName)
    {
        var message =
            $"An imp block for a generic type ('{typeName}') is not supported yet, because type "
            + "arguments cannot be inferred through a struct instantiation.";

        Report(location, message);
    }

    public void ReportStructCannotContainItself(TextLocation location, string name)
    {
        var message =
            $"Struct '{name}' contains itself by value, which has no finite size. "
            + $"Hold it through an 'array<{name}>' instead.";

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

    public void ReportDuplicateFieldName(TextLocation location, string structName, string fieldName)
    {
        var message = $"Field '{fieldName}' is already declared in struct '{structName}'";

        Report(location, message);
    }

    public void ReportUndefinedField(TextLocation location, string structName, string fieldName)
    {
        var message = $"Field '{fieldName}' does not exist in struct '{structName}'";

        Report(location, message);
    }

    public void ReportInvalidFieldAccess(TextLocation location, TypeSymbol type)
    {
        var message = $"Cannot access field on type '{type}'. '{type}' is not a struct type.";

        Report(location, message);
    }

    public void ReportCanOnlyCastFromAnyType(TextLocation location, TypeSymbol type)
    {
        var message = $"Cannot cast from type '{type}'. Cast expressions can only be used with 'any' type.";

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
        var message = "<main> must return void and have either no parameters or a single parameter of type array<string>.";

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

    /// <summary>
    /// A <c>class</c> reached a backend that cannot allocate one yet.
    /// </summary>
    /// <remarks>
    /// Reported rather than skipped. The precedent this replaces is the generic-struct skip below,
    /// which emitted nothing and said nothing: the type simply vanished from the generated C and the
    /// failure surfaced as a compiler or linker error against machine-written code.
    /// </remarks>
    public void ReportReferenceTypeNotSupportedByBackend(string name, string backend)
    {
        var message =
            $"'{name}' is a class, and the {backend} backend cannot allocate reference types yet. "
            + "Declare it as a 'struct' to target this backend.";

        Report(default, message);
    }

    /// <summary>A generic struct reached a backend that emits only non-generic ones.</summary>
    public void ReportGenericStructNotSupportedByBackend(string name, string backend)
    {
        var message =
            $"Generic struct '{name}' is not emitted by the {backend} backend, so any use of it "
            + "would fail to compile. Declare a non-generic struct for this target.";

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

    /// <summary>
    /// Reported when a struct field is assigned through something that has no storage location.
    /// </summary>
    /// <remarks>
    /// A struct is a value type, so writing to a field of one requires the address of where it
    /// lives. A local, a parameter, an array element and a field of any of those all have one; the
    /// result of a call does not — it is a temporary that is discarded at the end of the
    /// statement, so the assignment could not have any effect. This used to be emitted anyway,
    /// storing into a copy on the evaluation stack with no diagnostic at all.
    /// </remarks>
    public void ReportCannotAssignToTemporaryStructField(string fieldName)
    {
        var message =
            $"Cannot assign to field '{fieldName}': the value on the left is a temporary, "
            + "not a variable, array element, or field of one. Assign it to a variable first.";

        Report(default, message);
    }

    public void ReportRequiredMethodNotFound(string typeName, string methodName, string[] parameterTypeNames)
    {
        var parameterTypeNameList = string.Join(", ", parameterTypeNames);

        var message = $"The required method '{typeName}.{methodName}({parameterTypeNameList})' cannot be resolved among the given references.";

        Report(default, message);
    }

    /// <summary>
    /// Reports a bound node the C# rendering backend does not know how to write out.
    /// </summary>
    /// <remarks>
    /// Only <c>--emit-csharp</c> raises this. It means the debug rendering is incomplete for this
    /// program, not that the program is wrong — the MSIL backend is unaffected.
    /// </remarks>
    public void ReportUnsupportedCSharpNode(string nodeKind)
    {
        var message = $"The C# rendering backend does not support '{nodeKind}' nodes. " +
                      "The generated C# is incomplete; the compiled assembly is unaffected.";

        Report(default, message);
    }

    /// <summary>
    /// Reports an instance call on a .NET value type, which is not yet supported.
    /// </summary>
    /// <remarks>
    /// Calling an instance method on a struct requires a managed pointer to it, which the bound
    /// tree cannot currently express. Boxing instead compiles but returns wrong results, so this
    /// is rejected rather than mis-emitted.
    /// </remarks>
    public void ReportValueTypeInstanceCallUnsupported(TextLocation location, string methodName, string typeName)
    {
        var message =
            $"Cannot call instance method '{methodName}' on '{typeName}', which is a .NET value type. " +
            $"Use a static member of '{typeName}', or string(value) to format it.";

        Report(location, message);
    }

    public void ReportInvalidAssignmentTarget(TextLocation location)
    {
        var message = "Invalid assignment target.";
        Report(location, message);
    }

    /// <summary>
    /// Reports a referenced .NET assembly that could not be located.
    /// </summary>
    /// <remarks>
    /// Lists where the resolver looked and any similarly-named assemblies it passed on the way.
    /// A reference failure is nearly always a typo, a path that assumes a build configuration, or
    /// a project that has not been built — and which of the three it is only becomes obvious once
    /// you can see the search.
    /// </remarks>
    public void ReportAssemblyNotFound(
        TextLocation location,
        string request,
        IReadOnlyList<string> probedLocations,
        IReadOnlyList<string> suggestions)
    {
        var message = new StringBuilder();
        message.Append($"Could not find assembly '{request}'.");

        if (suggestions.Count > 0)
        {
            message.Append($" Did you mean {string.Join(" or ", suggestions.Select(s => $"'{s}'"))}?");
        }

        if (probedLocations.Count > 0)
        {
            message.AppendLine();
            message.Append("  Searched:");

            foreach (var probed in probedLocations.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                message.AppendLine();
                message.Append($"    {probed}");
            }
        }

        Report(location, message.ToString());
    }

    /// <summary>
    /// Reports a referenced project that exists but has produced no build output.
    /// </summary>
    public void ReportProjectNotBuilt(TextLocation location, string projectPath)
    {
        var message =
            $"The referenced project '{projectPath}' has no build output. " +
            $"Build it first, for example: dotnet build \"{projectPath}\"";

        Report(location, message);
    }

    public void ReportFileNotFound(TextLocation location, string path)
    {
        var message = $"Could not find file '{path}'.";
        Report(location, message);
    }

    public void ReportCircularImport(TextLocation location, string path)
    {
        var message = $"Circular import detected for '{path}'.";
        Report(location, message);
    }

    public void ReportGlobalStatementsRequireMainFunction(TextLocation location)
    {
        var message = "All statements must be inside a main() function. Global statements are not allowed without an explicit main() definition.";
        Report(location, message);
    }

    /// <summary>
    /// Reports a <c>global</c> declaration with no type clause.
    /// </summary>
    /// <remarks>
    /// A global's storage exists before any initializer runs, so its type cannot be inferred from
    /// the initializer the way a local's can — it must be spelled out.
    /// </remarks>
    public void ReportGlobalRequiresType(TextLocation location, string name)
    {
        var message = $"Global '{name}' must declare a type, e.g. global {name}: int = 0.";
        Report(location, message);
    }
}