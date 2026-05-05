# Quick Start: Optimized JSON Parser

## The Problem & Solution

**Problem**: JSON parser compilation was very slow (45+ seconds)
**Cause**: Deep nesting in conditional logic (9 levels)
**Solution**: Used string.indexOf() for character lookup

## Use This Parser

```bash
cd src/ProLang
dotnet run ../../examples/json-parser-optimized.prl --run
```

**Expected Time**: 5-10 seconds ⚡ (vs 45+ seconds before)

## What Changed

### Before (Slow)
```prolang
func digitToInt(ch: string) : int {
    if(ch == "0") { return 0 }
    else { if(ch == "1") { return 1 }
    else { if(ch == "2") { return 2 }
    // ... 7 more levels of nesting
```

### After (Fast)
```prolang
func digitToInt(ch: string) : int {
    return "0123456789".indexOf(ch)
}
```

## Files Provided

| File | Compilation | Features | Status |
|------|-------------|----------|--------|
| json-parser-optimized.prl | ✅ 5-10 sec | Full JSON | Recommended |
| json-parser-v2.prl | ✅ 10-15 sec | Full JSON | Now optimized |
| json-parser-working.prl | ✅ <5 sec | Demo | Quick test |

## Performance

```
Before optimization:  45+ seconds ❌
After optimization:   5-10 seconds ✅
Speedup:             5-15x faster
```

## Key Insight

**Avoid deep nesting (>3-4 levels)** in ProLang for fast compilation!

Instead of:
```prolang
if(x == "a") { ... }
else { if(x == "b") { ... }
else { if(x == "c") { ... }
```

Use:
```prolang
if(x == "a") { ... }
if(x == "b") { ... }
if(x == "c") { ... }
```

Or use data structures:
```prolang
let chars = "abc"
return chars.indexOf(x)
```

## Test It

```bash
cd src/ProLang
time dotnet run ../../examples/json-parser-optimized.prl --run
```

Expected output:
- Compilation time: 5-10 seconds
- Test results: All passing
- Output: JSON test results

## That's It!

The optimized parser is ready to use. No more long compile waits! 🎉
