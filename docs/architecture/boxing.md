# Boxing in the .NET backend

Where it comes from, how much of it is avoidable, and why introducing reference types is not the
answer.

Measured against the 34-program snapshot corpus on 2026-08-15.

> **Status: fixes 1–3 are implemented.** Boxing across the corpus went from **196 to 27**, an 86%
> reduction, with all 108 tests passing unchanged. `chip-8` went from 68 to **0**, `ring-buffer`
> from 47 to **0**. What remains traces to `any` written in the source — the JSON parser's
> `value: any` field and `parseResult(val: any, …)` — plus explicit casts. See
> [Outcome](#outcome) at the end.

---

## The data

**196 `box` instructions across the corpus.** What consumes them:

| Consumer | Count | Share | Source construct |
|---|---:|---:|---|
| `callvirt Object::ToString()` | 141 | **72%** | `string(x)` |
| `call Output::Write(object)` | 18 | 9% | `print(x)` |
| `call String::Concat(object, object)` | 10 | 5% | `"a" + x` |
| user functions taking `any` | 3 | 2% | genuine `any` in a signature |

Worst offenders: `chip-8` (68), `ring-buffer` (47), `json-parser-tests` (13).

**Roughly 86% of all boxing comes from three builtins, none of which the programmer asked to be
dynamic.**

## Root cause

`any` is `System.Object`. Three builtins declare `any` parameters:

```csharp
// Symbols/BuiltInFunctions.cs
Print       = new("print",  [new ParameterSymbol("text", TypeSymbol.Any, 0)], TypeSymbol.Void);
ArrayLength = new("length", [new ParameterSymbol("arr",  TypeSymbol.Any, 0)], TypeSymbol.Int);
```

So the binder inserts a conversion to `any` around the argument, and the emitter faithfully boxes
it. Same for `string(x)`, which lowers to a conversion whose target is `string`:

```csharp
// Compiler/Emitter.cs — EmitConversionExpression
if (toType == TypeSymbol.String)
{
    if (IsValueType(fromType))
        scope.IL.Emit(OpCodes.Box, GetTypeReference(fromType));   // ← allocation

    if (fromType != TypeSymbol.String)
        scope.IL.Emit(OpCodes.Callvirt, /* Object::ToString */);
}
```

Every `string(someInt)` in a loop allocates. `chip-8` does this 68 times per frame.

**The static type was never lost.** It is right there on `node.Expression.Type`; the emitter throws
it away by boxing to the declared parameter type instead of dispatching on it.

## The C backend already solves this

The same bound tree, through `--emit-c`, produces **zero** boxing:

```
119  prl_int_to_string
 21  prl_bool_to_string
  0  prl_any_to_string
  0  (void*) casts
```

Because `CEmitter.EmitStringCoerce` does two things the MSIL emitter does not:

```csharp
private void EmitStringCoerce(BoundExpression expr)
{
    expr = UnwrapAny(expr);          // 1. see through the binder's conversion to `any`

    var fn = expr.Type == TypeSymbol.Bool ? "prl_bool_to_string"
           : IsIntegerType(expr.Type)     ? "prl_int_to_string"     // 2. dispatch on the real type
           : IsFloatType(expr.Type)       ? "prl_float_to_string"
           : "prl_any_to_string";         //    …and only fall back when it genuinely is `any`
```

Its own comment states the insight: *"The binder inserts an implicit conversion/cast to `any`
around values passed to print(), length(), etc. Look through it to reach the underlying typed
value."*

That is the whole fix. The C backend recovers the static type; the MSIL backend does not.

---

## Should reference types be introduced?

**They since have been — as `class`, documented in `memory.md` — but not for this. The conclusion
below still stands: reference types remove none of these 196 boxes.**

Boxing here is not caused by structs being value types. It is caused by `int`, `bool`, and the
sized integers being converted to `System.Object` because a builtin's parameter is declared `any`.
A `class` keyword changes how *user aggregates* are represented; it does nothing about primitives
flowing into `any`, which is where all the cost is. A program that declares every one of its types
as a class boxes exactly as much as it did before.

The costs this section anticipated were real, and two of the three landed as predicted:

- **GC pressure.** Confirmed, and the reason `std/ui` keeps its integer arena: one allocation per
  frame is better than one per node, and expressiveness is not a reason to give that up where the
  arena already works.
- **Null and identity.** Confirmed. `a = b` then mutating `b.x` does change `a.x`, and null
  dereference is a runtime failure. That is the price of the feature, paid deliberately.
- **The C backend.** This one was wrong, or rather it asked for more than was needed. It assumed
  reference types imply ownership, lifetimes, and either a collector or manual `free`. They do not —
  they imply *allocation*, and the C backend already had an allocator: the process-lifetime bump
  arena in `native/prl_memory.h` that every array, string concatenation and file read already uses
  and never frees. A class is a pointer into it under exactly the same contract. No collector, no
  ownership, no new failure mode; only a higher allocation rate.

So the conclusion to carry forward is the narrow one. Reference types are for **recursive shapes and
shared mutable aggregates**, which is what they were eventually added for. They were never a fix for
boxing, and adding them did not make one.

---

## Recommended fixes, by impact

### 1. Type-directed `string(x)` — removes ~72%

Replace the unconditional `box` + `Object::ToString()` with a call selected from the operand's
static type, mirroring `EmitStringCoerce`:

| ProLang type | Emit |
|---|---|
| `int`, sized ints | `call ProLang.Runtime.StringOps.From(int)` |
| `bool` | `call ProLang.Runtime.StringOps.From(bool)` |
| `float32`/`float64` | `call ProLang.Runtime.StringOps.From(double)` |
| `string` | nothing |
| genuinely `any` | `box` + `ToString()`, as today |

The overloads go in `ProLang.Runtime.StringOps` as ordinary C#, so culture handling lives in one
readable place rather than being implicit in `Object::ToString`.

An alternative is IL's `constrained.` prefix — `constrained. int32 callvirt Object::ToString()`
calls `Int32.ToString()` directly with no allocation. It avoids adding overloads, but needs a
managed pointer, so the value must first go into a local. The overload approach is simpler, keeps
the emitter table-driven, and matches what the C backend already does.

### 2. Overload `print` — removes ~9%

`Output.Write(object)` becomes a set of overloads (`int`, `long`, `bool`, `double`, `string`,
`object`). The emitter picks by the argument's static type after unwrapping the conversion to
`any`. `IntrinsicRegistry` already dispatches on the `FunctionSymbol`, so this is a small change
to one entry rather than a new mechanism.

### 3. `String.Concat(string, string)` — removes ~5%

Once (1) exists, a string concatenation whose operands are statically known converts each side to
`string` and calls `Concat(string, string)`. No boxing, and it avoids `Concat`'s internal
`ToString` dispatch too.

### 4. Stop erasing `length` to `any`

`ArrayLength` declares `arr: any`, so every `length(arr)` boxes the array reference. Arrays are
already reference types so the box is a no-op at runtime, but it clutters the IL and defeats the
`ldlen` fast path when the argument is an `any`. Giving `length` a properly typed signature — or
overloads for `array<T>` and `string` — removes the conversion entirely.

**After all four, boxing survives only where a value genuinely flows into a declared `any`** — a
`map<string, any>`, an `array<any>`, a JSON value, or a user function whose parameter is `any`.
Which is exactly the stated goal: boxing only when it is written in the language.

---

## Scope: the C backend

There is no boxing in C, but the analogous cost is the `any` representation: `void*` plus a heap
allocation through `prl_any_to_string`. As measured above, **generated C currently uses none of
it** — `EmitStringCoerce` specialises everything.

Two things to keep in view:

- **`UnwrapAny` is load-bearing and only applied on the string-coercion path.** Other places that
  consume an `any`-declared parameter still emit `(void*)` casts. Worth auditing if more builtins
  gain `any` signatures.
- **If fixes 1–4 land, the two backends converge.** Both would dispatch on the recovered static
  type, and the shared logic — "unwrap the conversion, then select by type" — could move into the
  bound tree itself as a lowering step, so neither backend has to rediscover it. That is the
  cleaner long-term shape: the binder stops inserting a conversion to `any` when it can resolve a
  typed overload, and both backends simply emit what they are given.

---

## Outcome

Fixes 1–3 are implemented. Fix 4 turned out to be unnecessary.

| Stage | `box` count |
|---|---:|
| Before | 196 |
| After fix 1 (`string(x)`) and fix 2 (`print`) | 37 |
| After fix 3 (`Concat(string, string)`) | **27** |

Per program, the heaviest users are now clean:

| Program | Before | After |
|---|---:|---:|
| `chip-8` | 68 | **0** |
| `ring-buffer` | 47 | **0** |
| `02_operators` | 12 | **0** |
| `03_control_flow` | 5 | **0** |
| `json-parser-tests` | 13 | 11 |
| `json-parser` | 10 | 9 |

All 108 tests pass unchanged, including the 20 execution tests that assert program output — the
runtime overloads call the same `ToString()` the virtual dispatch reached, so the formatted text
is identical.

### What the remaining 27 are

Every one traces to `any` written in the source or to an explicit cast:

- **`json-parser` / `json-parser-tests` (20)** — `value: any` on the `JsonValue` struct and
  `parseResult(val: any, pos: int)`. A JSON parser is dynamically typed by nature.
- **`cast-*` tests (5)** — deliberate `as` casts through `any`.
- **`02_message_box` (1)** — a value crossing into .NET interop as `object`.

Which was the goal: boxing only where the language file asks for it.

### Why fix 4 was not needed

`ArrayLength` does declare `arr: any`, but arrays are already reference types, so
`EmitConversionExpression` never boxed them — `IsValueType` excludes array types. The `any`
signature costs nothing at runtime here. Giving `length` a precise signature is still worth doing
for clarity, but it is not a boxing fix.

### Implementation notes

- `Emitter.RuntimeOverloadParameter` maps a ProLang type to the metadata name of the runtime
  overload that takes it unboxed. It is the single place the mapping lives, shared by `string(x)`,
  `print`, and string concatenation.
- `int8`, `uint8`, `int16`, and `uint16` all route to the `int` overload: the IL evaluation stack
  has nothing narrower than `int32`, so they are already sitting there in the right form.
  `uint32` needs its own overload — same bit pattern, but `int32` would render values above 2^31
  as negative. `float32` needs its own too, because `Single.ToString()` and `Double.ToString()`
  disagree on the same value.
- `Emitter.UnwrapConversionToAny` is the .NET counterpart of `CEmitter.UnwrapAny`. It unwraps
  *only* conversions whose target is `any`; a numeric widening or a cast the program wrote is
  left alone.

### Still open

The two backends now do the same thing by different means — `UnwrapConversionToAny` in one,
`UnwrapAny` in the other, each with its own type-to-target table. Moving the decision into the
binder, so it stops inserting a conversion to `any` when a typed overload resolves, would let
both delete their copy. That is a binder change rather than a backend one, and worth doing before
a third backend needs the same trick.
