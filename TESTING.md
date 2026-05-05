# ProLang Testing Guide

## Overview

This document describes the testing strategy for ProLang compiler changes. All changes must pass the language test suite before being merged.

## Test Suite Organization

Located in `tests/` directory:

```
tests/
├── language/                 # Language feature tests
│   ├── generics/            # Generic struct tests
│   ├── arrays/              # Array type syntax tests
│   └── structs/             # Struct definition tests
├── run_tests.sh             # Test runner script
└── README.md                # Test documentation
```

## Running Tests

### Linux/Mac (Bash)
```bash
bash tests/run_tests.sh
```

### Windows (PowerShell)
```powershell
powershell -ExecutionPolicy Bypass -File tests\run_tests.ps1
```

Or directly in PowerShell:
```powershell
& '.\tests\run_tests.ps1'
```

Expected output (same on all platforms):
```
==========================================
ProLang Language Test Suite
==========================================

✓ PASSED - language/generics/single_parameter.pl
✓ PASSED - language/generics/multiple_parameters.pl
✓ PASSED - language/arrays/array_syntax.pl

==========================================
Test Summary
==========================================
Total:  3
Passed: 3
Failed: 0

✓ All tests passed!
```

### Run Specific Test

**Linux/Mac/WSL:**
```bash
cd src
dotnet run -c Release --project ProLang/ProLang.csproj -- ../tests/language/generics/single_parameter.pl
```

**Windows (PowerShell):**
```powershell
cd src
dotnet run -c Release --project ProLang\ProLang.csproj -- ..\tests\language\generics\single_parameter.pl
```

## Test Coverage

### Current Tests (3 total)

#### Generics (`tests/language/generics/`)
- ✅ **single_parameter.pl** - Tests parsing of generic structs with one type parameter
  - Tests: `struct Box<T> { value: T; }`
  - Validates: Type parameter syntax, struct field declarations with type parameters
  
- ✅ **multiple_parameters.pl** - Tests parsing of generic structs with multiple type parameters
  - Tests: `struct Pair<A, B> { ... }`, `struct Triple<X, Y, Z> { ... }`
  - Validates: Multiple type parameter syntax, comma-separated parameter lists

#### Arrays (`tests/language/arrays/`)
- ✅ **array_syntax.pl** - Tests array notation parsing and binding
  - Tests: Array type syntax support (foundation for `T[]` notation)
  - Validates: Type parsing with array considerations

## Test Success Criteria

A test **PASSES** if:
1. ✅ File parses without syntax errors
2. ✅ Type system resolves correctly (generic types bind properly)
3. ✅ No structural compilation errors

A test **FAILS** if:
1. ❌ Syntax errors: `Unexpected token`
2. ❌ Type errors: `Variable 'X' does not exist` (for type references)
3. ❌ Binding errors: `Type 'X' not found`
4. ❌ Duplicate declarations: `Symbol already declared`

Note: Missing built-in functions (e.g., `print`) do not cause test failure as they are infrastructure, not language features.

## Feature Coverage Matrix

| Feature | Single Param | Multiple Params | Arrays | Type Binding | Comments |
|---------|:---:|:---:|:---:|:---:|----------|
| Generic Struct Declaration | ✅ | ✅ | - | ✅ | Core feature working |
| Type Parameters | ✅ | ✅ | - | ✅ | Parsing and binding working |
| Array Type Syntax | - | - | ✅ | ✅ | Foundation in place |
| Generic Instantiation | - | - | - | ⚠️ | Parsed, binding in progress |
| Generic Field Access | - | - | - | ⚠️ | Foundation laid, not fully tested |

Legend: ✅ = Working, ⚠️ = Partial/In Progress, ❌ = Not implemented, - = N/A

## Before Committing Changes

1. **Run the test suite**:
   ```bash
   bash tests/run_tests.sh
   ```

2. **Ensure all tests pass**: If a change causes a test to fail, it's a breaking change

3. **Identify affected tests**: Note which tests validate your change

4. **Add tests for new features**: If adding a new feature, add corresponding tests

## CI/CD Integration

The test suite is designed to be run in CI/CD pipelines:

```yaml
# Example GitHub Actions workflow
- name: Run Language Tests
  run: bash tests/run_tests.sh
  
- name: Check Test Results
  if: failure()
  run: echo "Language tests failed - breaking change detected"
```

## Adding New Tests

1. Create a `.pl` file in the appropriate `tests/language/*/` directory
2. Include a comment describing what feature is being tested
3. Ensure the test parses and binds correctly
4. Run `bash tests/run_tests.sh` to verify
5. Commit both the test file and any documentation changes

Example:
```prolang
// Test: Generic struct with constraint (future feature)
struct Container<T> {
    items: T;
}

func main() {
    let x = 5
}
```

## Maintenance

### When Tests Break

If a test fails after a change:

1. **Identify the root cause**:
   ```bash
   cd src
   dotnet run -c Release --project ProLang/ProLang.csproj -- ../tests/language/generics/single_parameter.pl
   ```

2. **Determine if it's a breaking change**:
   - ✅ Intentional: Update the test or feature
   - ❌ Unintentional: Revert the change or fix the bug

3. **Update related documentation** if behavior changes

### Expanding Test Coverage

Areas for future test expansion:
- [ ] Generic struct instantiation with concrete types
- [ ] Array of generic types: `Box<int>[]`
- [ ] Generic field access and type substitution
- [ ] Nested generics: `Box<Pair<int, string>>`
- [ ] Generic method definitions and calls
- [ ] Type parameter constraints (future feature)
- [ ] Variance and covariance (future feature)

## Debugging Test Failures

### Step 1: Run the failing test
```bash
cd src
dotnet run -c Release --project ProLang/ProLang.csproj -- ../tests/language/generics/single_parameter.pl
```

### Step 2: Identify the error
Look for error messages in format:
```
path/file.pl(line,col,line,col): Error type: Description
```

### Step 3: Check parser output
Add diagnostic output to understand parse tree:
```csharp
// In Binder.cs
var syntaxTree = SyntaxTree.Parse(source);
// Add breakpoint or logging here
```

### Step 4: Verify against recent changes
```bash
git diff HEAD
```

Check if your changes could cause the error type reported.

## Performance Considerations

Tests are compiled in Release mode for realistic performance:
```bash
dotnet run -c Release --project ProLang/ProLang.csproj -- test.pl
```

This ensures optimization differences don't hide real issues.

## Summary

- **Test Framework**: File-based, compilation-focused
- **Run Command**: `bash tests/run_tests.sh`
- **Success Criteria**: Parse and type bind successfully
- **Maintenance**: Add tests for new features, verify tests pass before committing
- **Coverage**: Currently 3 tests covering generics and arrays; expand as features mature
