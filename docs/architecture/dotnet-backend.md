# The .NET backend

How ProLang source becomes a runnable .NET assembly, and where each concern lives.

Everything here is under `src/ProLang/CodeGen/DotNet/`, driven by
[`Compiler/Emitter.cs`](../../src/ProLang/Compiler/Emitter.cs).

---

## Where the backend sits

```
.prl source
   │
   ├─ Parse/Lexer.cs, Parse/Parser.cs        → syntax tree
   ├─ Intermediate/Binder.cs                 → bound tree + symbols
   ├─ Lowering/Lowerer.cs                    → if/while/for rewritten to goto + label
   │
   └─ BoundProgram ──┬─ Compiler/Emitter.cs   → .dll   (this document)
                     ├─ Compiler/CEmitter.cs  → .c     (--emit-c)
                     └─ Compiler/CEmitter.cs  → PSP    (--emit-psp)
```

All three backends consume the same `BoundProgram`. Lowering has already removed structured
control flow, so the emitter only ever sees six statement kinds: variable declaration, label,
goto, conditional goto, return, and expression statement.

`ProLangCompilation.PrepareProgram()` runs the phases up to code generation and returns either a
bound program or the diagnostics that stopped it. Every backend goes through it, so the order in
which phases are checked is defined once.

---

## Components

| File | Responsibility |
|---|---|
| `Compiler/Emitter.cs` | Orchestration, statement and expression emission |
| `CodeGen/DotNet/TypeEmitter.cs` | ProLang types to Cecil references; emits struct definitions |
| `CodeGen/DotNet/MethodBodyScope.cs` | Per-method locals, labels, and branch fixups |
| `CodeGen/DotNet/ReferenceAssemblyLocator.cs` | Finds and reads the .NET reference assemblies |
| `CodeGen/DotNet/ReferenceResolver.cs` | Resolves BCL/reference members to Cecil references, with caching |
| `CodeGen/DotNet/RuntimeLibrary.cs` | Locates `ProLang.Runtime.dll` and names its members |
| `CodeGen/DotNet/RuntimeOverloads.cs` | Which runtime overload takes a type unboxed; shared with the C# backend |
| `CodeGen/DotNet/InteropEmitter.cs` | Emits calls into .NET assemblies the program imported |
| `CodeGen/DotNet/Intrinsics/Intrinsic.cs` | A builtin's target member, or its bespoke IL sequence |
| `CodeGen/DotNet/Intrinsics/IntrinsicRegistry.cs` | Table mapping each builtin to an `Intrinsic` |
| `CodeGen/DotNet/Intrinsics/IntrinsicContext.cs` | What an intrinsic emitter is given |
| `CodeGen/CSharp/CSharpBackend.cs` | `--emit-csharp` rendering backend |
| `CodeGen/PreparedProgram.cs` | Result of the shared pre-emit phase check |
| `src/ProLang.Runtime/` | Managed runtime assembly shipped beside compiled programs |

IL is produced with **Mono.Cecil 0.11.6** — not `System.Reflection.Emit`, which cannot write a
PE file, and not `System.Reflection.Metadata`, which would mean hand-encoding signatures.

---

## Output shape

Every compiled program is one assembly containing:

- A single `abstract sealed class Program` in the global namespace, holding every user function
  as a `public static` method.
- One `SequentialLayout` value type per non-generic struct, also in the global namespace.
- A `.runtimeconfig.json` beside the output so `dotnet <program>.dll` can find a framework.
- A copy of `ProLang.Runtime.dll`.

Generic structs are monomorphised **by the binder**, not the emitter — concrete instantiations
arrive as ordinary `StructSymbol`s named e.g. `DynArray<int>`. Enums are erased to `System.Int32`
and their members constant-folded to `ldc.i4`, so `BoundProgram.EnumTypes` is never read here.

### Entry point protocol

The binder renames a user's `main` to `__UserMain` and synthesises a `__Main(string[])` that the
emitter fills in:

```
__Main(string[] args):
    call ProLang.Runtime.Output::Initialize()
    call Program::__UserMain()
    call ProLang.Runtime.Output::Flush()
    ret
```

Those three names — `__Main`, `__UserMain`, `__output` — are still matched as string literals in
`Binder.cs`, `Emitter.cs`, and `MsilDisassembler.cs`. Centralising them is outstanding work.

### Type mapping

