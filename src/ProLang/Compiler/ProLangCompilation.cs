using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Interpreter;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Syntax;

using ReflectionBindingFlags = System.Reflection.BindingFlags;

namespace ProLang.Compiler;

public sealed class ProLangCompilation
{

    private BoundGlobalScope? _globalScope;

    internal BoundGlobalScope GlobalScope
    {
        get
        {
            if (_globalScope == null)
            {
                var globalScope = Binder.BindGlobalScope(IsScript,Previous?.GlobalScope, SyntaxTrees);

                Interlocked.CompareExchange(ref _globalScope, globalScope, null);
            }

            return _globalScope;
        }
    }
 

    private ProLangCompilation(bool isScript, ProLangCompilation? previous, params SyntaxTree[] syntaxTrees)
    {
        IsScript = isScript;
        Previous = previous;
        SyntaxTrees = syntaxTrees.ToImmutableArray();
    }

    public static ProLangCompilation Create(params SyntaxTree[] syntaxTrees)
    {
        return new ProLangCompilation(isScript:false,previous: null,syntaxTrees);
    }

    public static ProLangCompilation CreateScript(ProLangCompilation previous, params SyntaxTree[] syntaxTrees)
    {
        return new ProLangCompilation(isScript: true, previous: previous,syntaxTrees);
    }

    private BoundProgram GetProgram()
    {
        var previous = Previous == null ? null : Previous.GetProgram();

        return Binder.BindProgram(IsScript, previous, GlobalScope);
    }

    internal EvaluationResult Evaluate(Dictionary<VariableSymbol, object> variables)
    {
        var parseDiagnostics = SyntaxTrees.SelectMany(st => st.Diagnostics);

        var diagnostics = parseDiagnostics.Concat(GlobalScope.Diagnostics).ToImmutableArray();
        
        if (diagnostics.Any())
        { 
            return new EvaluationResult(diagnostics, null!);
        }

        var program = GetProgram();

        //var appPath = Environment.GetCommandLineArgs()[0];
        //var appDirectory = Path.GetDirectoryName(appPath);
        //var cfgPath = Path.Combine(appDirectory!, "cfg.dot");
        //var cfgStatement = !program.Statement.Statements.Any() && program.Functions.Any() ?
        //        program.Functions.Last().Value : program.Statement;

        //var cfg = ControlFlowGraph.Create(cfgStatement);
        //using (var streamWriter = new StreamWriter(cfgPath))
        //{
        //    cfg.WriteTo(streamWriter);
        //}

        if (program.Diagnostics.Any())
        {
            return new EvaluationResult(program.Diagnostics.ToImmutableArray(), null!);
        }

        var evaluator = new Evaluator(program, variables);
        
        var value = evaluator.Evaluate();

        return new EvaluationResult(ImmutableArray<Diagnostic>.Empty, value);

    }
    
    public void EmitTree(TextWriter writer)
    {
        if (GlobalScope.MainFunction != null) 
        { 
            EmitTree(GlobalScope.MainFunction,writer);
        }
        else if (GlobalScope.ScriptFunction != null)
        {
           EmitTree(GlobalScope.ScriptFunction,writer);
        }
    }
    public void EmitTree(FunctionSymbol symbol, TextWriter writer)
    {
        var program = GetProgram();

        symbol.WriteTo(writer);
        writer.WriteLine();

        if (!program.Functions.TryGetValue(symbol, out var body))
        {
            return;
        }

        body.WriteTo(writer);
    }

    public ProLangCompilation? Previous { get; }

    public ImmutableArray<SyntaxTree> SyntaxTrees { get; }

    public bool IsScript { get; }

    public FunctionSymbol MainFunction => GlobalScope.MainFunction;

    public ImmutableArray<FunctionSymbol> Functions => GlobalScope.Functions;

    public ImmutableArray<VariableSymbol> Variables => GlobalScope.Variables;

    public IEnumerable<Symbol> GetSymbols()
    {
        var submission = this;

        var seenSymbols = new HashSet<string>();

        while (submission != null)
        {
            const ReflectionBindingFlags bindingFlags = 
                ReflectionBindingFlags.Static | 
                ReflectionBindingFlags.Public |
                ReflectionBindingFlags.NonPublic;

            var builtInFunctions = typeof(BuiltInFunctions)
                .GetFields(bindingFlags)
                .Where(fi => fi.FieldType == typeof(FunctionSymbol))
                .Select(fi => (FunctionSymbol)fi.GetValue(null))
                .ToList();


            foreach (var function in submission.Functions)
            {
                if (seenSymbols.Add(function.Name))
                {
                    yield return function;
                }
            }

            foreach (var variable in submission.Variables)
            {
                if (seenSymbols.Add(variable.Name))
                {
                    yield return variable;
                }
            }

            foreach (var builtIn in builtInFunctions)
            {
                if (seenSymbols.Add(builtIn.Name))
                {
                    yield return builtIn;
                }
            }

            submission = submission.Previous;

        }
    }

    public ImmutableArray<Diagnostic> Emit(string moduleName, string[] references, string outputPath)
    {
        var program = GetProgram();

        return Emitter.Emit(program,moduleName, references, outputPath);
    }

}