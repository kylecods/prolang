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

## Installing

Requires the **.NET 10 SDK**. Either route installs for the current user only — no administrator
rights, nothing machine-wide.

```powershell
# From a clone. Builds, installs to %LOCALAPPDATA%\Programs\ProLang, puts it on PATH.
.\install.ps1

# Or as a .NET global tool.
dotnet pack src/ProLang/ProLang.csproj -c Release
dotnet tool install --global --add-source ./artifacts/nupkg ProLang.Compiler
```

Both give you `prolang` on the command line, with the standard library beside it:

```powershell
prolang hello.prl --target=console --apphost -o hello.dll
.\hello.exe
```

`.\install.ps1 -Uninstall` removes it and its PATH entry;
`dotnet tool uninstall --global ProLang.Compiler` removes the tool. Installing both is fine, but
whichever comes first on PATH is the one `prolang` runs — `(Get-Command prolang).Source` says
which.

### Building applications

`--apphost` writes a native launcher next to the assembly, so a compiled program starts like any
other rather than as `dotnet app.dll`. For a GUI program it is what keeps the console window away:
`dotnet` is itself a console application, so starting a window through it opens one regardless.

```powershell
# A console tool
prolang tool.prl --target=console --apphost -o bin/tool.dll

# A Windows Forms application, with its own icon
prolang app.prl --target=winexe --apphost --icon=bin/app.ico -o bin/app.dll
```

`--icon` embeds a multi-resolution `.ico` into the launcher, which is what Explorer, the taskbar
and Start Menu shortcuts read. The library can draw one: `ui/chrome` renders artwork at each size
Windows asks for and writes the file, so an application's icon is drawn from source like the rest
of it rather than committed as a binary. `examples/16-pixel-editor/makeicon.prl` is twenty lines
doing exactly that.

The icon a *running window* shows is a separate thing, set with `chrome_window_icon`. Both are
worth setting: a program that sets only one looks finished in half the places it appears.

`tools/install-app.ps1` then installs what you built — a Start Menu entry for a windowed program,
a PATH entry for a command-line one:

```powershell
.\tools\install-app.ps1 -Path bin\app.exe -Name "My Application"
.\tools\install-app.ps1 -Path bin\tool.exe -AddToPath
.\tools\install-app.ps1 -Name "My Application" -Uninstall
```

### Working from a clone, without installing

```bash
dotnet build src/ProLang.sln -c Release
dotnet run --project src/ProLang/ProLang.csproj -- examples/hello/hello.prl -o hello.dll
dotnet hello.dll
```

## Standard library

Modules ship beside the compiler and are imported by name from anywhere:

```prolang
import "util"
import "ui/shape"
import "ui/theme"
```

`util`, `intstack`, `dynarray` and `testing`, plus `ui/` — a toolkit for drawing interfaces:
colours, pixel buffers, two rasterisers, flood fill, zoom and pan, undo history, palettes, themes,
a resolution-independent icon set, DPI-aware layout metrics, and the Windows Forms boundary.

See [std/README.md](std/README.md). `examples/16-pixel-editor/` is the worked example: after the
library was extracted it is four files, and everything else comes from `std/`.

## Command line

| Option | Description |
|---|---|
| `-o PATH` | Output assembly path |
| `-m NAME` | Module name |
| `-r PATH` | Reference a .NET assembly |
| `--target=KIND` | Subsystem of the emitted assembly: `library` (default), `console`, or `winexe` |
| `--framework=NAME` | Shared framework to request, or the shorthand `windowsdesktop` |
| `--apphost` | Also write a native launcher, so the program runs without `dotnet` |
| `--icon=PATH` | Embed an `.ico` in the launcher as the program's icon. Implies `--apphost` |
| `-d`, `--disassemble` | Print the lowered intermediate representation |
| `--msil=PATH` | Print an MSIL listing for a compiled assembly |
| `--emit-c` | Transpile to C99 |
| `--emit-psp` | Transpile to C99 targeting the PSP, with a build makefile |
| `--emit-csharp` | Render the lowered program as C# for inspection |
| `--c-output=PATH` | Override the transpiler output directory |
| `-h`, `--help` | Show help |

The compiler also runs the language server:

```bash
prolang lsp --stdio          # spoken by the editor, not by a person
```

## Editor support

`vscode-extension/` is a VS Code extension that starts `prolang lsp` and shows what the compiler
says: real diagnostics, hover documentation, go to definition across the import graph and into the
standard library, contextual completion, signature help, semantic highlighting, an outline, folding,
inlay hints and formatting.

It contains no analysis of its own — the extension is a client, and the language it describes is by
construction the language the compiler compiles. See
[the language server](docs/architecture/language-server.md).

```bash
cd vscode-extension && npm install && npm run compile
```

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
std/                    Standard library, shipped beside the compiler and imported by name
std/ui/                 UI toolkit: shapes, themes, icons, layout, the Windows Forms boundary
tools/                  install-app.ps1, for installing a compiled ProLang application
examples/               Example programs
tests/                  ProLang test programs used as the test corpus
tests/std/              The standard library's own test suite
vscode-extension/       VS Code extension: a thin client for `prolang lsp`
docs/                   Architecture, contributing, and performance documentation
```

## Development

```bash
dotnet test src/ProLang.Tests/ProLang.Tests.csproj -c Release
dotnet test src/WinFormsHelper.Tests/WinFormsHelper.Tests.csproj -c Release   # Windows only
dotnet run -c Release --project src/ProLang.Benchmarks -- --filter "*"
```

The standard library's own suite is written in ProLang and runs as part of the corpus, but is
worth running on its own while working on it:

```bash
dotnet run --project src/ProLang/ProLang.csproj -- tests/std/run_tests.prl -o bin/std-tests.dll
dotnet bin/std-tests.dll
```

Test baselines (IL snapshots and expected program output) are generated rather than committed:

```bash
PROLANG_UPDATE_SNAPSHOTS=1 dotnet test src/ProLang.Tests/ProLang.Tests.csproj -c Release
```

## Documentation

- [The .NET backend](docs/architecture/dotnet-backend.md) — how source becomes an assembly
- [Boxing in the .NET backend](docs/architecture/boxing.md) — where it comes from and how to remove it
- [.NET interop](docs/architecture/dotnet-interop.md) — referencing assemblies and calling into .NET
- [The language server](docs/architecture/language-server.md) — how the editor asks the compiler
- [Adding a builtin](docs/contributing/adding-a-builtin.md)
- [Performance baseline](docs/perf/baseline-2026-08-15.md)
- [AGENTS.md](AGENTS.md) — language reference and PSP toolchain setup
