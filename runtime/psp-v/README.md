# ProLang MSIL runtime for PSP, written in V

This is a runtime for a defined subset of ProLang's normal .NET assemblies. It opens a PE/CLI
DLL, reads its metadata and method bodies, and runs the IL on the PSP's CPU: compiled to MIPS
at load by a template JIT, or interpreted. The program is **not** transpiled to C. V emits C
for the runtime itself; PSPSDK compiles that into a reusable homebrew `EBOOT.PBP`.

The runtime supports console programs and the rotating cube in `examples/18-psp-cube`,
including its UI toolkit, structs and scene painter delegate. It is experimental, not a full
CLR. Fixtures are compared with .NET and executed in PPSSPP on both engines; physical PSP
testing is still required. The cube runs at a vblank-locked 60 fps in the tested PPSSPP
configuration, using about 3.6 ms of each 16.7 ms frame compiled (9.4 ms interpreted); see the
measurements below. Physical hardware is likely to be slower.

## Build

Requirements: .NET 10 SDK, **V 0.4.12**, and PSPSDK in WSL Ubuntu. V's C prelude is
version-sensitive, so the PSP build checks the version rather than silently accepting a
different compiler. Download V from its [official releases](https://github.com/vlang/v/releases/tag/0.4.12).
No V packages or managed runtime files are required on the PSP.

From the repository root in PowerShell:

```powershell
./runtime/psp-v/build.ps1 -VCompiler C:/tools/v/v.exe
```

The script builds ProLang, compiles the smoke fixture to MSIL, emits C for the V interpreter,
and invokes `build-psp.sh` through WSL. If the local ignored compiler at
`artifacts/toolchains/v_0.4.12/v/v.exe` exists, `-VCompiler` can be omitted.
Otherwise it uses `v` from PATH. Use `-Distro` for another WSL distribution.
`build-psp.sh` uses `$PSPDEV`, defaulting to `$HOME/pspdev-tmp/pspdev`.
The PSP build defaults to GCC `-O3`. Use `-Optimization s` or `-Optimization 2` to compare
size optimization or `-O2`; this changes the interpreter, not the MSIL program.

To choose another ProLang program:

```powershell
./runtime/psp-v/build.ps1 -Program ./runtime/psp-v/fixtures/arithmetic.prl
./runtime/psp-v/build.ps1 -Program ./examples/18-psp-cube/psp_cube.prl
```

Output is under `artifacts/msil-psp/`. Copy **both** `EBOOT.PBP` and `program.dll` to
`ms0:/PSP/GAME/ProLangMSIL/` on a PSP configured for homebrew, or open the EBOOT in PPSSPP.
`program.dll` is resolved beside the EBOOT. `runtime.log` is written there too.
Console output and graphics both use the existing GU renderer, with one owner of VRAM.
The last frame remains visible after return; HOME exits via a callback. In the cube, D-pad
moves the cursor, Cross clicks, shoulders rotate, and Start ends the render loop.

You can replace `program.dll` with another supported MSIL assembly without rebuilding the
EBOOT. ProLang also generates a `.runtimeconfig.json` and copies `ProLang.Runtime.dll` beside
the assembly; the interpreter does not read either. Its native bridge implements the supported
runtime calls directly.

For C generation without WSL, use `-GenerateOnly`, then run `runtime/psp-v/build-psp.sh`
inside WSL. The provided Makefile can also be used with another PSPSDK installation by
setting `RUNTIME_DIR` to this directory and building beside the generated `runtime.c`.

## Tests

```powershell
# Unit tests, differential execution against .NET, and expected failures.
./runtime/psp-v/test.ps1 -VCompiler C:/tools/v/v.exe

# Build and execute nine fixtures through PPSSPP.
./runtime/psp-v/test-ppsspp.ps1

# Reuse assemblies and an EBOOT already built by the commands above.
./runtime/psp-v/test-ppsspp.ps1 -SkipBuild

# Build the cube and verify 60 frames, changing geometry and memory collection.
./runtime/psp-v/test-cube.ps1

# Either PPSSPP script on the interpreter instead of the JIT.
./runtime/psp-v/test-ppsspp.ps1 -SkipBuild -NoJit
./runtime/psp-v/test-cube.ps1 -SkipBuild -NoJit
```

The JIT emits MIPS, so the desktop build (`test.ps1`) always interprets; there, the JIT's
encoder is checked word for word against `psp-as`, and every fixture method is compiled though
not run. The PPSSPP scripts run the JIT by default and check the log's `ENGINE jit` line, so a
fallback to the interpreter cannot pass silently. `-NoJit` places a `jit-off.txt` beside the
EBOOT, which is also how to turn the JIT off on hardware. Running both and comparing the cube's
per-frame geometry hashes is the differential test between the engines.

`test.ps1` builds with V's default C compiler. V 0.4.12's bundled tcc lacks `GetTickCount64`;
use a full compiler, e.g. MSVC by running inside a Visual Studio developer shell with
`VFLAGS=-cc msvc`.

The emulator script defaults to `C:/Program Files/PPSSPP/PPSSPPWindows64.exe`; override it
with `-Emulator`. It uses a portable copy under `artifacts/psp-tests/emulator/`, polls each
program's log with a timeout, and closes only the emulator process it started. The positive
fixtures must report `PROLANG OK`; unsupported IL, division by zero, excessive recursion, and a failed assertion
must report `PROLANG FAIL` with the expected diagnostic. Logs are preserved under
`artifacts/psp-tests/<fixture>/`. A successful package build alone does not prove execution.
The cube test injects Start after the requested frames, validates drawing counts and changing
edge coordinates, and captures `first-frame.bmp` and `last-frame.bmp` through a GE readback.
Its `test-frames.txt` is an optional test control file; do not copy it into an interactive package.

The allocation index and interior-pointer checks also have a standalone C stress test.
From the repository directory in WSL, with GCC installed:

```bash
gcc -std=c11 -O3 -Wall -Wextra -Werror -fsanitize=address,undefined \
  -fno-omit-frame-pointer runtime/psp-v/tests/gc_test.c -o /tmp/prolang-gc-test
/tmp/prolang-gc-test
```

## Performance

Measured with the same cube MSIL and PPSSPP 1.20.4 configuration (Direct3D 11,
normal emulation speed). These are PSP clock measurements between swaps, including
rendering and vblank, with initialization and screenshot readback excluded:

| Cumulative changes | Median frame time | Approximate fps |
| --- | ---: | ---: |
| Original interpreter, GCC `-Os` | 651 ms | 1.5 |
| Hash index for managed allocation lookup | 283 ms | 3.5 |
| Reusable argument/local/evaluation buffers | 283 ms | 3.5 |
| GCC `-O3` | 200 ms | 5.0 |
| Compact value slots and cached string literals | 83 ms | 12.0 |
| Execution form: dense opcodes, verified stack depth, one value stack, folded constants | 17 ms (12.0 ms work) | 60 |
| Fused instruction sequences | 17 ms (9.7 ms work) | 60 |
| Small leaf methods inlined | 17 ms (9.0 ms work) | 60 |
| **Template JIT to MIPS** (interpreter, for comparison: 9.4 ms work) | 17 ms (**3.6 ms work**) | 60 |

Once a frame fits in one vblank its time is locked at 16–17 ms, so `test-cube.ps1` also reports
**work**: the time from one present to the request for the next, before waiting on the GE and the
display. That is the headroom left, and the number to watch on hardware.

The initial comparisons used ten frames after startup. The current run verified 61 changing
orientations, with every frame's draw counts and edge-geometry hash identical to the 83 ms
interpreter's, and a median of 17 ms across 58 steady frames. The test forces collections after
frames 30 and 60; those two subsequent frames are excluded from the steady-state median. A
pointer-range filter reduced these collection frames from 584–599 ms to 148–150 ms; leaving the
1 MiB GU display list out of the root scan, and the faster interpreter, bring them to about 50 ms
of work. Production collects at the allocation threshold instead: at the cube's ~1.5 KiB of
garbage a frame, roughly every five or six seconds, which is a visible three-frame hitch.
Physical PSP performance remains unmeasured.

Where the 83 ms went: V lowered the dispatch `match` to a chain of comparisons, and every push,
pop, local access and array index was an out-of-line call returning a `Result` with bounds
checks — about 200 host instructions per IL instruction. Half of all calls were to methods like
`Ui->stride()` whose whole body is `ldc.i4 30; ret`. `decode.v` now turns each body into an
execution form once, at load:

- A dense opcode per instruction with every token resolved, including native calls, which were
  matched by comparing key strings on every call. GCC compiles the dispatch to a jump table.
- `nop`s removed, and calls to parameterless constant methods folded into the constant.
- The evaluation stack depth proven at every instruction (consistent at branch targets, within
  `maxstack`, correct at `ret`), so the loop does no underflow or overflow checks. Value kinds
  are still checked wherever they were, with the same diagnostics.
- Common sequences fused: `ldc; add`, `ldarg; ldfld`, `clt; ldc 0; ceq` (which is how `>=` is
  compiled), `dup; stloc; pop` (an assignment statement), and others. Never across a branch target.

Execution runs every frame in one contiguous value stack with an explicit frame array and no
recursion: a call's arguments are already on the caller's stack and become the callee's argument
slots where they lie. Freeing a temporary no longer scans every managed allocation; PSP value
slots are 16 bytes; frames are cleared on return to release dead references.

Small leaf methods (at most 40 operations, calling no ProLang method) are then copied into
their callers. `Ui->get`, a bounds-checked read of one int, was called 650 times a frame and the
call cost more than the read; calls fell from 1,750 to 290 a frame. The copies share one set of
scratch locals per caller, which is safe because a copy contains no calls, so two never run at
once. The one visible difference: a copied method no longer counts towards the 128-call depth
limit.

### The JIT

**Was it worth it?** After the work above, the interpreter spent about 9 ms on ~43,600
operations a frame: some 43 CPU cycles each, nearly all of it fetching the operation, dispatching
through the jump table, and copying 16-byte values in and out of memory by index. The drawing
itself is about 0.5 ms (the C backend's build of the same scene spends that much in the same
calls). Compiling each operation to its few loads and stores removes the fetch and dispatch, and
the proven stack depth makes every stack slot a fixed offset from one register, so there is no
stack pointer either. The estimate was 10–15 instructions per operation and ~3 ms a frame; the
result is 3.6 ms, 2.6 times faster than the interpreter, leaving 78% of each frame free instead
of 44%.

**How it works** (`jit.v`, which documents each template beside the code that emits it):

- Every body is compiled at load, before the program starts, into one buffer of machine code:
  152 methods, ~500 KB, in about 120 ms of PSP time including a collection at the end. The
  buffer comes from `memalign`, outside the collector, and is written back from the data cache
  and invalidated in the instruction cache before it runs.
- A body becomes a C-callable function, `int body(Vm *vm, Value *frame, Value *ret)`, using the
  same frame layout in the same value stack as the interpreter. It returns 0, or 1 with the
  diagnostic in `vm.jit_error`; callers propagate a 1, so an error unwinds every compiled frame.
- Hot operations are emitted inline with the interpreter's checks, in its order, raising its
  exact diagnostics. Every other operation, and the unusual case of a hot one (`ldfld` through a
  managed address, `ceq` on anything but two ints), calls `exec_slow`, the single implementation
  both engines share. So every operation is supported, and a rare one cannot behave differently
  compiled.
- Calls go through a table of entry addresses, which is also how `Invoke` finds a delegate's
  method. Depth, value-stack room and the instruction budget are checked as the interpreter
  does; the budget is charged per basic block rather than per operation.
- A returning frame clears only the pointer word of each slot, enough for the collector; the
  value stack is cleared in full when the program ends.

The JIT is used whenever it can be: on the PSP, unless `jit-off.txt` is present, and only if
compilation succeeds (it declines a frame too large for 16-bit offsets, or a V array layout it
does not recognize). `runtime.log` records which engine ran, the code size and the compile time.

The cube now executes about 43,600 operations and 290 calls per frame. Further gains would come
from keeping stack values in registers across operations and from dropping kind checks the
decoder can prove unnecessary, but with three quarters of the frame free, that is not pressing.
Use `test-cube.ps1 -SkipBuild` after rebuilding to reproduce the timing and correctness checks;
frame logs include instruction and call counts.

## Supported profile

| Area | Implemented |
| --- | --- |
| Image | PE32 and PE32+, optimized CLI metadata (`#~`), `#Strings`, `#Blob`, `#US`, tiny/fat method headers |
| Calls | Static MethodDefs in the loaded module, recursion, arguments, argument reassignment, locals, returns |
| Control flow | Short/long conditional and unconditional branches; targets must be instruction boundaries |
| Integers | 32-bit arithmetic with wraparound, signed/unsigned division and comparison opcodes, bitwise operations, masked shifts, narrowing conversions |
| State | Static fields, ProLang's `__GlobalsInit` called by its actual assembly entry point |
| Arrays | Primitive/string/object/struct arrays, zero initialization, aliasing, indexed load/store, length |
| Strings | UTF-16 code-unit storage, literals, concatenation, equality, length, char access, substring, ordinal `IndexOf` |
| Objects | Boxing/unboxing of supported integer primitives, bool and char; distinct box identities |
| Structs | Nested values, recursive zero initialization, copy semantics, fields and managed addresses into locals, arguments, globals and arrays |
| Delegates | Capture-free static functions using BCL `Action`/`Func`, generic TypeSpec signatures and `Invoke` |
| Builtins | Buffered print/flush, scalar string conversion, assert, console write, int min/max, monotonic milliseconds, sleep |
| PSP | All nine PSP builtins: GU initialization, clear, rectangles, lines, bitmap text, swap/vblank, controller masks |

Native calls are matched by declaring type, name, parameter types, return type, and instance
flag. Arbitrary `System.*` methods and assembly dependencies are not loaded. This bridge is
a small compatibility profile, not a replacement for the BCL. In particular, `IndexOf` is
ordinal; .NET's default overload can use current-culture rules.

Unsupported features include classes and instance MethodDefs, int64,
floating point, general generics in metadata (delegate signatures are supported),
exceptions/finally, reflection, external user assemblies, maps, files, random, Windows/OpenGL
interop. Normal MSIL compilation maps PSP builtins to `ProLang.Runtime.PspOps`; the V host
implements these imports. Running those calls directly on desktop .NET throws a clear
`PlatformNotSupportedException`.

Unknown instructions or unsupported signatures fail explicitly. All method bodies are decoded,
and their stack depths verified, before the entry point executes, including methods the program
does not call. This is not an ECMA verification engine — value types are checked as they are
used, not proven — and must not be treated as a sandbox for untrusted assemblies.

## Limits and memory

The image is limited to 8 MiB, each metadata table to 65,535 rows, evaluation stacks and locals
to 1,024 slots per method, all active frames together to 16,384 value slots (256 KiB on the PSP),
call depth to 128, array elements cumulatively to 262,144, boxed
objects to 262,144, and execution to 10 million instructions between buffer swaps (or for the
whole run of a console program). The output buffer holds at most
64 KiB of UTF-16 bytes before a flush. These are bounds, not promises that all maxima fit
simultaneously in the PSP heap. Sleep is limited to 60 seconds per call. Compiled code takes
roughly 3 KB per method from the PSP heap, outside the collector; a method whose frame exceeds
about 2,000 slots, or whose code exceeds the reach of a 16-bit branch, makes the whole program
fall back to the interpreter.

The PSP host reserves a 12 MiB heap and a 512 KiB main-thread stack. V is built with `-gc none`;
`gc.h` supplies a single-threaded conservative collector for the runtime's generated C
allocations on PSP. It scans saved registers, the main thread's stack and module data, follows
interior pointers, and reclaims unreachable temporary strings and struct payloads. The GU display
list, 1 MiB of static data that never holds a heap pointer, is left out of the root scan. Every
frame's arguments, locals and evaluation stack share one value stack, cleared on method return.
Struct payloads use immutable snapshots, copying only the field path on mutation.

VM arrays and boxes currently retain stable handles for the entire run: their cumulative
budgets still apply. Programs repeatedly allocating new arrays or boxes can exhaust those
budgets. The cube reuses its arrays. The desktop test executable has no collector and is
intended for bounded tests; the PSP collector is the memory profile exercised by the cube test.

## Source layout and next work

`reader.v` checks binary reads; `image.v` reads PE/CLI metadata and resolves tokens; `il.v`
decodes method bodies and checks branches; `decode.v` turns them into the execution form
(resolved, folded, inlined, fused and stack-verified), and documents every operation;
`value.v` defines values and UTF-16 conversion; `vm.v` interprets operations and holds
`exec_slow`, the one implementation of the rare ones; `jit.v` compiles them to MIPS;
`native.v` supplies runtime/BCL calls. `host.h` is the small file, output, clock, GU/controller,
code-memory and PSP bootstrap boundary; runtime logic is V.

The PSP build excludes V 0.4.12's unused Unix closure header from generated C and preloads
Newlib's `ctype.h` to avoid its `_S` macro colliding with V string literals. It does not change
the installed V compiler or supply fake Unix APIs. The V-generated C still produces some
pointer-cast and const-qualifier warnings with PSPSDK.

Next milestones are reclaiming unused array/box handles, class semantics, wider numeric
types, keeping values in registers across compiled operations, and verification on physical
PSP hardware — the JIT in particular, since PPSSPP models neither cache nor bus timing.

Format references: [ECMA-335](https://ecma-international.org/publications-and-standards/standards/ecma-335/),
[V 0.4.12](https://github.com/vlang/v/tree/0.4.12),
[PSPSDK build rules](https://github.com/pspdev/pspsdk/blob/master/src/base/build.mak).
