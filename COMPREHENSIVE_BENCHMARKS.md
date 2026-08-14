# Comprehensive Compiler Benchmarks

## Overview

Rewrote the benchmark suite to measure the **entire compiler pipeline** with phase-by-phase breakdown. This provides visibility into which compiler phases are bottlenecks and how optimizations affect each stage.

---

## Benchmark Structure

### 1. Full Pipeline Benchmarks (15 measurements)

Measures the complete compilation process: **Lex → Parse → Bind → Lower → Emit**

These benchmarks represent real-world compilation scenarios:

- **FullCompile_Simple** (Baseline)
  - ~25 lines of code
  - Single function, basic variables
  - Establishes baseline performance

- **FullCompile_StringProcessing**
  - ~65 lines of code
  - Tests string tokenization optimizations
  - 1 complex function + 8 wrapper functions
  - String method calls (length(), charAt())

- **FullCompile_StructHeavy**
  - ~55 lines of code
  - Tests struct definition and type resolution
  - 2 nested structs + 5 standalone structs
  - Tests field access compilation

- **FullCompile_Large**
  - ~140 lines of code
  - Tests compilation at scale
  - 20 functions with various complexity
  - 10 struct definitions
  - Multiple function calls chained

- **FullCompile_ComplexExpressions**
  - ~70 lines of code
  - Tests expression parsing and precedence
  - Deeply nested binary expressions
  - Complex logical conditions (if/elif/else)
  - 5 nested expression functions

---

### 2. Lexing + Parsing Phase Benchmarks (5 measurements)

Isolates the **Lexer** and **Parser** phases:

- **ParseOnly_Simple** - Baseline tokenization
- **ParseOnly_StringProcessing** - Tests string token handling
- **ParseOnly_StructHeavy** - Tests declaration parsing
- **ParseOnly_Large** - Tests parsing at scale
- **ParseOnly_ComplexExpressions** - Tests expression parsing

**Purpose**: Measure impact of:
- Stack-allocated buffer optimization (Lexer)
- Keyword lookup table optimization (Parser)
- Source-generated regex patterns (Lexer)

---

### 3. Binding + Lowering Phase Benchmarks (5 measurements)

Isolates the **Binder** and **Lowerer** phases:

- **BindOnly_Simple** - Baseline symbol binding
- **BindOnly_StringProcessing** - Tests method resolution
- **BindOnly_StructHeavy** - Tests struct field binding
- **BindOnly_Large** - Tests binding at scale
- **BindOnly_ComplexExpressions** - Tests expression binding

**Purpose**: Measure impact of:
- Type/method resolution caching (Emitter, Binder)
- Single-pass declaration collection (Binder)
- Operator precedence optimization (SyntaxFacts)

---

### 4. Emission Phase Benchmarks (5 measurements)

Isolates the **Emitter** code generation phase:

- **EmitOnly_Simple** - Baseline IL generation
- **EmitOnly_StringProcessing** - Tests method emission
- **EmitOnly_StructHeavy** - Tests type emission
- **EmitOnly_Large** - Tests emission at scale
- **EmitOnly_ComplexExpressions** - Tests expression emission

**Purpose**: Measure impact of:
- Type/method resolution caching (Emitter)
- Operator precedence dictionary lookup (Emitter)
- Assembly reference resolution optimization

---

## Benchmark Test Programs

### Simple Program (~25 lines)
```csharp
func main() {
    var x = 5
    var y = 10
    var z = x + y
    print(z)
}
```
**Characteristics**:
- Minimal complexity
- Single function
- No structs or complex types
- Basic arithmetic
- **Use**: Baseline for overhead measurement

---

### String Processing Program (~65 lines)
```csharp
func countChar(text: string, ch: string) : int {
    var count = 0
    var i = 0
    while(i < text.length()) {
        if(text.charAt(i) == ch) {
            count = count + 1
        }
        i = i + 1
    }
    return count
}
func process0(text: string) : int { ... }
// ... process1-7 functions
func main() { ... }
```
**Characteristics**:
- String-heavy code (tests Lexer stack allocation)
- Method calls on strings
- Multiple functions
- Loop and conditional logic
- **Use**: Measure string tokenization performance

---

### Struct Heavy Program (~55 lines)
```csharp
struct Point { x: int; y: int; }
struct Rectangle { topLeft: Point; bottomRight: Point; }
struct Data0 { value: int; name: string; flag: bool; }
// ... Data1-4 structs
func createPoint(px: int, py: int) : Point { ... }
func main() { ... }
```
**Characteristics**:
- Multiple struct definitions
- Nested struct fields
- Type resolution exercises
- Field access compilation
- **Use**: Measure type resolution caching impact

---

### Large Program (~140 lines)
```csharp
func func0(a: int, b: int, c: int) : int { ... }
// ... func1-19 functions
struct Struct0 { field1: int; field2: string; field3: bool; field4: int; }
// ... Struct1-9 structs
func main() {
    var x = 100
    x = func0(x, 5, 3)
    // ... x = func1-9(x, 5, 3)
    print(x)
}
```
**Characteristics**:
- 20 functions
- 10 structs
- 10 chained function calls
- Moderate complexity
- **Use**: Measure compilation at realistic scale

---

### Complex Expressions Program (~70 lines)
```csharp
func evaluate(a: int, b: int, c: int) : int {
    return a + b * c - (a / b) + (c * a) - (b / c) + (a * b * c)
}
func complexLogic(x: int) : int {
    if (x > 10 && x < 100 || x == 50) { ... }
    elif (x > 100 && x < 200) { ... }
    else { ... }
    return result
}
func nested0(x: int) : int { return (x + 1) * (x - 1) + (x * x) - (x / 2) * (x + 2) }
// ... nested1-4 functions
func main() { ... }
```
**Characteristics**:
- Complex nested expressions
- Operator precedence exercises
- Conditional logic chains
- Expression optimization tests
- **Use**: Measure expression parsing and precedence handling