| ProLang | .NET |
|---|---|
| `any` | `System.Object` |
| `bool` | `System.Boolean` |
| `int` / `uint32` | `System.Int32` / `System.UInt32` |
| `int8`/`uint8`/`int16`/`uint16`/`int64`/`uint64` | `SByte`/`Byte`/`Int16`/`UInt16`/`Int64`/`UInt64` |
| `float32` / `float64`, `float` | `System.Single` / `System.Double` |
| `string` | `System.String` |
| `array<T>` | `T[]` |
| `map<K,V>` | `Dictionary<K,V>` |
| `struct S` | `S` (value type) |
| `enum E` | `System.Int32` |

**Boxing.** `any` is `System.Object`, so a value type stored into an `any` — a variable, a struct
field, a return value, or an operand of `String.Concat(object, object)` — must be boxed. The test
is always *"is the emitted Cecil type a value type"*, never a list of ProLang type names: an
enumerated list silently missed `int64`, `float64`, and the sized integer types.

---

## Builtins

Builtins are singleton `FunctionSymbol`s on `Symbols/BuiltInFunctions.cs`, grouped into importable
modules (`io`, `math`, `filesystem`, `array`, `console`, `psp`) by `Symbols/Modules/`.

`IntrinsicRegistry` maps each one to the IL that implements it. Dispatch is a dictionary lookup
keyed on the symbol *instance* — several builtins share a name (`length` exists for both arrays
and strings), so name-based lookup would be ambiguous.

Most builtins are now a single line, because the work lives in `ProLang.Runtime`:

```csharp
[BuiltInFunctions.ConsoleSetCursor] = Call(RuntimeLibrary.ConsoleOps, "SetCursor", "System.Int32", "System.Int32"),
```

Only two shapes remain hand-emitted:

- **`length` on an array** — `ldlen` plus `conv.i4`, no call to make.
- **`print`** — its target is resolved per-emit rather than from the static table, since
  `ResolveOutputHelpers` must run first.

### ProLang.Runtime

`src/ProLang.Runtime/` is an ordinary dependency-free C# library:

| Type | Contents |
|---|---|
| `Output` | `Initialize` / `Write` / `Flush` — buffers `print()` and flushes at exit |
| `ConsoleOps` | Console module, including the output-redirection guards |
| `StringOps` | `CharAt`, `Substring` — the two whose ProLang semantics differ from .NET's |
| `MathOps` | `Random` |

It is read with Cecil for reference resolution and **never loaded into the compiler's process**.
The build drops it in `runtime/` beside the compiler; `ProLangCompilation.Emit` copies it next to
each emitted program.

Things that map exactly onto the BCL — `Math.Min`, `String.IndexOf`, `File.ReadAllText`,
`Thread.Sleep` — stay direct calls. Wrapping them would add a frame for nothing.

**Redirected output.** `ConsoleOps` makes every presentation operation (cursor, colour,
visibility) a no-op when `Console.IsOutputRedirected`. `SetCursorPosition` throws outright on a
redirected stream, so without this any program using the `console` module would crash when piped.
`ConsoleOps.Write` also flushes `Output` first, so buffered `print()` and direct console writes
appear in the order the program produced them.

**This does not apply to the C or PSP backends**, which have their own runtime in `native/*.h`.

---

## Metadata resolution and its cost

`ReferenceResolver` is the only route to anything outside the program being compiled. It walks the
loaded assemblies in load order and takes the first match, which is why
`System.Private.CoreLib` is loaded first — the facades that forward to it would otherwise win and
yield references with no members.

Resolution failure reports a diagnostic and returns `null`. **Callers must act on that.** Emitting
nothing where a call was expected strands operands on the evaluation stack and produces an
assembly that only fails when the JIT first reaches the method. `Emitter.Emit` will not write an
assembly if any diagnostic was reported during code generation.

Two caches, both of which matter a great deal:

- **`ReferenceAssemblyLocator`** caches the loaded assembly set for the process lifetime. Reading
  the twelve reference assemblies costs ~9 ms and 7.4 MB, which measurement put at roughly 99% of
  the time to compile a small program. Sharing `AssemblyDefinition` instances across emitters is
  safe: they are only read from, and `ImportReference` mutates the target module, never the source.
- **`ReferenceResolver`** caches resolved methods by signature. The emitter asks for the same
  handful of members once per emitted call site, so this turns per-call-site scans into one scan
  per distinct member. Only successes are cached — caching a failure would suppress its diagnostic
  at every later call site.

Together these took cold-start emission from 9.07 ms / 7.43 MB to 0.44 ms / 35.8 KB. See
[`docs/perf/baseline-2026-08-15.md`](../perf/baseline-2026-08-15.md).

---

## Interop

