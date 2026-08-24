# Adding a builtin function

Worked example: adding `toUpper(str)` to the `string` builtins.

Before the code generation refactor this meant editing the middle of a 305-line `if / else if`
chain inside the emitter's largest method and hand-writing the IL. It is now four small edits, none
of which touch the emitter.

---

## 1. Declare the symbol

`src/ProLang/Symbols/BuiltInFunctions.cs`:

```csharp
public static readonly FunctionSymbol StringToUpper = new("toUpper",
    ImmutableArray.Create(new ParameterSymbol("str", TypeSymbol.String, 0)),
    TypeSymbol.String)
{
    Documentation =
        "Returns the text uppercased.\n\n" +
        "Invariant, not locale-dependent, so a program's output does not vary by machine.",
};
```

The symbol is the identity the rest of the compiler uses. Several builtins share a name — `length`
exists for both arrays and strings — so dispatch keys on this instance, never on the name.

**`Documentation` is required**, and `BuiltInDocumentationTests` fails the build without it. A
builtin has no source file, so unlike everything written in ProLang there is no comment above it
for the documentation to be read from — this declaration is the only place it can live. It is what
a user sees on hover, as the detail beside a completion, and in the signature popup while typing
the call. Write one sentence of what it does, then a blank line, then anything about units,
bounds, edge cases, or which backends implement it. It is rendered as Markdown.

**Parameter names are user-visible too.** They are what a named argument is written with, what
signature help labels, and what an inlay hint shows at a call site. `min(arg1, arg2)` told a reader
nothing, which is why it is now `min(a, b)`.

## 2. Expose it from a module

`src/ProLang/Symbols/Modules/` — add it to the relevant module's `Functions` list so that
`import "io"`, `import "math"`, and so on bring it into scope. Skipping this leaves the symbol
unreachable and the binder reporting *"Function 'toUpper' doesn't exist"*.

A whole new module also needs a `Summary`, which is the one line shown beside its name while
someone is deciding what to import. `EveryModule_HasASummary` enforces it.

## 3. Implement it

**If it maps exactly onto a BCL member**, no implementation is needed — go straight to step 4 and
point the registry at the BCL. `Math.Min`, `String.IndexOf`, and `File.ReadAllText` all work this
way. Wrapping them would add a call frame for nothing.

**Otherwise**, add a `public static` method to `src/ProLang.Runtime/`. This is where a builtin goes
when ProLang's semantics differ from .NET's, when it needs a guard, or when it needs state:

```csharp
// src/ProLang.Runtime/StringOps.cs
/// <summary>Returns <paramref name="str"/> uppercased using the invariant culture.</summary>
/// <remarks>Invariant, not current-culture, so program output does not vary by machine locale.</remarks>
public static string ToUpper(string str) => str.ToUpperInvariant();
```

Prefer this over hand-emitting IL. `substring` needed two scratch locals per call site as IL
because ProLang's end index is exclusive and .NET's parameter is a length; as C# it is one
expression.

## 4. Register it

`src/ProLang/CodeGen/DotNet/Intrinsics/IntrinsicRegistry.cs`:

```csharp
[BuiltInFunctions.StringToUpper] = Call(RuntimeLibrary.StringOps, "ToUpper", "System.String"),
```

Arguments are already on the evaluation stack in declaration order when the entry runs. Use
`Call` for static methods and `CallVirt` for instance methods on a receiver.

Parameter type names must be **metadata full names** and must match exactly — that is what
distinguishes overloads. `"System.Int32"`, not `"int"`.

## 5. Test it

Add a `.prl` program exercising it, then classify it in
`src/ProLang.Tests/Infrastructure/TestCorpus.cs`:

```csharp
new("tests/language/string-methods/to-upper.prl", CorpusKind.Runnable),
```

`CorpusIntegrityTests` fails if a `.prl` file exists without being classified, so this step is not
optional. Then capture the baselines:

```bash
PROLANG_UPDATE_SNAPSHOTS=1 dotnet test src/ProLang.Tests/ProLang.Tests.csproj -c Release
```

Review the generated IL snapshot and expected-output file before moving on, then run the suite
normally to confirm it asserts.

---

## Emitting IL directly

Only when there is genuinely nothing to call. `length` on an array is the sole remaining example —
it is `ldlen` plus `conv.i4`:

```csharp
[BuiltInFunctions.ArrayLength] = ctx =>
{
    ctx.IL.Emit(OpCodes.Ldlen);
    ctx.IL.Emit(OpCodes.Conv_I4);   // ldlen yields native int
},
```

`IntrinsicContext` gives you `IL`, `Method(...)` and `Type(...)` for resolution, `DeclareTemp` for
a scratch local, and `GetTypeReference` for mapping a ProLang type.

**If a resolution returns `null`, emit nothing at all and return.** A diagnostic has already been
reported, and `Emitter.Emit` will refuse to write the assembly. Emitting a partial sequence strands
operands on the stack and produces an assembly that only fails when the JIT reaches it —
`AssemblyValidityTests` exists to catch exactly that.

---

## Other backends

`IntrinsicRegistry` is the .NET backend only. A builtin that should also work under `--emit-c` or
`--emit-psp` needs a matching `prl_*` function in `native/` and a case in
`src/ProLang/Compiler/CEmitter.cs`. The `psp_*` builtins are the reverse case: they exist for the
PSP target and have no .NET implementation.
