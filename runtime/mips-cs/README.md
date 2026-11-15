# C# MSIL → MIPS runtime

## On-device PSP runtime

`portable/` is the C# IL interpreter and MIPS JIT that **runs on the PSP**. The EBOOT
loads `program.dll`, reads its PE/CLI metadata and IL, verifies stack depths, compiles
methods to native MIPS at load, maintains the PSP instruction/data caches, and executes
them directly. Use `jit-off.txt` beside the EBOOT to select the interpreter. The cube,
UI toolkit, structs, globals and delegates run through this path. Programs are replaceable
without rebuilding the EBOOT.

```text
portable core → Roslyn type check → restricted C#→C → PSPSDK → EBOOT.PBP
                                                              ↓ on PSP
ProLang → program.dll → PE/CLI loader → IL interpreter + MIPS JIT → GU/controller
```

NativeAOT cannot target PSP/MIPS. The portable core is safe C#; `cs-to-c/` translates
the same checked array, index and reference operations to C and rejects unsupported syntax.
This is not a general C# compiler. C platform glue supplies file I/O, graphics/input,
executable code allocation, cache maintenance and failure handling. The renderer is shared
with `psp-v`; the loader, collector, IL semantics and MIPS compiler are portable C#.

Guest references are integer handles into a fixed table. The collector traces guest value
slots, permanent metadata objects, managed-address chains and output; it reuses unreachable
handles only at instruction boundaries, after the JIT has flushed cached slots. Frames and
image readers use checked array indices. PSP-native pointers remain inside the C platform
boundary and generated code; they are never stored as guest values. The desktop-only
files, marked in the [source layout](#source-layout), are type-checked but not translated.
After decoding and verification, both engines use an exact-length instruction plan with
precomputed stack locations, stack effects, operation categories and control-flow
boundaries. Call, type and literal information lives in a separate sparse table. Original
IL offsets remain available in the plan; the temporary decoder records and verifier queue
are released. The interpreter still charges each original instruction, and the JIT keeps
its existing block charging and folding rules.

JIT helper calls declare whether they read or write the frame, allocate, collect or never
return. Checked array, field and length helpers preserve cached integer registers. Helpers
that can observe or change the frame, allocate or collect publish the cache first; unknown
helpers do so conservatively. Control-flow joins also flush when their incoming states can
differ.

```powershell
./runtime/mips-cs/build-psp.ps1
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Frames 600
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Frames 2400 -NaturalGc -TimeoutSeconds 120
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Fixtures
./runtime/mips-cs/test-psp.ps1 -SkipBuild -NoJit -Frames 120
./runtime/mips-cs/test-psp.ps1 -SkipBuild -NoJit -Fixtures
./runtime/mips-cs/profile-c.ps1 -Frames 600
./runtime/mips-cs/report-psp.ps1
```

Requires .NET 10, the PSP SDK in a WSL distribution, and PPSSPP for execution tests.
`build-psp.ps1 -Distro NAME` must name a WSL distribution that is installed and has the SDK
(`wsl -l -q` lists them; the default is `Ubuntu`). `PSPDEV` defaults to
`$HOME/pspdev-tmp/pspdev` inside that distribution. `test-psp.ps1 -Emulator PATH` takes the
path of a PPSSPP executable, and the `artifacts/` baselines named below are local files, not
repository assets. The EBOOT and cube DLL are under
`artifacts/mips-cs/psp/`; copy both into a homebrew folder on a PSP. Do not ship
`test-frames.txt` or `jit-off.txt` in an interactive package.

The tests reuse exactly one EBOOT across twelve fixtures. The nine shared fixtures cover
the existing compatibility profile; the local `array_bounds`, `gc_roots`, and `cache_effects`
fixtures check array bounds, collection with long-lived guest references, and values cached
across JIT helper calls. The PSP report compares every cube frame with the native C backend
and disassembles the actual MIPS emitted on-device. A smooth 60 fps is a frame-budget result;
CPU work remains higher than direct C. Physical PSP performance is unverified.
The initial NativeAOT report below measured desktop emulation and is separate.

The safe runtime always collects at its own allocation-pressure thresholds. `-NaturalGc`
selects natural collection in the older baseline runtime; it does not change the safe
collector's policy. See the [benchmark guide](benchmarks/README.md) for reproducible
desktop and PSP comparisons.

The portable core uses eight-byte guest values, immutable metadata roots, bounded transitive
inlining, constant folding, a six-register integer cache, integer facts with checked boundaries,
and fixed-point division strength
reduction. Collection is requested after 16 KiB of guest allocations or 1024 allocations,
and runs at the next safe instruction boundary. The table holds at most 16,383 live guest
objects; arrays are also subject to a live element limit. Collection stops the world.
It is an experimental compatibility profile, not a security sandbox.

### Source layout

Each file in `portable/` holds one static class, apart from `Model.cs`, which holds the structs,
and the directory is flat (see the rules below). The rows up to `Runtime.cs` are in the order
`build-psp.ps1` gives the files to `cs-to-c` (`DesktopHost.cs` second, type-checked only); the
desktop-only files after them are never given to it.

| File | Class | Responsibility |
| --- | --- | --- |
| `Model.cs` | structs | `Value`, `HeapObject`, `ByteReader`, `PeSection`, `MethodSignature`, `DecodedOp`, `PlanOp`, `ColdOp`, `MethodBody`: fields only; the invariants of each are documented there |
| `DesktopHost.cs` | `Host` | **Desktop only.** The desktop platform boundary: counts draws, hashes geometry, fake JIT addresses under `JitCapture` |
| `ValueKind.cs` | `ValueKind` | Constants: what a `Value` or heap object holds |
| `ElementType.cs` | `ElementType` | Constants: ECMA-335 element type codes, also the runtime's type codes |
| `TypeEncoding.cs` | `TypeEncoding` | Constants: composite type codes (struct, generic parameter, array) |
| `Il.cs` | `Il` | Constants: CIL opcodes |
| `Limits.cs` | `Limits` | Constants: value stack slots, handle table size, instruction budget, nesting limits |
| `JitAbi.cs` | `JitAbi` | Constants shared with the C glue: JIT helper numbers and trap reasons |
| `MetadataTable.cs` | `MetadataTable` | Constants: metadata table ids |
| `MetadataToken.cs` | `MetadataToken` | Constants: token and coded index layout |
| `MipsOp.cs` | `MipsOp` | Constants: MIPS primary opcodes the JIT emits |
| `MipsFunct.cs` | `MipsFunct` | Constants: MIPS SPECIAL function codes |
| `MipsReg.cs` | `MipsReg` | Constants: MIPS register numbers |
| `JitFrame.cs` | `JitFrame` | Constants: native frame offsets and the in-memory shape of a `Value` |
| `Metadata.cs` | `Metadata` | The image bytes, PE sections, metadata streams and tables, and the bounds-checked readers |
| `TypeSystem.cs` | `TypeSystem` | Signature decoding, type codes, field tables, token resolution |
| `Loader.cs` | `Loader` | `Load`: PE and CLI headers, streams, tables, fields and methods; decodes and verifies every method |
| `Decoder.cs` | `Decoder` | IL bytes to `DecodedOp`: method header, locals, operands, branch targets |
| `Verifier.cs` | `Verifier` | Stack-depth verification, then plan building |
| `Planner.cs` | `Planner` | `DecodedOp` to the compact `PlanOp` and sparse `ColdOp` tables; the `PlanOp.Flags` layout |
| `Natives.cs` | `Natives` | The whitelist of calls outside the program (`ResolveNative`) and their implementations (`Call`) |
| `Objects.cs` | `Objects` | Strings, records, arrays, boxes, managed addresses, zero values, equality, formatting |
| `Heap.cs` | `Heap` | The handle table and the mark-and-sweep collector |
| `Arithmetic.cs` | `Arithmetic` | `Int32` values, truthiness, the IL integer operations |
| `Interpreter.cs` | `Interpreter` | The value stack, `Invoke`, `ExecuteResolved`, call entry and exit, the instruction budget |
| `MipsAssembler.cs` | `MipsAssembler` | The word buffer, labels, branch fixups and instruction encoders |
| `RegisterCache.cs` | `RegisterCache` | The six-register (`$s2` to `$s7`) integer cache of the JIT |
| `Jit.cs` | `Jit` | Compiles methods to MIPS: emitters, inlining, constant folding, the helper effect contract |
| `Runtime.cs` | `Runtime` | `Run` (reset, load, compile, invoke, flush) and `Fail` |
| `DesktopProgram.cs` | `Host` | **Desktop only.** The command line: `--self-test`, `--jit-golden`, `--repeat`, `--reset-test`, or run a program |
| `LoaderTests.cs` | `LoaderTests` | **Desktop only.** Loader bounds and value checks; runs the heap and plan suites |
| `HeapTests.cs` | `HeapTests` | **Desktop only.** Collector roots, handle reuse and address reads and writes |
| `PlanTests.cs` | `PlanTests` | **Desktop only.** Verifier-to-plan conversion and helper effects |
| `JitGoldenTests.cs` | `JitGoldenTests` | **Desktop only.** Hashes the MIPS words the JIT emits, using fake host addresses |
| `AbiTests.cs` | `AbiTests` | **Desktop only.** Pins the numbers the C glue depends on |

`Portable.csproj` builds the desktop executable from all of these. Documentation is in XML
comments on every class and on the non-trivial methods, and the invariants (the overloaded
`HeapObject.Parent`, `Value.Data` per kind, the `PlanOp.Flags` bits, copy-on-write in
`Objects.WriteAddress`, the register cache and helper effect contract) are documented where
they live.

### Portable C# rules

Everything except the desktop-only files must stay inside the C# subset that `cs-to-c/`
accepts, because the same source is built by Roslyn for the desktop and translated to C for
the PSP. `cs-to-c` is not a general C# compiler, and it is not changed to fit the runtime.

- **Classes** are `static` and hold only fields and static methods; there are no nested types.
  **Structs** hold fields only: no methods, properties or constructors.
- **Statements:** `if`, `while`, `for`, `switch` with `case` and `default`, `break`, `continue`,
  `return` and blocks. Locals and parameters have explicit types (no `var`); `ref` parameters
  and locals are allowed, `in` and `out` are not.
- **Not allowed:** `foreach`, `do`, `goto`, `throw`, lambdas, local functions, LINQ, generics,
  enums, properties, pattern matching (`is`), string interpolation, `nameof`, `?.`, `??`,
  named arguments, and array initializers. Strings are ASCII with no embedded NUL. A failure
  goes through `Runtime.Fail`.
- **No `unsafe`,** `fixed`, `stackalloc`, pointers, `Unsafe.*` or `MemoryMarshal`; the project
  keeps `AllowUnsafeBlocks` false.
- **Every member states its accessibility** (`private`, `internal` or `public`), never the implicit
  default. `cs-to-c` ignores the modifier, so this only helps the reader. A member used by one class
  alone is `private` and, if it is a field, named `_camelCase`.
- **Constants** are `const` fields whose initializer is **one numeric literal**, optionally
  negative: `cs-to-c` emits `static <type> Class_Name = <literal>;`. It cannot be an expression
  such as `1 << 9` and cannot name another constant, and its C# type must be the C# type of the
  literal: `int`, or `uint` when the literal is unsigned (a `U` suffix, or a hex value above
  `int.MaxValue`, as `TypeEncoding.StructTagMask` and `GenericParamTagMask` are), so that the
  generated C promotes values exactly as the C# does.
- **`case` labels stay numeric literals,** because C rejects a static variable as a case label.
  Each one carries a trailing `// mnemonic` comment naming the constant (`Il`, `Natives`,
  `MetadataTable`) that it stands for, and each such switch has a header comment naming the
  class. Everywhere else, use the constant, not the number. GCC folds a file-static that is
  never written into a constant, so the constants cost nothing in the hot path.
- **C ordering:** all structs are in `Model.cs`, in dependency order, and it is the **first**
  argument to `cs-to-c`. `DesktopHost.cs` is the **second**: it is type-checked against the
  translated files, and never translated. Static field initializers must be C constants.
- **Some violations show only in C.** A constant used as a case label, for example, passes
  `dotnet build` and fails in `psp-gcc`, so a change to `portable/` is not checked until
  `build-psp.ps1` has run to the end.
- **The file list is explicit.** `build-psp.ps1` names every translated file, in order. A new
  file must be added there (after the files it depends on), and a desktop-only one must not be.
- **`portable/` stays flat.** `benchmarks/compare.ps1` copies only the top-level `*.cs` and
  `*.csproj` files of `portable/` when it builds the instrumented copies.
- **Patched by pattern.** `compare.ps1` edits `DesktopHost.cs` and `DesktopProgram.cs` by
  regular expression. It requires the `Present` method's signature, the `geometry = 2166136261;`
  line followed directly by the end of that method and `CollectStats`, and exactly one line
  that ends a successful run in `DesktopProgram.cs`. Do not edit those lines, and do not put
  anything, even a comment, between the end of `Present` and `CollectStats`.
- **Behaviour is fixed by tests that compare text.** Diagnostic strings are matched exactly,
  and the `Host` API names and signatures are the contract with the C glue (below).

### C ABI contract

`cs-to-c` names a member `Class_Member` (`Interpreter.Memory` is `Interpreter_Memory`) and a
constant `Class_Name`. The glue in `psp/host.h` and `psp/entry.h` uses these generated symbols,
so renaming one in C# is a change to the glue as well. `psp/host.h` is included **before** the
generated code and `psp/entry.h` **after** it, which is why only `entry.h` can use the
generated constants.

| C symbol | C# origin | Used for |
| --- | --- | --- |
| `Value` | `Value` (`Model.cs`) | A frame is a native `Value *`. Generated MIPS reads `Kind` at byte 0 and `Data` at byte 4, eight bytes in all (`JitFrame`) |
| `HeapObject`, field `Values` | `HeapObject` | `pvm_array_element` and `pvm_field_element` index `object->Values` |
| `Interpreter_Memory` | `Interpreter.Memory` | The value stack and globals; frame pointers are converted to checked indices against it |
| `Heap_HandleTable` | `Heap.HandleTable` | Looking up the object of an array or record handle |
| `Metadata_Image` | `Metadata.Image` | `Host_CodeReady` reads the method's name from the image for the log |
| `Interpreter_Steps` | `Interpreter.Steps` | `host.h` declares it too, ahead of the generated definition, so that `Host_Counter` can hand JIT code its address as the instruction budget counter |
| `Interpreter_Enter`, `Interpreter_Leave`, `Interpreter_Slow` | the methods of that name | Behind the Enter, Leave and Slow helpers (`pvm_enter`, `pvm_leave`, `pvm_slow`), which first convert the frame pointer to an index |
| `Objects_ValidateArray`, `Objects_ValidateField` | the methods of that name | The array and field element helpers validate through them |
| `Objects_ArrayLength` | `Objects.ArrayLength` | **Is** helper 7: `Host_Helper` returns its address, so its signature is part of the ABI |
| `Runtime_Run` | `Runtime.Run` | Called by `main` with the image, its size and the engine choice |
| `JitAbi_HelperEnter`, `JitAbi_HelperLeave`, `JitAbi_HelperSlow`, `JitAbi_HelperArrayElement`, `JitAbi_HelperFieldElement`, `JitAbi_HelperArrayLength` | `JitAbi` | The switch in `Host_Helper`. `JitAbi.HelperTrap` (4) has no arm: it is the default, `pvm_trap` |
| `Limits_ValueStackSlots` | `Limits.ValueStackSlots` | Bounds every JIT frame index (`pvm_frame_index`, `Host_Call`) and every global slot (`Host_GlobalSlot`) |

The other direction is the `Host` API: generated code calls `Host_Fail`, `Host_Reset`,
`Host_Length`, `Host_Ascii`, `Host_Number`, `Host_Write`, `Host_Init`, `Host_Clear`, `Host_Rect`,
`Host_Line`, `Host_Text`, `Host_Present`, `Host_CollectStats`, `Host_Millis`, `Host_Sleep`,
`Host_Vsync`, `Host_Buttons`, `Host_Collected`, `Host_ReleaseVals`, `Host_ReleaseChars`,
`Host_ReleaseInts`, `Host_ReleaseOps`, `Host_ReleaseWords`, `Host_CodeReady`, `Host_Counter`,
`Host_MethodSlot`, `Host_GlobalSlot`, `Host_Helper` and `Host_Call`, all defined in `psp/host.h`
and `psp/entry.h`. Their names and signatures are the ones in `DesktopHost.cs`.

**Trap reasons.** `pvm_trap` in `psp/host.h` maps the reason that generated code passes to the
trap helper to a message. It is included before the generated code, so the numbers stay literals
there, each with a comment naming its `JitAbi` constant, and `AbiTests` pins every one:

| Reason | `JitAbi` constant | Message |
| --- | --- | --- |
| 1 | `TrapExpectedInt32` | expected an int32 value |
| 3 | `TrapExpectedArray` | expected non-null array |
| 10 | `TrapBudget` | instruction budget exceeded |
| 11 | `TrapIndexOutOfRange` | array index out of bounds |
| 12 | `TrapDivideByZero` | division by zero |
| 13 | `TrapDivisionOverflow` | int32 division overflow |

Reasons 1 and 3 are also the values of `ValueKind.Int32` and `ValueKind.Array`, because
`Jit.EmitKindCheck` passes the expected kind as the reason. Any other reason gives "JIT value kind
check failed". Likewise `AbiTests` pins the helper numbers 1 to 7, the 16384 of
`Limits.ValueStackSlots` and of `Limits.HandleTableSize` (two limits that only happen to be
equal), and the layout of `Value`.

**`pvm_at` is `always_inline`.** `psp/arrays.h` defines the checked accessor behind every
translated array access. `cs-to-c` emits every method with external linkage, so GCC's
unit-growth budget for inlining is spent on cold load-time code. Without the attribute, ordinary
refactors of that code changed whether `Interpreter.ExecuteResolved` inlined `pvm_at`, and the
interpreter's median frame time moved by about 6%. The attribute costs about 7 KB of `.text`.

### Regression gates

A change to `portable/` is checked by these, run in order. The first four need only .NET; the
others also need WSL with the PSP SDK, and PPSSPP.

```powershell
dotnet build runtime/mips-cs/portable/Portable.csproj -c Release --nologo -v quiet   # warnings are errors
dotnet runtime/mips-cs/portable/bin/Release/net10.0/Portable.dll --self-test
./runtime/mips-cs/test.ps1
./runtime/mips-cs/golden.ps1 -Check
./runtime/mips-cs/build-psp.ps1 -Distro Ubuntu         # C# -> C -> psp-gcc -> EBOOT
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Emulator PATH -Frames 600           # cube, JIT
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Emulator PATH -Frames 600 -NoJit    # cube, interpreter
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Emulator PATH -Fixtures [-NoJit]
./runtime/mips-cs/compare-psp-logs.ps1 [-Strict] [-Baseline DIR]
```

`--self-test` runs five desktop suites and prints one `PASS` line for each: loader bounds
(`LoaderTests`), value operations and the instruction encoders, heap collection and address roots
(`HeapTests`), verified execution plans and helper effects (`PlanTests`), and the ABI constants
(`AbiTests`). The unsafe-code rule is checked with
`git grep --untracked -nE "unsafe|stackalloc|fixed *\(|Unsafe\.|MemoryMarshal" -- runtime/mips-cs/portable`,
which must find nothing.

**`golden.ps1 -Record` and `-Check`.** `-Record` compiles the cube and the twelve fixtures into
`artifacts/mips-cs/golden/guests` and records, for each guest, two outputs in
`artifacts/mips-cs/golden/expected` (and a hash of the guest itself, 27 items in all). `-Check`,
the default, runs everything again into `.../actual` and compares exactly, after CRLF to LF,
exiting non-zero on any difference. `-SkipBuild` reuses the existing builds. Every process gets
`-TimeoutSeconds` (120): one that outlives it is killed with its children, its item fails, and
`-Record` refuses to record it. `-Check` also fails on an orphan, a file in `expected` that no
current guest produces. Record only on a runtime you trust; a refactor must leave `-Check` green.
The two outputs per guest are:

- **The desktop run:** `Portable.dll <guest>` (with 600 frames for the cube), its output and exit
  status. For the cube that is every `FRAME` line with `geometry=`, `instructions=` and `calls=`,
  which the desktop host's fixed 17 ms clock makes deterministic. A guest that is meant to fail is
  recorded failing.
- **The JIT golden:** `Portable.dll --jit-golden <guest>`. The desktop cannot run MIPS, but it can
  compile it: with `Host.JitCapture` on, the helper, slot and counter addresses are fixed fake
  values and each method's words are captured and hashed, one `METHOD` line per method and a
  `TOTAL`. So the loader, decoder, planner and the JIT's emitted code are compared byte for byte
  without a PSP.

**`compare-psp-logs.ps1`.** It compares the `runtime.log` of each guest and engine
(`<engine>/<case>/runtime.log`, 2 engines by 13 guests, written by `test-psp.ps1`) in `-Actual`
(default `artifacts/mips-cs/psp-tests`) with the recording in `-Baseline` (default
`artifacts/mips-cs/refactor-baseline`), line for line. It compares the `RUNTIME` and `ENGINE`
lines, every `METHOD ... code_bytes=` line and `JIT total_code_bytes`, the `PROLANG OK` or `FAIL`
line, everything the guest printed, and for each `FRAME` up to `-MaxFrame` (600) its number and
its `lines`, `rects`, `texts` and `geometry`. It ignores each frame's `time_ms` and `work_us`,
which are clocks, and frames past `-MaxFrame`. Without `-Strict` it also ignores `instructions=`
and `calls=` and the `HEAP` lines, and reports whether the counts match; `-Strict` compares them.
The interpreter's instruction counts depend on the emulated speed: the guest reads a real clock
(`time_millis`) for its frame-rate text, so a slower run can execute a different number of
instructions in a frame. Geometry and draw counts never change. `artifacts/mips-cs/refactor-baseline-v2`
is the same set of logs recorded after `MethodBody.Machine` was removed and `pvm_at` was forced
inline (the `HEAP` lines are 4 bytes per method lower than in `refactor-baseline`); compare against
it with `-Strict -Baseline artifacts/mips-cs/refactor-baseline-v2`.

A log in `-Actual` that is older than `artifacts/mips-cs/psp/EBOOT.PBP` (`-Eboot`) fails as stale:
it was left by an earlier build, typically because `test-psp.ps1` stopped partway or only some of
its four variants (cube or `-Fixtures`, each with and without `-NoJit`) ran after the last build.
All 26 logs must be current, and a subset of engines or guests is not supported (a missing log is a
failure as well); `-AllowStale` turns the age check off for logs that were copied on purpose.

## Desktop NativeAOT reference

The top-level project is the earlier .NET 10 NativeAOT **desktop host** for the compatibility profile in
[`../psp-v`](../psp-v/README.md). It reads ProLang's existing PE/CLI assemblies as metadata,
interprets their IL or compiles it to MIPS32 words, and executes those words in a checked
little-endian MIPS engine. It does not load guest assemblies into the CLR, generate C#, or
use `Reflection.Emit`. No packages are needed for a normal build.

The integration test is the actual [`psp_cube.prl`](../../examples/18-psp-cube/psp_cube.prl),
including the UI toolkit and painter delegate, compiled through ProLang's normal MSIL backend.
All three engines must produce the same draw counts and geometry on **every** frame. An
optional V/PPSSPP log checks the coordinates independently using V's exact FNV geometry hash.

```text
psp_cube.prl → ProLang → program.dll → metadata + IL decoder + stack verifier
                                                ├─ IL interpreter
                                                ├─ MIPS templates
                                                └─ constant folding + fusion → MIPS templates
                                                           ↓
                                                checked MIPS word execution
                                                           ↓
                                               headless PSP calls + frame hashes

C# host → NativeAOT → desktop x64 executable (or a separate CoreCLR/JIT build)
```

NativeAOT does not support PSP/MIPS as a host target; see Microsoft's
[supported platforms](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).
The guest MIPS ABI uses checked services for calls, arrays, structs, strings and other complex
operations. It is deliberately documented in `Mips.cs`; it is not the V JIT's PSP EABI. The
emitted binaries are inspectable guest code, **not** an EBOOT or standalone PSP executable.

## Build and test

From the repository root:

```powershell
dotnet build runtime/mips-cs/ProLang.Mips.csproj -c Release
./runtime/mips-cs/test.ps1
dotnet publish runtime/mips-cs/ProLang.Mips.csproj -c Release -r win-x64 `
    -p:PublishAot=true -p:IlcGenerateMapFile=true -o artifacts/mips-cs/native
./runtime/mips-cs/test.ps1 -SkipBuild -NativeExe "$PWD/artifacts/mips-cs/native/ProLang.Mips.exe"
```

NativeAOT publishing requires the .NET native build prerequisites (Visual C++ tools on
Windows). `test.ps1` compiles all nine shared V fixtures afresh, compares the four console programs
with their actual .NET execution, checks the PSP drawing bridge, and requires explicit
diagnostics for four failure cases. It runs every fixture on all three engines, plus unit
checks for decoding, stack verification, arithmetic, branch encoding and a non-nop MIPS delay
slot. Neither the test nor the runtime invokes V or PPSSPP.
It also builds the portable desktop runtime, runs loader and collector checks, and checks
the local array bounds and collection fixtures against their expected outcomes. With
`-SkipBuild`, that runtime must already be built.

To run another supported assembly:

```powershell
dotnet runtime/mips-cs/bin/Release/net10.0/ProLang.Mips.dll run program.dll --engine=optimized -- alpha beta
```

## Profile the cube and inspect instructions

```powershell
./runtime/mips-cs/profile.ps1
# Optionally require agreement with an existing V/PPSSPP reference:
./runtime/mips-cs/profile.ps1 -VLog artifacts/psp-tests/cube/runtime.log
# Reuse the compiler/runtime binaries already built:
./runtime/mips-cs/profile.ps1 -SkipBuild -Samples 5 -Frames 60
```

The script is for Windows x64. It builds the compiler and both host variants, compiles the
cube afresh, captures a separate CoreCLR disassembly run, and profiles IL, baseline MIPS and
optimized MIPS under **both** CoreCLR and NativeAOT. It restores all process environment
settings afterward. Use `-DumpBin PATH` if Visual Studio's `vswhere` cannot locate `dumpbin.exe`.
Offline builds can set `-p:NuGetAudit=false --ignore-failed-sources` when all AOT packs are
already cached; the scripts use this for the package-free runtime.

The entry points are `artifacts/mips-cs/aot/report.html` and
`artifacts/mips-cs/jit/report.html`. Each directory contains:

| File | Meaning |
| --- | --- |
| `report.txt`, `report.html` | Measured timings, allocation bytes, GC counts, code size, optimization counts and limitations |
| `il.txt` | Original **guest** IL, method signatures, verified stack depths |
| `host-il.txt` | Original C# IL for `MipsCpu.Execute` and `Vm.Step`, before trimming/AOT, read using the compiler's Cecil dependency |
| `mips.txt`, `optimized.txt` | Emitted MIPS words, register instructions, service annotations and original IL offsets |
| `*.mips.bin` | Concatenated little-endian method words; method offsets appear in the corresponding text listing |
| `host-jit.txt` | Actual x64 CoreCLR JIT instructions for `MipsCpu.Execute` and `Vm.Step`, from a separate untimed run |
| `host-aot.txt` | Actual x64 machine instructions from the NativeAOT executable, using `dumpbin /DISASM` |
| `host-aot-hot.txt` | Named NativeAOT `MipsCpu.Execute` and `Vm.Step` instructions, extracted from the full listing using its PDB symbols |
| `first-frame.svg`, `last-frame.svg` | Draw-command previews; text approximates the PSP bitmap font |
| `frames.csv`, `samples.csv` | Validation frame data and raw measured run samples |
| `v-reference.log` | Optional historical V log, copied with its SHA256 in the report |

**There are two different compilations:** the C# host is compiled by CoreCLR or NativeAOT;
the host's template compiler emits guest MIPS. NativeAOT has no host JIT. `syscall` in the
MIPS listing is a counted C# service bridge, not an unimplemented instruction. Guest code
uses integer-kind checks and explicit delay slots. Memory accesses, IL branch boundaries,
evaluation stack depth, call depth, frame space and execution budgets are checked.

### What is measured

One complete warmup run precedes each mode's measured runs. CoreCLR tiering and ReadyToRun
are disabled by the script. Each sample is the median work time of frames 2..N; the report
shows the median, minimum and maximum across samples. Image load and guest compilation time
are recorded separately. Per-method entry/operation counters are enabled for all modes;
these are counts, **not** a CPU sampling profile. SVG generation, disassembly, file output and
forced GC between runs are outside the timing boundary. Allocation figures include VM setup;
frame CSV times come from a separate validation pass with SVG capture enabled.

The host is headless: no rasterization, GPU, vblank or real controller. It supplies a
deterministic 17 ms/frame application clock and injects Start to stop the cube. Sleep and
Vsync are no-ops. These measurements cannot establish PSP frame rate or hardware speed.
Emulating MIPS adds dispatch work, so compare baseline and optimized MIPS to assess guest
optimizations; the direct IL interpreter may be faster on a desktop. The report says so
even when an optimization produces a slowdown.

### Implemented optimizations

Calls, strings and field tokens are resolved at load; string literals are cached. Stack
depth verification allows generated loads/stores at fixed frame offsets. Integer arithmetic,
argument/local copies and truth branches use MIPS instructions; complex operations share the
interpreter's checked implementation. Optimized mode folds calls to parameterless integer
constant methods, then fuses a constant followed by an integer operation. Fusion is forbidden
across any branch target. The baseline MIPS mode disables both transformations, allowing
their code size, instruction count, call count and host timing effects to be measured.

## Scope

Supported: static functions/recursion; int32 arithmetic, unsigned variants and narrowing;
branches; arguments/locals; globals; arrays; nested value structs and managed addresses;
capture-free delegates; boxes; UTF-16 strings; the V profile's scalar BCL/runtime calls;
all nine PSP calls through the headless host. Struct writes replace immutable snapshots,
while array fields retain reference semantics.

Unsupported signatures/instructions fail explicitly. This is an experimental compatibility
profile, not a CLR or a sandbox: no classes, exception handling, int64/floating point,
arbitrary external assemblies or general generic methods. Limits include an 8 MiB image,
1024 locals/stack slots per method, 16384 live value slots, 128 call depth, 262144 cumulative
array elements, 2 million object handles and 10 million dispatched IL groups between presents.
Temporary handles remain live until the VM run ends; long-running application memory
management is future work. It does not reproduce the V PSP collector or its memory profile.

`Image.cs` reads/verifies metadata and IL; `Vm.cs` implements values and checked semantics;
`Mips.cs` emits and executes MIPS; `Host.cs` supplies the headless PSP boundary; `Program.cs`
runs differential profiling and writes reports; `SelfTests.cs` tests the execution engine.