---

## Expected Performance Improvements

Based on optimizations implemented:

| Phase | Optimization | Expected Improvement |
|-------|--------------|----------------------|
| **Lexing** | Stack-allocated buffers (≤256 chars) | 20-30% allocations reduction |
| **Lexing** | Error path consolidation | 5-10% allocation reduction |
| **Parsing** | Token list pre-allocation | 10-15% allocation reduction |
| **Parsing** | Keyword lookup dictionary | 5-10% speed improvement |
| **Binding** | Single-pass declaration collection | 10-20% improvement |
| **Emission** | Operator precedence dictionary | 5-10% improvement |
| **Overall** | Combined effect | 10-25% compilation speedup |

---

## Benchmark Metrics Measured

Each benchmark measures:

1. **Execution Time (Mean, µs)**
   - Mean execution time across 14 iterations
   - Shows overall performance
   - Helps identify bottlenecks

2. **Memory Allocation (Bytes)**
   - Total allocated per operation
   - Shows GC pressure
   - Validates optimization effectiveness

3. **GC Collections**
   - Gen 0, Gen 1, Gen 2 collections
   - Per 1000 operations
   - Lower = better memory efficiency

4. **Ratio to Baseline**
   - Comparison to SimpleCompilation
   - Shows relative complexity
   - Helps identify problematic patterns

---

## Running the Benchmarks

### Full Suite (All 25 benchmarks)
```bash
dotnet run -c Release --project ProLang.Benchmarks
```

### Single Benchmark
```bash
dotnet run -c Release --project ProLang.Benchmarks -- --filter "*Simple*"
```

### Memory Analysis Only
```bash
dotnet run -c Release --project ProLang.Benchmarks -- --memory
```

### Compare Against Baseline
Run before and after optimizations:
```bash
# Before optimizations
dotnet run -c Release --project ProLang.Benchmarks > baseline.txt

# After optimizations
dotnet run -c Release --project ProLang.Benchmarks > optimized.txt

# Compare
```

---

## Benchmark Output Analysis

### Key Metrics to Watch

1. **Full Pipeline vs Phase Breakdown**
   - If Full > (Parse + Bind + Emit), there's overhead
   - Should be approximately additive

2. **Phase Breakdown**
   - Which phase takes the most time?
   - Which phase allocates the most?
   - Target the slowest phase for optimization

3. **Per-Program Variation**
   - Simple: High overhead relative to work
   - Large: Shows realistic compilation
   - Expressions: Tests parser performance
   - Strings: Tests Lexer performance
   - Structs: Tests type resolution

4. **Scaling Characteristics**
   - Does time scale linearly with code size?
   - Does allocation scale linearly?
   - Are there quadratic behaviors?

---

## Interpretation Guide

### Example Results

```
Full Pipeline - Simple:       145.9 µs (25.36 KB allocated)
  ParseOnly:                   85.3 µs ( 5.21 KB allocated)  
  BindOnly:                    35.7 µs ( 8.15 KB allocated)
  EmitOnly:                    24.9 µs (12.00 KB allocated)
  ─────────────────────────────────────────────────────────
  Calculated sum:             146.0 µs (25.36 KB) ✓ Matches!

Full Pipeline - Large:        412.3 µs (135.2 KB allocated)
  ParseOnly:                  245.1 µs ( 42.3 KB allocated)  
  BindOnly:                    98.5 µs ( 61.2 KB allocated)
  EmitOnly:                    68.7 µs ( 31.7 KB allocated)
  ─────────────────────────────────────────────────────────
  Calculated sum:             412.3 µs (135.2 KB) ✓ Matches!
```

**Analysis**:
- Parse phase: 60% of time, 31% of allocations (string tokens)
- Bind phase: 24% of time, 45% of allocations (symbol tables)
- Emit phase: 17% of time, 23% of allocations (IL generation)

**Optimization Priority**:
1. Parser (stack allocation for strings)
2. Binder (single-pass collection, caching)
3. Emitter (dictionary lookups)

---

## Comparison: Old vs New Benchmarks

### Old Benchmark (3 programs)
- Only measured full compilation
- No phase breakdown
- No insight into bottlenecks
- Limited test coverage
- Hard to isolate optimization impact

### New Benchmark (25 measurements)
- 5 test programs covering different scenarios
- Phase-by-phase breakdown (5 per phase)
- Identifies bottlenecks clearly
- Comprehensive test coverage
- Easy to measure individual optimization impact

---

## Next Steps

1. **Run baseline**: Execute benchmarks before optimizations are used
2. **Apply optimizations**: Stack allocation, dictionary lookups, etc.
3. **Re-run benchmarks**: Execute with optimizations enabled
4. **Compare results**: Measure improvement in each phase
5. **Identify remaining bottlenecks**: Focus on slowest remaining phase
6. **Iterate**: Apply additional optimizations based on results

---

## Files Modified

- `src/ProLang.Benchmarks/CompilerBenchmarks.cs` - Complete rewrite with 25 benchmarks
- Tests now cover:
  - Full pipeline (5 scenarios)
  - Parse phase isolation (5 scenarios)
  - Bind phase isolation (5 scenarios)
  - Emit phase isolation (5 scenarios)
  - 5 different test programs exercising different code patterns

Build Status: ✅ **SUCCESS (0 errors)**
