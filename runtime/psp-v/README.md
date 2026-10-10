# ProLang MSIL runtime for PSP, written in V

This is an initial interpreter for a defined subset of ProLang's normal .NET assemblies.
It opens a PE/CLI DLL, reads its metadata and method bodies, and interprets the IL on the
PSP's CPU. The program is **not** transpiled to C. V emits C for the interpreter itself;
PSPSDK compiles that interpreter into a reusable homebrew `EBOOT.PBP`.

The runtime supports console programs and the rotating cube in `examples/18-psp-cube`,
including its UI toolkit, structs and scene painter delegate. It is an experimental interpreter,
not a full CLR. Fixtures are compared with .NET and executed in PPSSPP; physical PSP testing
is still required. Interpretation is substantially slower than the existing C PSP backend.
The optimized cube runs at about 12 fps in the tested PPSSPP configuration; see the
measurements below. It still needs further optimization for smooth gameplay.

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
```

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

The initial comparisons used ten frames after startup. The final run verified 61 changing
orientations, seven collections, and a median of 83 ms across 58 steady frames. The test
forces collections after frames 30 and 60; those two subsequent frames are excluded from
the steady-state median. A pointer-range filter reduced these collection frames from
584–599 ms to 148–150 ms. Production collects at the allocation threshold instead of
forcing a collection every 30 frames. Physical PSP performance remains unmeasured.

The main changes address actual overhead: freeing a temporary used to scan every managed
allocation; a hash table now finds it directly. PSP value slots shrank from 76 to 16 bytes:
integers and handles stay inline, while strings, structs and addresses use immutable payloads.
Call buffers are reused per depth and cleared on return to release dead references. Literal
string values are prepared once during decoding. The collector rejects words outside the
managed heap range before searching its block descriptors.

The cube still executes roughly 63,000 IL instructions and 3,700 ProLang calls per frame.
Further gains likely require integer-specific array storage, combining frequent instruction
sequences during decoding, or compiling hot methods to native code. These are future work.
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

Unknown instructions or unsupported signatures fail explicitly. All method bodies are decoded
before the entry point executes, including methods the program does not call. This is not an
ECMA verification engine and must not be treated as a sandbox for untrusted assemblies.

## Limits and memory

The image is limited to 8 MiB, each metadata table to 65,535 rows, evaluation stacks and locals
to 1,024 slots per method, call depth to 128, array elements cumulatively to 262,144, boxed
objects to 262,144, and execution to 10 million instructions between buffer swaps (or for the
whole run of a console program). The output buffer holds at most
64 KiB of UTF-16 bytes before a flush. These are bounds, not promises that all maxima fit
simultaneously in the PSP heap. Sleep is limited to 60 seconds per call.

The PSP host reserves a 12 MiB heap and a 512 KiB main-thread stack. V is built with `-gc none`;
`gc.h` supplies a single-threaded conservative collector for the interpreter's generated C
allocations on PSP. It scans saved registers, the main thread's stack and module data, follows
interior pointers, and reclaims unreachable temporary strings and struct payloads. Argument,
local and evaluation buffers are retained per call depth and cleared on method return.
Struct payloads use immutable snapshots, copying only the field path on mutation.

VM arrays and boxes currently retain stable handles for the entire run: their cumulative
budgets still apply. Programs repeatedly allocating new arrays or boxes can exhaust those
budgets. The cube reuses its arrays. The desktop test executable has no collector and is
intended for bounded tests; the PSP collector is the memory profile exercised by the cube test.

## Source layout and next work

`reader.v` checks binary reads; `image.v` reads PE/CLI metadata and resolves tokens; `il.v`
decodes method bodies and checks branches; `value.v` defines values and UTF-16 conversion;
`vm.v` executes instructions; `native.v` supplies runtime/BCL calls. `host.h` is the small
file, output, clock, GU/controller, and PSP bootstrap boundary; interpreter logic is V.

The PSP build excludes V 0.4.12's unused Unix closure header from generated C and preloads
Newlib's `ctype.h` to avoid its `_S` macro colliding with V string literals. It does not change
the installed V compiler or supply fake Unix APIs. The V-generated C still produces some
pointer-cast and const-qualifier warnings with PSPSDK.

Next milestones are reclaiming unused array/box handles, class semantics, wider numeric
types, interpreter performance, and verification on physical PSP hardware.

Format references: [ECMA-335](https://ecma-international.org/publications-and-standards/standards/ecma-335/),
[V 0.4.12](https://github.com/vlang/v/tree/0.4.12),
[PSPSDK build rules](https://github.com/pspdev/pspsdk/blob/master/src/base/build.mak).
