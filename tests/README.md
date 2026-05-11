# ProLang Language Tests

This directory contains language feature tests for the ProLang compiler. All tests must pass before changes are merged.

## Directory Structure

```
tests/
├── language/
│   ├── generics/           # Generic struct tests
│   │   ├── single_parameter.prl
│   │   └── multiple_parameters.prl
│   ├── arrays/             # Array syntax tests
│   │   └── array_syntax.prl
│   ├── json-parser/        # JSON parser tests (moved from examples)
│   │   ├── json-parser.prl
│   │   ├── json-parser-basic.prl
│   │   ├── json-parser-v2.prl
│   │   ├── json-parser-working.prl
│   │   ├── json-parser-optimized.prl
│   │   ├── json-parser-utils.prl
│   │   ├── json-demo.prl
│   │   ├── json-data-processor.prl
│   │   ├── test-json-minimal.prl
│   │   └── test-json-v2-simple.prl
│   ├── string-methods/     # String method tests (moved from examples)
│   │   ├── test-string-methods.prl
│   │   ├── test-string-methods-compiler.prl
│   │   ├── test-string-methods-build.prl
│   │   └── phase-b-string-methods.prl
│   ├── lexer/              # Lexer tests (moved from examples)
│   │   └── test-lexer-digits.prl
│   ├── parser/             # Parser tests (moved from examples)
│   │   ├── test-parser-simple.prl
│   │   └── test-parse-result.prl
│   ├── compiler/           # Compiler tests (moved from examples)
│   │   ├── test-simple-compile.prl
│   │   └── test-else-if.prl
│   └── structs/            # Struct definition tests
├── run_tests.sh            # Test runner script
├── run_tests.ps1           # PowerShell test runner script
└── README.md               # This file
```

## Test Categories

### Generics (`language/generics/`)
Tests for generic struct functionality:
- **single_parameter.prl** - Generic structs with one type parameter: `struct Box<T>`
- **multiple_parameters.prl** - Generic structs with multiple type parameters: `struct Pair<A, B>`

### Arrays (`language/arrays/`)
Tests for array type syntax:
- **array_syntax.prl** - Array notation `T[]` parsing

### JSON Parser (`language/json-parser/`) - Moved from examples/
Comprehensive JSON parsing tests:
- **json-parser.prl** - Main JSON parser implementation
- **json-parser-basic.prl** - Basic JSON parsing functionality
- **json-parser-v2.prl** - JSON parser version 2
- **json-parser-working.prl** - Working JSON parser variant
- **json-parser-optimized.prl** - Optimized JSON parser
- **json-parser-utils.prl** - JSON parser utilities
- **json-demo.prl** - JSON parser demonstration
- **json-data-processor.prl** - JSON data processing with parser
- **test-json-minimal.prl** - Minimal JSON test
- **test-json-v2-simple.prl** - Simple JSON v2 test

### String Methods (`language/string-methods/`) - Moved from examples/
String manipulation and method tests:
- **test-string-methods.prl** - Basic string methods
- **test-string-methods-compiler.prl** - String methods with compiler
- **test-string-methods-build.prl** - String methods build test
- **phase-b-string-methods.prl** - Phase B string methods test

### Lexer (`language/lexer/`) - Moved from examples/
Lexer and tokenization tests:
- **test-lexer-digits.prl** - Digit tokenization test

### Parser (`language/parser/`) - Moved from examples/
Parser functionality tests:
- **test-parser-simple.prl** - Simple parser test
- **test-parse-result.prl** - Parse result handling test

### Compiler (`language/compiler/`) - Moved from examples/
Compiler functionality tests:
- **test-simple-compile.prl** - Simple compilation test
- **test-else-if.prl** - If-else statement compilation test

### Structs (`language/structs/`)
Reserved for struct-specific tests (non-generic).

## Running Tests

### On Linux/Mac (Bash)
```bash
bash tests/run_tests.sh
```

### On Windows (PowerShell)
```powershell
powershell -ExecutionPolicy Bypass -File tests\run_tests.ps1
```

Or from PowerShell directly:
```powershell
& '.\tests\run_tests.ps1'
```

### Run Specific Test (All Platforms)
```bash
# Linux/Mac/WSL
dotnet run --project src/ProLang/ProLang.csproj -- tests/language/generics/single_parameter.pl

# Windows (PowerShell)
cd src
dotnet run --project ProLang/ProLang.csproj -- ../tests/language/generics/single_parameter.pl
```

### Check Compilation Only
Tests check for compilation errors. A test passes if the file compiles without syntax/type errors.

## Test Requirements

1. **All tests must compile** - No syntax or semantic errors
2. **Tests must parse correctly** - Valid ProLang syntax
3. **Tests are isolated** - Each test file is independent

## Adding New Tests

1. Create a `.prl` file in the appropriate subdirectory
2. Ensure the file compiles without errors
3. Add a brief comment describing what the test checks
4. Run `tests/run_tests.sh` to verify all tests pass

## Test Expectations

### Current Test Status
**Core Language Tests (3 tests):**
- ✅ Generic structs with single type parameter
- ✅ Generic structs with multiple type parameters
- ✅ Array type syntax (T[])

**Feature Tests (24 tests - moved from examples):**
- JSON Parser (10 tests)
- String Methods (4 tests)
- Lexer (1 test)
- Parser (2 tests)
- Compiler (2 tests)

**Total: 27 language tests**

### Known Limitations
- Generic struct instantiation (e.g., `Box<int> { value: 42 }`) requires additional work beyond parsing
- Generic method binding is in progress
- Print function integration with test framework pending

## CI/CD Integration

Tests should be run:
1. Before committing changes
2. After implementing new features
3. As part of automated CI pipeline

A breaking change is any change that causes tests to fail.

## Debugging Failed Tests

If a test fails:

1. **Check the error message**: Run the specific test to see the compilation error
2. **Review recent changes**: Check if your changes modified syntax parsing or binding
3. **Verify syntax**: Ensure the test file follows ProLang syntax rules
4. **Isolate the issue**: Create a minimal test case to reproduce the error

Example:
```bash
dotnet run --project src/ProLang/ProLang.csproj -- tests/language/generics/single_parameter.pl
```

Check the output for specific error messages and locations.
