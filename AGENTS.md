# ProLang Compiler - Agent Guide

## Overview

**ProLang** is a modern programming language that compiles to .NET MSIL (Microsoft Intermediate Language) and executes on the .NET runtime. It provides a clean syntax with strong typing, generics, structs, and seamless .NET interoperability.

**For Agents**: This guide explains the ProLang compiler architecture, how to compile code, run tests, and debug issues efficiently.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Compiler Architecture](#compiler-architecture)
3. [Building the Compiler](#building-the-compiler)
4. [Compiling ProLang Code](#compiling-prolang-code)
5. [Running Tests](#running-tests)
6. [Examples](#examples)
7. [Troubleshooting](#troubleshooting)
8. [Language Features](#language-features)

---

## Quick Start

### Prerequisites

- **.NET 10.0** or later (use `dotnet --version` to check)
- **ProLang compiler** (built from source or pre-built)

### Program Structure

**Important**: All ProLang programs must have an explicit `main()` function. Global statements (code outside of functions) are not allowed.

```prolang
import "io"

func main() {
    print("Hello, World!")
}
```

### Compile a ProLang Program

```bash
cd D:/MOVEMENT/DESKTOP/PERSONAL/prolang

# Compile a single file
dotnet run --project src/ProLang/ProLang.csproj -- examples/hello/hello.prl

# Compile with output path
dotnet run --project src/ProLang/ProLang.csproj -- examples/hello/hello.prl -o hello.dll

# Compile multiple files
dotnet run --project src/ProLang/ProLang.csproj -- file1.prl file2.prl -o output.dll

# Run the compiled executable
dotnet hello.dll
```

---

## Compiler Architecture

### Compilation Pipeline

ProLang uses a **multi-stage compilation architecture**:

```
Source Code (.prl)
        ↓
    [LEXER] → Tokens
        ↓
    [PARSER] → Syntax Tree (AST)
        ↓
    [BINDER] → Bound AST + Symbols
        ↓
    [LOWERING] → Intermediate Representation
        ↓
    [EMITTER] → .NET MSIL Code
        ↓
    .NET Assembly (.dll/.exe)
        ↓
    .NET Runtime Execution
```

### Directory Structure

```
prolang/
├── src/ProLang/                   # Compiler source code
│   ├── Parse/                     # Lexer, Parser, diagnostics
│   ├── Syntax/                    # AST node definitions
│   ├── Intermediate/              # Binder, bound tree, control flow graph
│   ├── Lowering/                  # Rewrites structured control flow to goto + label
│   ├── Compiler/                  # Pipeline orchestration, MSIL emitter, C transpiler
│   ├── CodeGen/DotNet/            # Metadata resolution, builtin registry, interop
│   ├── Symbols/                   # Symbol table and builtin modules
│   ├── Text/                      # Source text management
│   ├── Interop/                   # .NET reflection-based symbol discovery
│   ├── Cli/                       # REPL
│   ├── Program.cs                 # Entry point
│   └── ProLang.csproj             # Project file
│
├── src/ProLang.Runtime/           # Managed runtime shipped with compiled programs
├── src/ProLang.Tests/             # xUnit suite: IL snapshots, execution, IL validity
├── src/ProLang.Benchmarks/        # BenchmarkDotNet suite
├── native/                        # C runtime headers for the C99/PSP backends
├── vscode-extension/              # VS Code extension: a thin client for `prolang lsp`
├── docs/                          # Architecture, contributing, performance
│
├── examples/                      # ProLang example programs
│   ├── 01-syntax-types/           # Type system examples
│   ├── 02-operators/              # Operator examples
│   ├── 03-control-flow/           # If/elif/else, loops
│   ├── 04-functions/              # Function definitions
│   ├── 05-dotnet-interop/         # .NET interop examples
│   ├── 06-structs/                # Struct definitions
│   ├── 07-strings/                # String operations
│   ├── 08-ring-buffer/            # Ring buffer implementation
│   ├── 09-json-parser/            # JSON parser (bidirectional)
│   ├── 10-lox/                    # Placeholder
│   ├── 11-std/                    # Standard library demos
│   ├── 12-snake/                  # Console Snake game
│   ├── 13-winforms/               # Windows Forms interop
│   ├── 14-chip-8/                 # CHIP-8 emulator
│   ├── 15-psp-demo/               # PlayStation Portable target
│   ├── 16-pixel-editor/           # Pixel editor: 4 modules over the std/ui toolkit
│   └── 17-widgets/                # The widget toolkit: one counter, on Windows Forms and on PSP
│
├── std/                           # Standard library, shipped beside the compiler
│   ├── ui/                        # UI toolkit: shapes, themes, icons, layout, WinForms boundary
│   └── README.md                  # Module list and the rules for adding one
│
├── tests/std/                     # The standard library's own suite, written in ProLang
├── tools/install-app.ps1          # Installs a compiled ProLang application for the current user
├── install.ps1                    # Installs the compiler as the `prolang` command
│
└── .claude/                       # Claude Code configuration
    ├── launch.json                # Dev server launch configs
    └── plans/                     # Planning documents
```

---

## Building the Compiler

### From Source

```bash
cd D:/MOVEMENT/DESKTOP/PERSONAL/prolang

# Restore dependencies
dotnet restore src/ProLang/ProLang.csproj

# Build the compiler
dotnet build src/ProLang/ProLang.csproj -c Release

# The compiler is now ready to use
# Output: src/ProLang/bin/Release/net10.0/ProLang.dll
```

### Verify Build

```bash
dotnet run --project src/ProLang/ProLang.csproj -- --help

# Output:
# usage: prolang <source-paths> [options]
#   -r=PATH       The path of an assembly to reference
#   -o=PATH       The output path of the assembly to create
#   -m=NAME       The name of the module
#   -h, --help    Prints help
```

---

## Compiling ProLang Code

### Command-Line Interface

```bash
dotnet run --project src/ProLang/ProLang.csproj -- [OPTIONS] <SOURCE-FILES>
```

### Options

| Option | Description | Example |
|--------|-------------|---------|
| `-o PATH` | Output assembly path | `-o output.dll` |
| `-m NAME` | Module name | `-m MyModule` |
| `-r PATH` | Reference assembly | `-r System.Core.dll` |
| `--target=KIND` | Subsystem: `library` (default), `console`, `winexe` | `--target=winexe` |
| `--framework=NAME` | Shared framework, or `windowsdesktop` | `--framework=windowsdesktop` |
| `--apphost` | Also write a native launcher beside the assembly | `--apphost` |
| `--icon=PATH` | Embed an `.ico` in the launcher. Implies `--apphost` | `--icon=bin/app.ico` |
| `-d, --disassemble` | Print the lowered IR | `-d` |
| `--msil=PATH` | Print an MSIL listing for a compiled assembly | `--msil=out.dll` |
| `--emit-c` | Transpile to C99 | `--emit-c` |
| `--emit-psp` | Transpile to C99 for the PSP | `--emit-psp` |
| `--emit-csharp` | Render the lowered program as C# for inspection | `--emit-csharp` |
| `--c-output=PATH` | Override the transpiler output directory | `--c-output=./gen` |
| `-h, --help` | Show help | `-h` |

### The language server

`prolang lsp --stdio` runs a Language Server Protocol server over standard input and output,
driven by the same lexer, parser and binder that compile a program. It backs the VS Code extension
in `vscode-extension/`, which contains no analysis of its own.

Worth knowing when changing the compiler's front end: the binder records what each name resolves
to as it binds (`src/ProLang/Intermediate/BindingRecorder.cs`), and every editor navigation feature
is a query over that. `docs/architecture/language-server.md` explains the design; the tests are in
`src/ProLang.Tests/Lsp/`.

### Examples

#### Single File Compilation

```bash
dotnet run --project src/ProLang/ProLang.csproj -- examples/hello/hello.prl -o hello.dll
dotnet hello.dll
```

#### Multiple File Compilation

```bash
dotnet run --project src/ProLang/ProLang.csproj -- \
  examples/09-json-parser/json-parser.prl \
  examples/09-json-parser/json-parser-tests.prl \
  -o json-parser.dll
  
dotnet json-parser.dll
```

#### With .NET References

```bash
dotnet run --project src/ProLang/ProLang.csproj -- myfile.prl \
  -r "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Core.dll" \
  -o myapp.dll
```

#### Building a distributable application

`--apphost` writes a native launcher beside the assembly, so the program runs as `app.exe` rather
than `dotnet app.dll`. It is a byte-patched copy of the .NET SDK's apphost template
(`src/ProLang/Compiler/AppHost.cs`), and it carries the subsystem across — which is what actually
suppresses the console window for a GUI program, since `dotnet` is itself a console application.

```bash
prolang app.prl --target=winexe --apphost -o bin/app.dll     # windowed
prolang tool.prl --target=console --apphost -o bin/tool.dll  # console
```

A failure to write a launcher is a warning, not an error: the assembly is still a complete program.

`--icon` embeds a multi-resolution `.ico` into the launcher as Win32 resources
(`src/ProLang/Compiler/IconEmbedder.cs`), which is what Explorer, the taskbar and Start Menu
shortcuts read. `ui/chrome` can draw the file — see `examples/16-pixel-editor/makeicon.prl` — so an
icon is generated from source rather than committed. The icon a *running window* shows is separate
and is set with `chrome_window_icon`.

Two things about the embedding are worth knowing before touching it. The `.ico` is taken apart:
each image becomes an `RT_ICON` and the file's directory is rewritten as an `RT_GROUP_ICON` whose
entries are **14 bytes**, not the file's 16, because a 2-byte resource id replaces a 4-byte offset.
Getting that wrong does not fail — the executable is valid and Windows silently shows the generic
icon. `IconEmbedderTests` compares the group byte for byte for that reason.

### Installing

`install.ps1` installs the compiler for the current user and puts `prolang` on PATH;
`tools/install-app.ps1` installs a program built with it, with a Start Menu entry for a windowed
one or a PATH entry for a command-line one. Both take `-Uninstall`. The compiler is also packable
as a .NET global tool (`dotnet pack`, then `dotnet tool install --global --add-source`).

Whatever installs the compiler must bring `std/`, `runtime/` and `lib/` with it — they sit beside
the executable and the compiler does not work without them. `runtime/` and `lib/` are produced by
`AfterTargets` steps in `ProLang.csproj` that copy into the build output rather than declaring
project items, so `dotnet publish` does **not** pick them up on its own; `install.ps1` copies them
explicitly and the `.csproj` names them explicitly for packing.

---

## Running Tests

### Test Examples

Each example is a self-contained ProLang program:

```
examples/
├── 01-syntax-types/
│   └── syntax_types.prl           # Types: int, string, bool, array, map, struct
├── 02-operators/
│   └── operators.prl              # +, -, *, /, %, ==, !=, <, >, !, &&, ||
├── 03-control-flow/
│   └── 03_control_flow.prl        # if, while, for, match expressions
├── 04-functions/
│   └── functions.prl              # Function definitions, recursion
├── 05-dotnet-interop-assembly-loading/
│   └── assembly_loading.prl       # Load .NET assemblies at runtime
├── 06-structs/
│   └── structs.prl                # Custom struct definitions
├── 07-strings/
│   └── strings.prl                # String methods: length(), charAt(), etc.
├── 08-ring-buffer/
│   └── ring-buffer.prl            # Ring buffer data structure
├── 09-json-parser/
│   ├── json-parser.prl            # JSON deserialization
│   └── json-parser-tests.prl      # JSON parser test suite
└── hello/
    └── hello.prl                  # Hello world program
```

### Running Example Tests

#### Simple Example

```bash
cd D:/MOVEMENT/DESKTOP/PERSONAL/prolang

# Compile and run hello world
dotnet run --project src/ProLang/ProLang.csproj -- examples/hello/hello.prl -o hello.dll
dotnet hello.dll

# Output:
# Hello, World!
# The answer is: 42
# ...
```

#### JSON Parser Tests (13 tests)

```bash
# Compile parser and tests
dotnet run --project src/ProLang/ProLang.csproj -- \
  examples/09-json-parser/json-parser.prl \
  examples/09-json-parser/json-parser-tests.prl \
  -o json-parser-final.dll

# Run tests
dotnet json-parser-final.dll

# Expected output:
# === ProLang JSON Parser - Test Suite ===
# Test 1: Parse String ... ✓ Passed
# Test 2: Parse Number ... ✓ Passed
# ... (13 tests total)
# === Test Summary ===
# ✅ All 13 tests passed!
```

#### Ring Buffer Example

```bash
# Compile and run ring buffer
dotnet run --project src/ProLang/ProLang.csproj -- \
  examples/08-ring-buffer/ring-buffer.prl \
  -o ring-buffer.dll

dotnet ring-buffer.dll
```

### Running All Examples

#### Bash/Linux/macOS

```bash
#!/bin/bash
cd D:/MOVEMENT/DESKTOP/PERSONAL/prolang

for example in examples/*/; do
    example_name=$(basename "$example")
    prl_file=$(ls "$example"*.prl 2>/dev/null | head -1)
    
    if [ -f "$prl_file" ]; then
        echo "Running: $example_name"
        dotnet run --project src/ProLang/ProLang.csproj -- "$prl_file" -o /tmp/test.dll
        dotnet /tmp/test.dll
        echo "---"
    fi
done
```

#### PowerShell/Windows

```powershell
cd D:\MOVEMENT\DESKTOP\PERSONAL\prolang

Get-ChildItem -Path examples -Directory | ForEach-Object {
    $example = $_.FullName
    $prlFiles = Get-ChildItem -Path $example -Filter "*.prl"
    
    foreach ($file in $prlFiles) {
        Write-Host "Running: $($file.Name)"
        & dotnet run --project src/ProLang/ProLang.csproj -- $file.FullName -o test.dll
        & dotnet test.dll
        Write-Host "---"
    }
}
```

---

## Examples

### 1. Hello World

**File**: `examples/hello/hello.prl`

```prolang
import "io"

func main() {
    print("Hello, World!")
    let x = 42
    print("The answer is: " + string(x))
}
```

**Run**:
```bash
dotnet run --project src/ProLang/ProLang.csproj -- examples/hello/hello.prl -o hello.dll
dotnet hello.dll
```

### 2. Functions and Recursion

**File**: `examples/04-functions/functions.prl`

```prolang
import "io"

func factorial(n: int) : int {
    if(n <= 1) {
        return 1
    }
    return n * factorial(n - 1)
}

func main() {
    print("5! = " + string(factorial(5)))
}
```

### 3. Structs and Types

**File**: `examples/06-structs/structs.prl`

```prolang
struct Person {
    name: string,
    age: int
}

let p = Person { name: "Alice", age: 30 }
print(p.name + " is " + string(p.age))
```

### 4. JSON Parsing (Bidirectional)

**File**: `examples/09-json-parser/json-parser.prl`

```prolang
// Deserialization: JSON string → ProLang object
let json = "[1, 2, 3]"
let arr = parseJson(json)
print(arr[0])  // Output: 1

// Serialization: ProLang object → JSON string
let typed_arr: array<int> = [10, 20, 30]
let json_str = arrayToJson(typed_arr)
print(json_str)  // Output: [10, 20, 30]
```

---

## Main Function & Entry Point

### Executables vs Libraries

ProLang supports two types of compilations:

**Executables** (with `main()` function):
- Requires an explicit `main()` function
- Has an entry point and can be run directly with `dotnet program.dll`
- Can have global statements if they're inside main()

**Libraries** (without `main()` function):
- No `main()` function needed
- Defines functions, types, and structs for reuse
- Cannot have global statements
- Compiled as DLL but cannot be executed directly
- Can be imported and used by other programs

### Main Function in Executables

Every executable program must define an explicit `main()` function. This is the entry point for program execution.

#### Signature Variants

**No arguments** (most common):
```prolang
func main() {
    print("Program starts here")
}
```

**With command-line arguments**:
```prolang
func main(args: array<string>) {
    let argCount = length(args)
    print("Received " + string(argCount) + " arguments")
    
    if argCount > 0 {
        print("First argument: " + args[0])
    }
}
```

### Output Collection & Flushing

All `print()` calls are collected internally and flushed to the console after `main()` completes. This ensures:
- **Atomic output**: All output appears at once without interleaving
- **Testability**: Output can be captured for testing
- **Proper ordering**: Multiple `print()` calls maintain their order

Example output behavior:
```prolang
func main() {
    print("Line 1")
    print("Line 2")
    print("Line 3")
}
// Output:
// Line 1
// Line 2
// Line 3
```

---

## Creating Libraries

Libraries in ProLang are files without a `main()` function. They define reusable functions, types, and structs:

```prolang
// math-lib.prl - A reusable library
import "io"

func add(a: int, b: int) : int {
    return a + b
}

func multiply(a: int, b: int) : int {
    return a * b
}

struct Point {
    x: int,
    y: int
}

func distance(p1: Point, p2: Point) : int {
    // Simplified distance calculation
    let dx = p1.x - p2.x
    let dy = p1.y - p2.y
    return dx + dy
}
```

**Compile as library:**
```bash
dotnet run --project src/ProLang/ProLang.csproj -- math-lib.prl -o math-lib.dll
```

**Using the library from a program:**
```prolang
import "io"

// Math functions available (would be imported in real scenario)
func add(a: int, b: int) : int {
    return a + b
}

func main() {
    let result = add(10, 20)
    print("Result: " + string(result))
}
```

### Library Restrictions

- ❌ **No `main()` function** - Libraries are not executable
- ❌ **No global statements** - Code must be in functions
- ✅ **Functions, types, and structs** - Libraries define reusable components

### The shared standard library

Modules under `std/` ship beside the compiler and are imported **by name, without an extension**:

```prolang
import "util"        // std/util.prl
import "ui/shape"    // std/ui/shape.prl
```

An extensionless import resolves against the `std/` directory next to the compiler executable
(`ProLangCompilation.cs`, the "Try the std/ directory" branch), so it works from any directory and
needs no relative path. A path **with** an extension — `import "tools.prl"` — resolves relative to
the importing file, which is how an application's own modules stay separate from the library's.

`std/README.md` is the module list. The rules when adding one:

- Give it a prefix nothing else uses. ProLang has **one flat global namespace** across every
  imported file, so a collision is a compile error and, worse, an unresolved name can silently bind
  to an arbitrary .NET method instead.
- Put tests in `tests/std/`, import them from `tests/std/run_tests.prl`, and classify **both** files
  in `src/ProLang.Tests/Infrastructure/TestCorpus.cs`. Files under `std/` are not scanned by the
  corpus, so that suite is their only coverage.
- Anything that imports `winforms` belongs in `ui/chrome`. The whole import graph compiles into one
  assembly, so a single edge to the shim makes a test suite need the Windows Desktop runtime pack
  and stop being runnable under `dotnet test`.
- New library files must be under `std/**/*.prl` to be picked up by the `Content` glob in
  `src/ProLang/ProLang.csproj`, which is what copies them beside the compiler and packs them into
  the `dotnet tool` package.

---

## Troubleshooting

### Common Errors

#### "error: file 'X.prl' doesn't exist"

**Cause**: Source file path is incorrect or file is missing

**Solution**:
```bash
# Verify file exists
ls examples/09-json-parser/json-parser.prl

# Use absolute path
dotnet run --project src/ProLang/ProLang.csproj -- \
  "D:/MOVEMENT/DESKTOP/PERSONAL/prolang/examples/09-json-parser/json-parser.prl"
```

#### Compilation Errors

**Example**: `Method 'length' does not exist on type 'any'`

**Cause**: Calling methods on `any` type that aren't guaranteed to exist

**Solution**:
- Use properly typed variables when possible
- For `any` types, use type detection before calling methods
- Check type with `string(value)` to see the actual .NET type

#### "All statements must be inside a main() function"

**Cause**: Global statements (code outside of functions) are not allowed

**Solution**:
```prolang
// WRONG - this will fail:
print("Hello")

// CORRECT - wrap in main():
func main() {
    print("Hello")
}
```

#### "Cannot convert type 'any' to 'array<any>'"

**Cause**: ProLang's type system doesn't allow implicit conversion from `any` to specific generic types

**Solution**:
- Keep variables as `any` type when dealing with dynamic JSON data
- Create separate functions for specific types (e.g., `arrayToJson(arr: array<int>)`)
- Use type-specific parsing functions

### Debugging Tips

#### Check Compiler Version

```bash
dotnet --version
# Expected: 10.0.0 or later
```

#### Verify Compilation Output

```bash
# List generated assembly
ls -la *.dll

# Inspect assembly with ILDASM (if installed)
ildasm output.dll
```

#### Test Minimal Code

```prolang
// minimal_test.prl
print("Compiler works!")
```

```bash
dotnet run --project src/ProLang/ProLang.csproj -- minimal_test.prl -o minimal.dll
dotnet minimal.dll
```

---

## Language Features

### Program Organization

ProLang supports **implicit program type detection**:
- **Executable**: File with `main()` function → can be run with `dotnet program.dll`
- **Library**: File without `main()` function → DLL for code reuse

### Type System

| Type | Example | Notes |
|------|---------|-------|
| `int` | `42` | 32-bit integer |
| `string` | `"hello"` | Immutable text |
| `bool` | `true`, `false` | Boolean |
| `any` | `parseJson(...)` | Dynamic type |
| `array<T>` | `array<int>` | Generic array |
| `map<K, V>` | `map<string, any>` | Key-value pairs |
| Custom `struct` | `struct Person { name: string }` | Aggregate types |

### Functions

```prolang
func add(a: int, b: int) : int {
    return a + b
}

let result = add(3, 5)
```

#### Default and named arguments

A parameter may declare a default, and an argument may be given by name:

```prolang
func box(label: string, width: int = 10, pad: int = 0, bold: bool = false) : string { ... }

box("a")                        // every default applies
box("b", 20)                    // positional, as usual
box("c", pad: 4)                // skip a parameter by naming a later one
box("d", bold: true, width: 7)  // named arguments may be in any order
```

Rules worth knowing:

- **A default must be a constant** — a literal, an enum member, or an expression that folds to one
  (`0 - 5`, `4 * 16`). A default that could name a variable would have to be re-bound in the
  *caller's* scope, where the name may not exist or, worse, may mean something else.
- **Optional parameters must come last.** Otherwise omitting one in the middle would silently shift
  every positional argument after it.
- **A positional argument cannot follow a named one.**
- Named arguments are for ProLang functions. A `.NET` method is matched by metadata signature,
  where parameter names are not part of the contract, so its arguments stay positional.

Everything is resolved in the binder: the argument list reaching the backends is always complete
and positional, so this works identically under `--emit-c` and `--emit-psp`.

#### Function values

A top-level function can be used as a value, and a parameter can have a function type:

```prolang
func double_it(x: int) : int { return x * 2 }

func apply(f: func(int) : int, v: int) : int {
    return f(v)
}

let chosen: func(int) : int = double_it
print(apply(chosen, 21))            // 42
```

`func(A, B) : R` is the type; the return clause is optional and means `void`. Types are structural,
so any function with a matching signature is assignable.

**They capture nothing.** That restriction is the whole design: with nothing captured, the .NET
backend can bind one to a BCL delegate over a null target (`ldnull; ldftn; newobj`) and the C and
PSP backends can use a plain function pointer — no garbage collector is involved on any target.

The consequence is that a handler cannot reach the state around it, so function values do *not*
replace the event-tag pattern; they are for things that are genuinely pure, such as a custom
painter. Refused rather than half-supported: generic functions, which have no single signature to
point at, and .NET methods, which no C backend could take a pointer to. At most 8 parameters.

### Control Flow

```prolang
// if / elif / else. `else if` is accepted as a synonym for `elif`.
if(x > 0) {
    print("positive")
} elif(x < 0) {
    print("negative")
} else {
    print("zero")
}

// while loop
while(i < 10) {
    print(i)
    i = i + 1
}

// for loop — the bounds are INCLUSIVE at both ends, so this prints 0 through 10
for(let i = 0 to 10) {
    print(i)
}
```

There is no `match`, `switch`, `do`/`while`, or foreach. A multi-way branch is an `if`/`elif`
chain, which is also how callbacks and dispatch tables are expressed — function values exist (see
below) but capture nothing, so a handler still cannot reach the state it would need to change.

### Structs

```prolang
struct Point {
    x: int;
    y: int;
}

func main() {
    let p: Point = Point { x: 10, y: 20 }
    print(p.x)  // 10
    p.x = 30    // fields are assignable

    // Arrays of structs, nested structs, and assignment through either, all work.
    let points: array<Point> = array_new(2)
    points[0] = Point { x: 1, y: 2 }
    points[0].x = 99
}
```

> **Structs are .NET value types**, so passing one to a function passes a *copy*: assigning to a
> scalar field of a parameter is invisible to the caller. An `array<T>` field is a reference to a
> real array, so writes *through* it are visible — that is the mutation channel the language has.
> The repo's convention for everything else is to return a new struct, as `std/dynarray.prl` does.

### Collections

```prolang
// Arrays are fixed-length. `length()` is the only method they have — there is no push or pop.
let arr: array<int> = [1, 2, 3]
let zeroed: array<int> = array_new(16)
print(arr.length())

// Growable collections are written in ProLang: see std/dynarray.prl for DynArray<T>, and
// examples/16-pixel-editor/intstack.prl for a fixed-capacity stack.

// Maps/Objects
let map: map<string, any> = { "name": "Alice", "age": 30 }
let name = map["name"]
```

> `map<K, V>` is thinly exercised — two uses in the whole repository. Prefer `array<T>`.

### Time

```prolang
import "console"

let start: int = time_millis()
thread_sleep(120)
let elapsed: int = time_millis() - start    // 120, give or take the scheduler
```

`time_millis()` and `thread_sleep(ms)` are the whole of the language's relationship with time, which
is why they share a module.

The clock is **monotonic**, not a wall clock, so an interval can never come out negative because the
system time was adjusted underneath it. It counts from an arbitrary origin — only differences mean
anything — and it is a 32-bit `int` like everything else, so it wraps after about twenty-five days.
Always **subtract two readings** rather than comparing them: two's-complement subtraction gives the
right interval straight through a wrap, while `if (now > then)` does not.

Backed by `Stopwatch` on .NET, `QueryPerformanceCounter` on Windows under `--emit-c`,
`clock_gettime(CLOCK_MONOTONIC)` on POSIX, and `sceKernelGetSystemTimeLow` on the PSP. All four are
fine-grained; `GetTickCount` was deliberately not used, because its ~15ms resolution is most of a
frame at 60fps.

`std/ui/fps.prl` is the worked example — a frame-rate counter built on it.

### Assertions

```prolang
import "test"

func main() {
    assert(1 + 1 == 2, "arithmetic works")
}
```

`assert` ends the program with a non-zero exit code and the message on stderr. It flushes the
`print()` buffer first, so the output leading up to a failure is not lost — without that, a
failing assert would discard everything the program had printed, since `print()` is only flushed
after `main()` returns.

This is what makes a test suite written in ProLang a real gate. `examples/16-pixel-editor/tests/`
is a worked example: 295 checks across ten modules, run by `dotnet test` through the corpus.

### String Methods

```prolang
let s = "hello"
print(s.length())           // 5
print(s.charAt(0))          // h
print(s.charCode(0))        // 104 — the only way to do arithmetic on text
print(s.substring(1, 3))    // el
print(s.indexOf("l"))       // 2
```

### .NET Interop

```prolang
// Call .NET methods
let now = System.DateTime.Now()
print(string(now))

// Use .NET types
let list = System.Collections.Generic.List<int>()
```

---

## For Agents: Working with ProLang

### File Navigation

When working on ProLang code:

1. **Source files** end with `.prl`
2. **Compiled output** is `.dll` or `.exe`
3. **Examples** are in `examples/` directory
4. **Compiler source** is in `src/ProLang/`

### Compilation Pattern

Standard pattern for agents to follow:

```bash
# 1. Navigate to project root
cd D:/MOVEMENT/DESKTOP/PERSONAL/prolang

# 2. Compile code
dotnet run --project src/ProLang/ProLang.csproj -- \
  input_file.prl \
  -o output.dll

# 3. Run executable
dotnet output.dll
```

### Reading Test Output

Tests print structured output:

```
=== Test Name ===
Input: [test data]
Output: [result]
✓ Passed

=== Test Summary ===
✅ All N tests passed!
```

### Common Agent Tasks

#### Task: Add a new test
1. Edit the `.prl` test file
2. Recompile with updated sources
3. Run and verify output

#### Task: Debug compilation error
1. Check error message for line number
2. Read line in source file
3. Identify type mismatch or syntax error
4. Fix and recompile

#### Task: Optimize code
1. Identify hot path
2. Reduce object allocations
3. Simplify type conversions
4. Recompile and test

---

## Additional Resources

### Documentation

- `README.md` - Overview, CLI reference, repository layout
- `docs/architecture/dotnet-backend.md` - How source becomes a .NET assembly
- `docs/architecture/language-server.md` - How `prolang lsp` answers an editor from the compiler
- `docs/architecture/ui-toolkit.md` - The framework-independent widget toolkit and its display list
- `docs/contributing/adding-a-builtin.md` - Worked example of adding a builtin function
- `docs/perf/baseline-2026-08-15.md` - Compiler performance baseline and methodology

### Key Files for Understanding

- `src/ProLang/Program.cs` - Compiler entry point (CLI)
- `src/ProLang/Compiler/ProLangCompilation.cs` - Pipeline orchestration, import resolution
- `src/ProLang/Parse/Lexer.cs` - Tokenization
- `src/ProLang/Parse/Parser.cs` - Parsing to AST
- `src/ProLang/Intermediate/Binder.cs` - Symbol binding and generic monomorphisation
- `src/ProLang/Lowering/Lowerer.cs` - Rewrites if/while/for into goto + label form
- `src/ProLang/Compiler/Emitter.cs` - MSIL emission
- `src/ProLang/CodeGen/DotNet/` - Metadata resolution, builtins, interop
- `src/ProLang.Runtime/` - Managed runtime shipped with compiled programs
- `src/ProLang/Compiler/CEmitter.cs` - C99 and PSP transpiler

---

## PSP Support (Compile to PlayStation Portable)

ProLang can target the **PSP** by transpiling to C99 and cross-compiling with the `pspdev`
toolchain (installed in WSL). This produces a native MIPS `EBOOT.PBP` — no .NET runtime
is needed on the PSP.

### Pipeline

```
foo.prl  →  prolang --emit-psp  →  .c + prolang_runtime.h + psp_main.c + Makefile.psp
                                              ↓  (WSL) make -f Makefile.psp
                                          EBOOT.PBP  →  PPSSPP / PSP memory stick
```

### Building (WSL2 Ubuntu)

```bash
# Toolchain: extract pspdev release into $PSPDEV, then:
export PSPDEV=$HOME/pspdev-tmp/pspdev
export PATH=$PATH:$PSPDEV/bin
```

```bash
# 1. On Windows, transpile (outputs to examples/xxx/.prolang/psp/):
dotnet run --project src/ProLang/ProLang.csproj -- foo.prl --emit-psp

# 2. In WSL, build (psp_main.c provides PSP_MODULE_INFO, exit callback, GU-less bootstrap):
make -f Makefile.psp
# Produces EBOOT.PBP
```

Run in PPSSPP by placing `EBOOT.PBP` in `PSP/GAME/YourGame/EBOOT.PBP`, or copy to a
PSP memory stick (`ms0:/PSP/GAME/YourGame/`) on custom firmware (e.g. 6.61 Infinity).

### The `psp` module

Programs can `import "psp"` to access GU graphics + controller input. The built-ins
map to `prl_psp_*` functions defined in the `__PSP__` branch of `prolang_runtime.h`:

| Function | Description |
|---|---|
| `psp_init()` | Initialise GU, double-buffered 480×272, set analog sampling |
| `psp_clear(color)` | Clear screen |
| `psp_fill_rect(x, y, w, h, color)` | Draw filled rectangle (GU 2D) |
| `psp_draw_text(x, y, text, color)` | Draw text with an 8×8 bitmap font |
| `psp_swap_buffers()` | Finish frame, wait vblank, swap display |
| `psp_vsync()` | Wait for vblank |
| `psp_buttons_held()` | Return controller button bitmask (see `pspctrl.h`) |
| `psp_button_pressed(button)` | True if a button bit is held |

**Colours** are `0xRRGGBB` — the usual hex-colour ordering, so `16711680` / `0xFF0000`
is red. The runtime swaps red and blue into the GE's native `0xAABBGGRR` and forces
alpha opaque (`prl_psp_color`); callers never deal with the hardware order.

Other `prl_*` runtime functions (print, sleep, console) also get PSP implementations.
`prl_print` / `prl_console_write` route through the **same GU 8×8 font renderer** (not
`pspDebugScreenPrintf`) and `prl_thread_sleep` → `sceKernelDelayThread`.

> **Important (rendering):** GU must be the *single* owner of VRAM. Do **not** call
> `pspDebugScreenInit()` alongside GU — both grab the same VRAM base and corrupt each
> other. `psp_main.c` deliberately omits it, and all text/`print()` output goes through
> the GU font so there is one framebuffer. `prl_psp_start_frame()` guards against calling
> `sceGuStart` twice per frame; always end a frame with `psp_swap_buffers()`.

> **Rendering invariants in `prl_psp.h`** (each of these was previously violated and
> produced flickering multicolour noise instead of the intended image):
> 1. **Framebuffer parameters are VRAM-relative offsets, not absolute addresses.**
>    `sceGuSwapBuffers` adds `sceGeEdramGetAddr()` (`0x04000000`) itself, so passing a
>    `0x44000000`-based pointer to `sceGuDrawBuffer`/`sceGuDispBuffer` makes
>    `sceDisplaySetFrameBuf` latch an out-of-range address and the display keeps showing
>    raw VRAM. `prl_psp_vram_alloc()` therefore returns plain offsets.
> 2. **Vertex data must live in display-list memory (`sceGuGetMemory`), never on the
>    stack.** `GU_DIRECT` only records the *pointer*; the GE dereferences it later, when
>    the list executes at `sceGuFinish`/`sceGuSync`. Stack locals are dead by then and
>    were never flushed out of the CPU data cache, so the GE reads garbage coordinates
>    and colours. `prl_psp_valloc()` sub-allocates from one pool taken per frame.
> 3. **The font byte layout is MSB-left** (`bits & (0x80 >> col)`); scanning it LSB-first
>    mirrors every glyph horizontally.
> 4. **`print()` must not swap buffers after drawing only the new text** — consecutive
>    prints then land in alternating buffers and the display flickers between two
>    half-written frames. The PSP console keeps a character/colour grid and repaints all
>    of it before each present.

### Examples

- `examples/15-psp-demo/psp_demo.prl` — moving rectangles + text + controller exit
- `examples/15-psp-demo/psp_chip8.prl` — a CHIP-8 emulator rendered via GU with
  controller-mapped hex keypad (D-pad + face buttons), Start exits

### Notes / limitations

- `--emit-psp` also emits the desktop C files/build scripts; only `Makefile.psp`,
  `.c`, `prolang_runtime.h`, and `psp_main.c` are used for PSP.
- `.NET interop` (`System.*`, WinForms, assembly loading) is unavailable on PSP —
  use the C transpiler path which has no managed runtime.
- Integer-heavy code requires explicit casts for narrowing (e.g. `uint8(x & 0xFF)`)
  — the conversion classifier treats widening as implicit, narrowing as explicit.
- Memory: generated C uses `malloc`/structs (no GC), friendly to the PSP's 32 MB.

---

## Summary

**ProLang** is a fully-featured language compiler that:
- ✅ Compiles to .NET MSIL
- ✅ Supports generics, structs, and type safety
- ✅ Provides .NET interoperability
- ✅ Has 9+ example programs
- ✅ Works on Linux, macOS, Windows
- ✅ Centralizes output through main() entry point
- ✅ Supports command-line arguments via main() parameters

**Key Requirements**:
- All programs must have an explicit `main()` function
- Global statements (code outside functions) are not allowed
- `main()` can optionally accept `args: array<string>` for command-line arguments
- All output is collected and flushed after main() completes

**For Agents**: Use this guide to efficiently compile, test, and debug ProLang code. The compilation pipeline is well-structured and errors are generally clear about what needs fixing. Remember to always wrap code in an explicit `main()` function.

---

**Last Updated**: 2026-05-09  
**Compiler Version**: net10.0  
**Status**: ✅ Production Ready (with centralized output & command-line argument support)
