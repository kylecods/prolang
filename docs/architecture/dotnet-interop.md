# .NET interop

How a ProLang program calls into .NET, and how references are resolved.

---

## Referencing an assembly

Two equivalent routes, both resolved the same way:

```prolang
import "assembly:CSharpFibonacci"                    // bare name
import "assembly:libs/CSharpFibonacci.dll"           // explicit path
import "assembly:test_lib/CSharpFibonacci.csproj"    // C# project
```

```bash
prolang app.prl -r CSharpFibonacci
prolang app.prl -r test_lib/CSharpFibonacci.csproj
```

Namespaces are also importable directly, for the BCL:

```prolang
import "dotnet:System"
```

## What resolution does

`Interop/AssemblyReferenceResolver` probes, in order:

1. the path as written, relative to the importing file, then the working directory, then absolute
2. the same with `.dll` appended, so a bare name works
3. the compiler's own `lib/` directory, for bundled helpers such as `WinFormsHelper`
4. **build output nearby** — a recursive search for `<name>.dll`, preferring paths under `bin/`,
   then `Release` over `Debug`, then most recently written

Step 4 is what removes the need to write
`assembly:test_lib/bin/Debug/net10.0/MyLib.dll` — a path that hardcodes a configuration and a
target framework, and silently breaks when either changes.

A `.csproj` reference resolves through the project's own build output. If the project exists but
has not been built, the diagnostic says so and gives the command to run, rather than reporting a
missing file.

### Case is significant

A reference must match the assembly's file name exactly, including case, even on Windows.
Windows would happily resolve `CSharpFIbonacci.dll` to `CSharpFibonacci.dll` and then fail on
Linux, where the same source is suddenly a broken reference — so the resolver compares the name
on disk and reports the mismatch with a suggestion.

### When it fails

The diagnostic names near-miss assemblies found nearby and lists every location searched:

```
csharp_fibonacci.prl(1,40,1,72): Could not find assembly './CSharpFIbonacci.dll'.
    Did you mean 'CSharpFibonacci.dll'?
    searched: .../CSharpFIbonacci.dll
    searched: .../**/CSharpFIbonacci.dll
```

## Deployment

A resolved assembly is copied next to the compiled program automatically, alongside
`ProLang.Runtime.dll`. Nothing has to be deployed by hand for `dotnet myprogram.dll` to start.

## `-r` and `import` behave the same

Both load the assembly and register its namespaces, so referenced types are visible to the
**binder** as well as the emitter.

They did not always. `-r` used to hand its path only to the emitter, so a referenced type resolved
during code generation but was rejected during binding as undefined — the flag appeared to work
and then did not. Both now go through `ProLangCompilation.TryReferenceAssembly`.

## How a call is emitted

The binder discovers members through `System.Reflection` (`Interop/DotNetAssemblyRegistry`) and
records a `MethodInfo` on a `DotNetFunctionSymbol`. The emitter needs a Cecil `MethodReference`,
so `CodeGen/DotNet/InteropEmitter` converts it by matching metadata full names.

Every interop member is therefore resolved twice, through two different reflection stacks. That
is the largest remaining wart in this area; see
[the .NET backend](dotnet-backend.md#outstanding-work).

> **Overload limitation.** Matching prefers exact parameter type names and falls back to arity.
> The fallback is unavoidable while reflection and Cecil spell constructed generics differently —
> `List\`1[[System.Int32, …]]` against `List\`1<System.Int32>` — so an overload set that differs
> only in generic parameter types can still resolve to the wrong member.

## Building a C# library for interop

Nothing special is required — an ordinary class library:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

## Calling members

| Member kind | Syntax | |
|---|---|---|
| Static method | `Math.Abs(x)` | ✅ |
| Static property | `DateTime.Now` | ✅ |
| Static field | `String.Empty` | ✅ |
| Constructor | `Random.new()` | ✅ |
| Instance method | `r.Next(100)` | ✅ on reference types |
| Instance method on a struct | `g.Equals(g)` | ❌ diagnostic — see below |

```prolang
import "io"
import "dotnet:System"

func main() {
    print(string(Math.Abs(0 - 7)))       // static method
    print(string(DateTime.Now))          // static property
    print(String.Empty)                  // static field
    print(String.Concat("Pro", "Lang"))  // overload chosen by arity

    let r = Random.new()                 // constructor
    print(string(r.Next(100)))           // instance method on the result
}
```

**Let the type be inferred.** `let r = Random.new()` keeps the .NET type, which is what makes
`r.Next(100)` resolvable. Writing `let r: any = Random.new()` erases it deliberately, and instance
calls then fail — `any` carries nothing to resolve a member name against.

That inference works because a .NET value now has a `DotNetTypeSymbol` carrying its
`System.Type`, rather than collapsing to `any`. It is still `System.Object` at the IL level, and
converts wherever `any` does, so nothing that compiled before stopped compiling.

### Instance methods on value types are rejected

Calling an instance method on a struct — `Guid`, `DateTime`, `TimeSpan` — reports:

```
Cannot call instance method 'Equals' on 'Guid', which is a .NET value type.
Use a static member of 'Guid', or string(value) to format it.
```

The call needs a *managed pointer* to the struct, which is what IL's `constrained.` prefix is for
and which the bound tree cannot currently express. Boxing the receiver instead produces IL that
passes verification and then reads the object header as though it were the struct's data —
`g.Equals(g)` returned `False`. Refusing is better than being silently wrong; lifting it needs an
address-of node in the bound tree.

Static members cover most of what these types are used for, and `string(value)` formats them.

### Overloads

Static methods and constructors are matched by name **and argument count**, so
`String.Concat("a", "b")` picks the two-argument overload. Overloads differing only in parameter
*types* at the same arity still resolve to whichever appears first in metadata order.

Only `public` members are callable.

> **A trap worth knowing about.** `examples/Directory.Build.targets` replaces `CoreCompile` so
> MSBuild can build `.prlproj` projects. That import is now conditioned on the project extension.
> Without the condition it applied to *every* project under `examples/`, including ordinary C#
> libraries — which then reported a successful build while producing no assembly, because the
> ProLang compiler had been run over a project containing no `.prl` files. That is why the interop
> example could never find its library.

## Worked example

`examples/05-dotnet-interop-assembly-loading/`:

```bash
cd examples/05-dotnet-interop-assembly-loading
./build.sh   # builds test_lib, then compiles the ProLang program
./run.sh
```
