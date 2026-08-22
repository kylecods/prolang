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

See [AGENTS.md](AGENTS.md) for a full language tour, and [examples/](examples/) for worked
programs including a JSON parser, a CHIP-8 emulator, and a Snake game.

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
native/                 C runtime headers for the C99 and PSP backends
std/                    Standard library written in ProLang
examples/               Example programs
tests/                  ProLang test programs used as the test corpus
docs/                   Architecture, contributing, and performance documentation
```

## Development

```bash
dotnet test src/ProLang.Tests/ProLang.Tests.csproj -c Release
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
