# ProLang Language Enhancement - Implementation Guide

## Overview

This guide provides step-by-step instructions for implementing string methods in ProLang, enabling text parsing and JSON processing capabilities.

## Phase A: Complete ✅
- ✅ Explored ProLang syntax and capabilities
- ✅ Identified language limitations  
- ✅ Created working JSON data processor demo (`json-demo.prl`)
- ✅ Documented practical limitations and workarounds

## Phase B: In Progress
Implement string methods in ProLang compiler/interpreter to enable full JSON parsing.

### Recommended Implementation Order

#### Step 1: Add String Methods to Interpreter (Easiest)
**File**: `src/ProLang/Interpreter/Evaluator.cs`
**Time**: 1-2 hours
**Complexity**: Low - just add explicit cases

Add to `EvaluateCallExpression` method (around line 430):
- `"length"` - return `((string)args[0]).Length`
- `"charAt"` - return `((string)args[0])[index].ToString()`
- `"substring"` - return `((string)args[0]).Substring(start, end-start)`
- `"indexOf"` - return `((string)args[0]).IndexOf(needle)`

**Test**: Run with `--run` flag to test interpreter

#### Step 2: Define String Methods in Symbol System
**Files**: 
- `src/ProLang/Symbols/BuiltInFunctions.cs`
- `src/ProLang/Intermediate/Binder.cs`

**Time**: 2-3 hours
**Complexity**: Medium - follow existing array method pattern

1. Add function symbols to `BuiltInFunctions.cs`:
```csharp
public static readonly FunctionSymbol StringLength = new("length",
    ImmutableArray.Create(new ParameterSymbol("str", TypeSymbol.String, 0)), 
    TypeSymbol.Int);
// ... etc for charAt, substring, indexOf
```

2. Create `StringMethods` dictionary in `Binder.cs` (mirroring `ArrayMethods`)

3. Update `BindMethodCallExpression` to handle string method calls

**Test**: Compile ProLang code using string methods

#### Step 3: Implement IL Emission (Hardest)
**File**: `src/ProLang/Compiler/Emitter.cs`
**Time**: 3-4 hours
**Complexity**: High - requires IL knowledge

Add IL generation for string methods in `EmitCallExpression`:
- Map ProLang string methods to .NET IL instructions
- Example: `.length()` → `Callvirt` to `String.get_Length`

**Test**: Compile to `.dll` and verify IL correctness

#### Step 4: Fix Lexer Issues
**File**: `src/ProLang/Parse/Lexer.cs`
**Time**: 1-2 hours
**Complexity**: Medium

Optional improvements:
- Allow identifiers ending with numbers (change `IsLetter` to `IsLetterOrDigit`)
- Add proper escape sequence handling (`\n`, `\t`, etc.)

**Test**: Parse ProLang code with escaped strings and `person1` style names

#### Step 5: Add `else if` Support (Nice to Have)
**File**: `src/ProLang/Parse/Parser.cs`
**Time**: 2-3 hours
**Complexity**: Medium

Parse `else if` as syntactic sugar instead of requiring nested blocks.

**Test**: Compile ProLang with `else if` clauses

## Quick Start: Implementing String Methods

### Minimal Implementation (Step 1 Only)
If you only implement in the interpreter:

```csharp
// In Evaluator.cs, add to EvaluateCallExpression switch:

case "length":
    return ((string)args[0]).Length;

case "charAt":
    var str = (string)args[0];
    var idx = (int)args[1];
    return idx >= 0 && idx < str.Length ? str[idx].ToString() : "";

case "substring":
    var str = (string)args[0];
    var start = (int)args[1];
    var end = (int)args[2];
    return start >= 0 && end <= str.Length && start <= end 
        ? str.Substring(start, end - start) 
        : "";

case "indexOf":
    var str = (string)args[0];
    var needle = (string)args[1];
    var pos = str.IndexOf(needle);
    return pos >= 0 ? pos : -1;
```

**Result**: String methods work with `dotnet run examples/json-parser.prl --run`

### Full Implementation (All 5 Steps)
Complete implementation supports both interpreter and compiled modes.

## Testing Checklist

- [ ] `string.length()` works in interpreter
- [ ] `string.charAt(0)` returns first character
- [ ] `string.substring(0, 3)` extracts first 3 characters
- [ ] `string.indexOf("x")` finds substring or returns -1
- [ ] JSON parser can be fully implemented
- [ ] All existing tests still pass
- [ ] New string method tests have 90%+ coverage
- [ ] Compiled `.dll` files work correctly

## Expected Code Changes

| File | Changes | Lines |
|------|---------|-------|
| `BuiltInFunctions.cs` | Add 4 function symbols | +15 |
| `Binder.cs` | Add StringMethods dict + binding logic | +30 |
| `Evaluator.cs` | Add switch cases for string methods | +40 |
| `Emitter.cs` | Add IL emission for strings | +50 |
| `Lexer.cs` | Fix identifier/escape handling | +20 |
| `Parser.cs` | Add else if support | +15 |

**Total Lines of Code**: ~170 lines of new code
**Total Files Modified**: 6 core files

## References

- **Array method implementation**: `Binder.cs` lines 1082-1145
- **Function evaluation**: `Evaluator.cs` lines 330-457  
- **IL emission**: `Emitter.cs` lines 735-811
- **String tokenization**: `Lexer.cs` lines 326-367

## Next Steps

1. Choose implementation approach:
   - [ ] **Option A**: Interpreter-only (quick, enables `--run` mode)
   - [ ] **Option B**: Full implementation (complete, supports compilation)

2. Pick starting point:
   - [ ] Start with Step 1 (interpreter, fastest feedback)
   - [ ] Start with Step 2 (symbol system, follows architecture)

3. Create feature branch: `feature/string-methods`

4. Implement incrementally, testing after each step

## Success Criteria for Phase B

✅ String methods work in both interpreter and compiler
✅ JSON parser fully implementable in ProLang
✅ All limitations from Phase A are resolved
✅ Backward compatibility maintained
✅ Comprehensive tests added

## Estimated Timeline

| Phase | Effort | Timeline |
|-------|--------|----------|
| Interpreter Methods | 1-2 hrs | Day 1 |
| Symbol System | 2-3 hrs | Day 1-2 |
| IL Emission | 3-4 hrs | Day 2-3 |
| Lexer Fixes | 1-2 hrs | Day 3 |
| Else-if Support | 2-3 hrs | Day 4 |
| Testing & Docs | 2-3 hrs | Day 4-5 |
| **Total** | **11-17 hours** | **~1 week** |

## Common Pitfalls to Avoid

1. **Forgetting to bind string methods** - Need entries in both symbol system AND evaluator
2. **Type mismatch in IL** - String methods return strings, not objects
3. **Out of bounds errors** - Always check indices before accessing
4. **Breaking array methods** - String and array methods use different code paths
5. **Escape sequence conflicts** - Test `""` quote escaping carefully

## Questions?

Refer to:
- Array method implementation for patterns
- ProLang examples in `/examples/` for usage patterns
- Compiler architecture docs in this repo
