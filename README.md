# ProLang

A statically typed programming language that compiles to .NET MSIL, with additional backends that
transpile to C99 for native and PlayStation Portable targets.

```prolang
import "io"

struct Point {
    x: int;
    y: int;
}

func distance(a: Point, b: Point) : int {
    let dx: int = a.x - b.x
    let dy: int = a.y - b.y
    return dx * dx + dy * dy
}

func main() {
    let origin: Point = Point { x: 0, y: 0 }
    let target: Point = Point { x: 3, y: 4 }
    print("squared distance = " + string(distance(origin, target)))
}
```

## Getting started

Requires the **.NET 10 SDK**.

```bash
# Build the compiler
dotnet build src/ProLang.sln -c Release

# Compile and run a program
dotnet run --project src/ProLang/ProLang.csproj -- examples/hello/hello.prl -o hello.dll
dotnet hello.dll
```

## Command line

| Option | Description |
|---|---|
| `-o PATH` | Output assembly path |
| `-m NAME` | Module name |
| `-r PATH` | Reference a .NET assembly |
| `--target=KIND` | Subsystem of the emitted assembly: `library` (default), `console`, or `winexe` |
| `--framework=NAME` | Shared framework to request, or the shorthand `windowsdesktop` |
| `-d`, `--disassemble` | Print the lowered intermediate representation |
| `--msil=PATH` | Print an MSIL listing for a compiled assembly |
| `--emit-c` | Transpile to C99 |
| `--emit-psp` | Transpile to C99 targeting the PSP, with a build makefile |
| `--emit-csharp` | Render the lowered program as C# for inspection |
| `--c-output=PATH` | Override the transpiler output directory |
| `-h`, `--help` | Show help |

## Language

Statically typed with `int`, sized integers (`int8` … `uint64`), `float32`/`float64`, `bool`,
`string`, `array<T>`, `map<K,V>`, user-defined `struct` and `enum`, and a dynamic `any`. Generic
functions and structs are monomorphised at bind time. Programs need an explicit `main()`; a file
without one compiles as a library.

.NET interop lets a program import namespaces and assemblies directly:

```prolang
import "dotnet:System"
import "winforms"
```

`import "test"` brings in `assert(condition, message)`, which ends the program with a non-zero
exit code and a message on stderr. It is what lets a test suite written in ProLang actually fail;
`print()` is buffered until `main()` returns, and `assert` flushes it before throwing so the output
leading up to a failure survives.

See [AGENTS.md](AGENTS.md) for a full language tour, and [examples/](examples/) for worked
programs including a JSON parser, a CHIP-8 emulator, a Snake game, and a
[pixel editor](examples/16-pixel-editor/) with a 295-check test suite.

A Windows Forms program wants both new flags:

```bash
prolang examples/16-pixel-editor/main.prl --target=winexe -o bin/pixel-editor.dll
```

`--target=winexe` selects the windows subsystem, so no console window opens behind the form; it
implies `--framework=windowsdesktop` in the emitted `runtimeconfig.json`, and marks the entry point
`[STAThread]`.

That last part matters more than it sounds. The Windows *common* dialogs — open file, save file —
are COM objects that require a single-threaded apartment. Called from an MTA thread they do not
throw: they disable the owner window and never return, so the program looks frozen while still
pumping messages. `MessageBox` and an ordinary `Form.ShowDialog()` are unaffected, which makes the
symptom look specific to the file commands rather than to the apartment.

## Backends

| Target | Flag | Runtime |
|---|---|---|
| .NET assembly | *(default)* | `ProLang.Runtime.dll`, deployed beside the output |
| C99 | `--emit-c` | `native/*.h`, emitted alongside the generated C |
| PlayStation Portable | `--emit-psp` | `native/*.h` plus a `Makefile.psp` for the pspdev toolchain |

`--emit-csharp` is not a target — it renders the lowered program as readable C# so codegen can be
inspected without reading IL. See [the .NET backend](docs/architecture/dotnet-backend.md).

## Repository layout

```
src/ProLang/            Compiler: lexer, parser, binder, lowering, backends
src/ProLang.Runtime/    Managed runtime library shipped with compiled programs
src/ProLang.Tests/      xUnit suite: IL snapshots, execution tests, IL validity
src/ProLang.Benchmarks/ BenchmarkDotNet suite covering each compiler phase
src/WinFormsHelper.Tests/ xUnit suite for the Windows Forms shim (Windows only)
native/                 C runtime headers for the C99 and PSP backends
std/                    Standard library written in ProLang
examples/               Example programs
tests/                  ProLang test programs used as the test corpus
docs/                   Architecture, contributing, and performance documentation
```

## Development

```bash
dotnet test src/ProLang.Tests/ProLang.Tests.csproj -c Release
dotnet test src/WinFormsHelper.Tests/WinFormsHelper.Tests.csproj -c Release   # Windows only
dotnet run -c Release --project src/ProLang.Benchmarks -- --filter "*"
```

Test baselines (IL snapshots and expected program output) are generated rather than committed:

```bash
PROLANG_UPDATE_SNAPSHOTS=1 dotnet test src/ProLang.Tests/ProLang.Tests.csproj -c Release
```

## Documentation

- [The .NET backend](docs/architecture/dotnet-backend.md) — how source becomes an assembly
- [Boxing in the .NET backend](docs/architecture/boxing.md) — where it comes from and how to remove it
- [.NET interop](docs/architecture/dotnet-interop.md) — referencing assemblies and calling into .NET
- [Adding a builtin](docs/contributing/adding-a-builtin.md)
- [Performance baseline](docs/perf/baseline-2026-08-15.md)
- [AGENTS.md](AGENTS.md) — language reference and PSP toolchain setup
