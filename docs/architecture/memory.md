# Memory model

ProLang has two kinds of user-defined aggregate, and they differ in exactly one way: whether a value
of that type *is* the data or *refers to* it.

```prolang
struct Point { x: int, y: int }     // a value — assigning copies it
class  Node  { value: int, next: Node }   // a reference — assigning aliases it
```

Everything else about them is the same. Same field syntax, same creation expression, same generics,
no methods on either.

## Which to reach for

Use `struct` by default. It is a plain aggregate with no allocation, no identity and no null, and it
is what every existing ProLang program is built from.

Use `class` when the data has *identity* — when two names should see each other's writes, or when a
type has to refer to itself:

- **Recursive shapes.** A struct cannot contain itself by value; that has no finite size and is a
  compile error naming the cycle. A linked list, a tree, an AST, an environment chain: all classes.
- **Shared mutable state.** A struct passed to a function is copied, so assigning to a scalar field
  of the parameter is invisible to the caller. This is the single most common ProLang trap, and it
  is why so much of `std/` keeps its counters in an `array<int>` — writes *through* an array
  reference are visible where writes to a scalar field are not. A class removes the need for that.

```prolang
func bump(c: Counter) : void {
    c.count = c.count + 1     // visible to the caller iff Counter is a class
}
```

## Aliasing is the trade

The cost of a class is that assignment no longer copies:

```prolang
let a = Counter { count: 1 }
let b = a
b.count = 5
// a.count is now 5
```

The only cue distinguishing that from the struct behaviour is the declaration keyword, which may be
in another file. Hover shows `class Counter` or `struct Counter` for this reason.

## `null`

A class variable may hold `null`, and a class field left unwritten starts as `null`. Dereferencing
one is a runtime failure, not a compile error — there is no definite-assignment analysis and no
non-nullable reference type.

`null` converts to a class type and to `any`. It deliberately does **not** convert to `string`,
`array<T>` or `map<K, V>`, even though all three are nullable underneath; those stay non-null so that
existing string- and array-heavy code keeps its guarantees. `let x = null` is an error, because there
is nothing to infer — write the type.

Comparing a value type against `null` is an error rather than a constant `false`, on the grounds that
it is always a mistake.

## Lifetime: the backends genuinely differ

This is the part to read before writing an allocation-heavy program.

| | `--emit-msil` (.NET) | `--emit-c` / `--emit-psp` |
|---|---|---|
| where a class lives | the CLR heap | the runtime arena, `native/prl_memory.h` |
| reclaimed? | **yes**, by the tracing GC | **no**, never |

On .NET a class that becomes unreachable is collected, and a program may allocate indefinitely. On
the C and PSP backends every allocation comes from one process-lifetime bump arena and nothing is
ever freed — `Arena_Pop_To` exists in `native/arena.h` and has no callers.

So the same source, with the same semantics, has different memory behaviour per target. A loop
allocating per iteration is fine under `--emit-msil` and will exhaust the arena under `--emit-c`.
Nothing in the type system expresses this.

Note that this is not new with classes. Every `new array`, every string concatenation, every
`substring` and every file read already allocates from that arena and already never frees. Classes
raise the rate; they did not introduce the model.

When the arena runs out, the program aborts with a message naming the limit rather than returning
null and crashing later somewhere unrelated. `DEFAULT_RESERVE` in `native/arena.h` sets the size:
16MB on desktop, 4MB on the PSP, whose whole heap is 8MB.

If a native program needs reclamation, the mechanism to build on is `Arena_Pop_To` — scoped regions
or a per-frame reset — not a collector.

## What a class costs

Every class literal is a heap allocation where a struct would have been a stack value. For something
like the UI display list, which builds one flat `array<int>` per frame and touches no other memory,
that is a real regression: one allocation per node instead of one per frame. `std/ui` is written
around an integer arena for that reason and stays that way.

Classes are justified by expressiveness — by the programs that could not be written at all — not by
performance.

## Emission

`TypeEmitter.EmitStruct` is the one place that decides value versus reference. A struct becomes a
sealed `System.ValueType` with `SequentialLayout`; a class becomes a sealed `System.Object` with a
parameterless constructor and no layout attribute, because pinning field order would stop the runtime
packing reference fields. Everything downstream — boxing, default values, field assignment, casts,
array element access — reads the Cecil flag rather than a ProLang symbol, so it follows from that one
choice.

On the C backend a class is `Name*`, allocated by a generated `prl_new_Name(...)` that calls
`prl_alloc_zero`, and reached with `->`. The struct definition C emits is identical for both kinds;
only naming, passing and member access differ.

## Related

- `docs/architecture/boxing.md` — why `any` boxes, and why reference types were not the fix for it
- `docs/architecture/ui-toolkit.md` — the integer arena, and why it stays
- `docs/architecture/dotnet-backend.md` — the rest of the type mapping