`InteropEmitter` is the seam between the compiler's **two reflection stacks**. The binder
discovers imported .NET members with `System.Reflection` and records a `MethodInfo` on a
`DotNetFunctionSymbol`; the emitter needs a Cecil `MethodReference`. Every interop member is
therefore resolved twice, the second time by matching metadata full names.

> **Known limitation.** Interop methods are matched by name and parameter *count*, not parameter
> types. Overloads differing only in parameter types resolve to whichever appears first in
> metadata order. Fixing it requires a reflection-to-Cecil type comparison for arbitrary types.

---

## Branch fixups

Lowering produces labels and gotos, and a goto can target a label that has not been emitted yet.
Branches are therefore emitted with a placeholder operand and recorded in a fixup list, which
`EmitFunctionBody` patches once the whole body exists. Branches are always emitted in long form;
`OptimizeMacros()` shortens them afterwards.

---

## Testing

`src/ProLang.Tests/` covers this backend three ways:

| Suite | What it proves |
|---|---|
| `IlSnapshotTests` | The emitted IL for a corpus of programs is unchanged, via `MsilDisassembler` |
| `ExecutionTests` | Compiled programs still produce the same stdout |
| `AssemblyValidityTests` | Every emitted method body has balanced stack depth and resolvable branch targets |
| `CorpusIntegrityTests` | No `.prl` file in the repository escapes classification |

Snapshots and expected output are generated, not committed. Regenerate with:

```bash
PROLANG_UPDATE_SNAPSHOTS=1 dotnet test src/ProLang.Tests/ProLang.Tests.csproj
```

`AssemblyValidityTests` is the one that catches the silent-failure class directly: it abstractly
interprets stack depth through every method body, so an intrinsic that emitted nothing on a failed
resolution shows up as an imbalance rather than as a runtime `InvalidProgramException`.

---

## The C# rendering backend

`--emit-csharp` writes the lowered program out as readable C#, into `.prolang/csharp/`. It is a
**diagnostic aid, not a compilation target** — the MSIL emitter remains the .NET code path.

It consumes the same `BoundProgram`, so what it shows is what the emitter sees, including the
goto-and-label form lowering leaves behind. Reconstructing `if`/`while` would hide the thing it
exists to show. Builtins render as calls into `ProLang.Runtime`, matching what the MSIL backend
emits, so the two read against each other.

The output compiles. All 26 corpus programs that render produce C# that builds clean against
`ProLang.Runtime.dll`, which is the practical fidelity check — several places where ProLang's type
system is more permissive than C#'s only surfaced because the generated code failed to compile:

| ProLang | C# | Rendering |
|---|---|---|
| `any` in arithmetic or comparison | no operators on `object` | unboxing cast to the other operand's type |
| implicit narrowing on assignment | explicit only | cast inserted at the assignment |
| `ushort + 1` stays `uint16` | promotes to `int` | cast back at the assignment |
| `as` works on value types | reference and nullable only | unboxing cast for value-type targets |
| shadowing in nested scopes | one flat method scope | shadowed locals get a numeric suffix |
| conversion to `string` | `(string)someInt` is illegal | `((object)x).ToString()` |

Struct discovery differs from the MSIL backend too. `BoundProgram.StructTypes` holds generic
*templates*; the monomorphised instantiations are never in it, and the MSIL emitter only gets away
with that because it emits struct types lazily as `GetTypeReference` meets them. A text backend
must declare a type before it is referenced, so `CollectStructTypes` gathers them up front from
signatures, locals, and field types, iterating until closed.

---

## Outstanding work

- **`Emitter.cs` is ~1,300 lines**, holding orchestration plus statement and expression emission.
  Metadata resolution, type mapping, struct emission, interop, builtins, per-method state, and
  the runtime library have all been extracted. Splitting expression emission out as well is
  possible but would mean threading a context object through some thirty methods to produce one
  file that is still the largest — worth doing only if it starts changing often.
- **Two reflection stacks.** The binder discovers interop members with `System.Reflection` while
  the emitter needs Cecil, so every interop member is resolved twice. Unifying them is a much
  larger change than this refactor.
- **Interop overload matching falls back to arity** when parameter type names do not compare
  equal, which is unavoidable while reflection and Cecil spell constructed generics differently.
- **Overload selection could move into the binder.** Both backends now decide independently which
  runtime overload a value reaches — `RuntimeOverloads` for .NET, `UnwrapAny` for C. If the binder
  stopped inserting a conversion to `any` where a typed overload resolves, both could drop their
  copy. See [boxing.md](boxing.md).
