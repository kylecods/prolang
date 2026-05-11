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
│   ├── Parse/                     # Lexer, Parser, Syntax tree
│   ├── Compiler/                  # Binder, Symbol resolution
│   ├── Lowering/                  # Intermediate code generation
│   ├── Intermediate/              # IR definitions
│   ├── Symbols/                   # Symbol table and definitions
│   ├── Syntax/                    # AST node definitions
│   ├── Text/                      # Source text management
│   ├── Interop/                   # .NET interop support
│   ├── Cli/                       # CLI utilities
│   ├── Program.cs                 # Entry point
│   └── ProLang.csproj             # Project file
│
├── examples/                      # ProLang example programs
│   ├── 01-syntax-types/           # Type system examples
│   ├── 02-operators/              # Operator examples
│   ├── 03-control-flow/           # If/loops/match
│   ├── 04-functions/              # Function definitions
│   ├── 05-dotnet-interop/         # .NET interop examples
│   ├── 06-structs/                # Struct definitions
│   ├── 07-strings/                # String operations
│   ├── 08-ring-buffer/            # Ring buffer implementation
│   ├── 09-json-parser/            # JSON parser (bidirectional)
│   ├── hello/                     # Basic hello world
│   └── test-string-methods/       # String method tests
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
| `-h, --help` | Show help | `-h` |

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

### Control Flow

```prolang
// if-else
if(x > 0) {
    print("positive")
} else {
    print("non-positive")
}

// while loop
while(i < 10) {
    print(i)
    i = i + 1
}

// for loop
for(i = 0 to 10) {
    print(i)
}
```

### Structs

```prolang
struct Point {
    x: int,
    y: int
}

let p = Point { x: 10, y: 20 }
print(p.x)  // 10
```

### Collections

```prolang
// Arrays
let arr: array<int> = [1, 2, 3]
arr.push(4)

// Maps/Objects
let map: map<string, any> = { "name": "Alice", "age": 30 }
let name = map["name"]
```

### String Methods

```prolang
let s = "hello"
print(s.length())           // 5
print(s.charAt(0))          // h
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

- `IMPLEMENTATION_SUMMARY.md` - Feature completeness
- `CROSS_PLATFORM_TESTING.md` - Platform-specific setup
- `JSON_PARSER_PERFORMANCE_ANALYSIS.md` - Performance metrics
- `examples/JSON_PARSER_SUMMARY.md` - JSON parser details

### Key Files for Understanding

- `src/ProLang/Program.cs` - Compiler entry point
- `src/ProLang/Parse/Lexer.cs` - Tokenization
- `src/ProLang/Parse/Parser.cs` - Parsing to AST
- `src/ProLang/Compiler/Binder.cs` - Symbol binding
- `src/ProLang/Compiler/Emitter.cs` - MSIL emission

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
