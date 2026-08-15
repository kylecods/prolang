using Mono.Cecil;
using Mono.Cecil.Cil;
using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Emit;

/// <summary>
/// Structural verification of every emitted method body.
/// </summary>
/// <remarks>
/// The emitter guards most intrinsics with <c>if (method != null) { emit }</c> and no
/// <c>else</c>. When metadata resolution fails, nothing is emitted, the evaluation stack is left
/// unbalanced, and compilation still reports success — the failure only surfaces as a
/// <c>InvalidProgramException</c> when the method is first JIT-compiled, possibly never.
/// These tests turn that into a compile-time-visible failure by re-reading the emitted assembly
/// and abstractly interpreting stack depth through every method body.
/// </remarks>
public sealed class AssemblyValidityTests
{
    [Theory]
    [MemberData(nameof(Corpus))]
    public void EmittedAssembly_HasBalancedStackInEveryMethod(string relativePath)
    {
        var entry = TestCorpus.Get(relativePath);

        using var scratch = ScratchDirectory.Create(entry.Name);
        var result = CompilerHarness.CompileToFile(scratch.Path, entry.FullPath);

        Assert.True(
            result.Succeeded,
            $"Expected '{entry.RelativePath}' to compile, but got diagnostics:{Environment.NewLine}{result.DiagnosticText}");

        using var assembly = AssemblyDefinition.ReadAssembly(
            result.AssemblyPath!,
            new ReaderParameters { ReadSymbols = false });

        var failures = new List<string>();

        foreach (var module in assembly.Modules)
        {
            foreach (var type in module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    VerifyBody(type, method, failures);
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Invalid IL emitted for '{entry.RelativePath}':{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures.Select(f => "  " + f)));
    }

    /// <summary>
    /// Walks a method body verifying that branch targets resolve and that the evaluation stack
    /// depth is consistent along every path reaching a given instruction.
    /// </summary>
    private static void VerifyBody(TypeDefinition type, MethodDefinition method, List<string> failures)
    {
        var body = method.Body;
        var instructions = body.Instructions;

        if (instructions.Count == 0)
        {
            failures.Add($"{type.Name}::{method.Name} has an empty body.");
            return;
        }

        var indexOf = new Dictionary<Instruction, int>(instructions.Count);
        for (var i = 0; i < instructions.Count; i++)
        {
            indexOf[instructions[i]] = i;
        }

        // -1 means "not yet reached". Every reachable instruction gets the stack depth on entry.
        var depthOnEntry = new int[instructions.Count];
        Array.Fill(depthOnEntry, -1);

        var worklist = new Stack<(int Index, int Depth)>();
        worklist.Push((0, 0));

        // Exception handlers begin with the exception object (or nothing, for a finally block)
        // already pushed, so they are additional entry points rather than unreachable code.
        foreach (var handler in body.ExceptionHandlers)
        {
            if (handler.HandlerStart != null && indexOf.TryGetValue(handler.HandlerStart, out var handlerIndex))
            {
                var startDepth = handler.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter ? 1 : 0;
                worklist.Push((handlerIndex, startDepth));
            }
        }

        while (worklist.Count > 0)
        {
            var (index, depth) = worklist.Pop();

            if (index < 0 || index >= instructions.Count)
            {
                failures.Add($"{type.Name}::{method.Name} branches outside the method body.");
                return;
            }

            if (depthOnEntry[index] >= 0)
            {
                if (depthOnEntry[index] != depth)
                {
                    failures.Add(
                        $"{type.Name}::{method.Name} IL_{instructions[index].Offset:x4} is reached with " +
                        $"inconsistent stack depths ({depthOnEntry[index]} and {depth}).");
                }

                continue;
            }

            depthOnEntry[index] = depth;

            var instruction = instructions[index];
            var depthAfter = depth - PopCount(instruction, method) + PushCount(instruction);

            if (depthAfter < 0)
            {
                failures.Add(
                    $"{type.Name}::{method.Name} IL_{instruction.Offset:x4} ({instruction.OpCode.Name}) " +
                    $"pops from an empty stack (depth {depth} before).");
                continue;
            }

            switch (instruction.OpCode.FlowControl)
            {
                case FlowControl.Return:
                    var expected = method.ReturnType.FullName == "System.Void" ? 0 : 1;
                    if (instruction.OpCode.Code == Code.Ret && depth != expected)
                    {
                        failures.Add(
                            $"{type.Name}::{method.Name} returns with {depth} value(s) on the stack, expected {expected}.");
                    }
                    continue;

                case FlowControl.Throw:
                    continue;

                case FlowControl.Branch:
                    PushTarget(instruction, depthAfter);
                    continue;

                case FlowControl.Cond_Branch:
                    PushTarget(instruction, depthAfter);
                    PushNext(index, depthAfter);
                    continue;

                default:
                    PushNext(index, depthAfter);
                    continue;
            }
        }

        return;

        void PushTarget(Instruction instruction, int depth)
        {
            switch (instruction.Operand)
            {
                case Instruction target when indexOf.TryGetValue(target, out var targetIndex):
                    worklist.Push((targetIndex, depth));
                    break;

                case Instruction:
                    failures.Add(
                        $"{type.Name}::{method.Name} IL_{instruction.Offset:x4} branches to an instruction " +
                        "that is not in this method body (unpatched branch fixup).");
                    break;

                case Instruction[] targets:
                    foreach (var target in targets)
                    {
                        if (indexOf.TryGetValue(target, out var switchIndex))
                        {
                            worklist.Push((switchIndex, depth));
                        }
                    }
                    break;

                default:
                    failures.Add(
                        $"{type.Name}::{method.Name} IL_{instruction.Offset:x4} ({instruction.OpCode.Name}) " +
                        $"has a branch operand of unexpected type '{instruction.Operand?.GetType().Name ?? "null"}'.");
                    break;
            }
        }

        void PushNext(int index, int depth)
        {
            if (index + 1 < instructions.Count)
            {
                worklist.Push((index + 1, depth));
            }
            else
            {
                failures.Add($"{type.Name}::{method.Name} falls off the end of the method body.");
            }
        }
    }

    private static int PopCount(Instruction instruction, MethodDefinition containing) =>
        instruction.OpCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Popi or StackBehaviour.Pop1 or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi
                or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8
                or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi
                or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4
                or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
            StackBehaviour.Varpop => VariablePopCount(instruction, containing),
            StackBehaviour.PopAll => 0, // Only `leave`, which unwinds the stack entirely.
            _ => 0,
        };

    private static int PushCount(Instruction instruction) =>
        instruction.OpCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8
                or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Varpush => VariablePushCount(instruction),
            _ => 0,
        };

    /// <summary>Argument count for a call, plus the receiver, plus the return value for newobj.</summary>
    private static int VariablePopCount(Instruction instruction, MethodDefinition containing)
    {
        if (instruction.OpCode.Code == Code.Ret)
        {
            return containing.ReturnType.FullName == "System.Void" ? 0 : 1;
        }

        if (instruction.Operand is not IMethodSignature signature)
        {
            return 0;
        }

        var count = signature.Parameters.Count;

        // newobj allocates its own receiver, so `this` is not on the stack for it.
        if (signature.HasThis && !signature.ExplicitThis && instruction.OpCode.Code != Code.Newobj)
        {
            count++;
        }

        return count;
    }

    private static int VariablePushCount(Instruction instruction)
    {
        if (instruction.OpCode.Code == Code.Newobj)
        {
            return 1;
        }

        if (instruction.Operand is not IMethodSignature signature)
        {
            return 0;
        }

        return signature.ReturnType.FullName == "System.Void" ? 0 : 1;
    }

    public static TheoryData<string> Corpus => TestCorpus.CompilableData;
}
