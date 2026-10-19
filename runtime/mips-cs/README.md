# C# MSIL → MIPS runtime

## On-device PSP runtime

`portable/` is the C# IL interpreter and MIPS JIT that **runs on the PSP**. The EBOOT
loads `program.dll`, reads its PE/CLI metadata and IL, verifies stack depths, compiles
methods to native MIPS at load, maintains the PSP instruction/data caches, and executes
them directly. Use `jit-off.txt` beside the EBOOT to select the interpreter. The cube,
UI toolkit, structs, globals and delegates run through this path. Programs are replaceable
without rebuilding the EBOOT.

```text
portable/*.cs → Roslyn type check → restricted C#→C → PSPSDK → EBOOT.PBP
                                                              ↓ on PSP
ProLang → program.dll → PE/CLI loader → IL interpreter + MIPS JIT → GU/controller
```

NativeAOT cannot target PSP/MIPS. The portable core uses an unmanaged C# subset and
`cs-to-c/` translates that same source; unsupported C# syntax fails explicitly. This is
not a general C# compiler. C platform glue supplies allocation, file I/O, graphics/input,
cache maintenance and failure handling. The renderer and conservative collector are shared
with `psp-v`; the loader, IL semantics and MIPS compiler are C#, not the V runtime.

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

Requires .NET 10, PSPSDK in WSL Ubuntu, and PPSSPP for execution tests. `PSPDEV` defaults
to `$HOME/pspdev-tmp/pspdev` inside WSL. The EBOOT and cube DLL are under
`artifacts/mips-cs/psp/`; copy both into a homebrew folder on a PSP. Do not ship
`test-frames.txt` or `jit-off.txt` in an interactive package.

The tests reuse exactly one EBOOT across all nine fixtures. The PSP report compares every
cube frame with the native C backend, includes forced collection frames in its p99/max,
and disassembles the actual MIPS emitted on-device. A smooth 60 fps is a frame-budget
result; CPU work remains higher than direct C. Physical PSP performance is unverified.
The initial NativeAOT report below measured desktop emulation and is separate.

The portable core uses eight-byte values on PSP, immutable metadata outside the managed
collector, explicit VM roots, bounded transitive inlining, constant folding, a six-register
integer cache, integer facts with checked boundaries, and fixed-point division strength
reduction. A 16 KiB managed-heap growth interval bounds collection work on the cube.
Collection still stops the world; the stress measurements are not a guarantee
of zero pauses. It is an experimental compatibility profile, not a security sandbox.

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
Windows). `test.ps1` compiles all nine V fixtures afresh, compares the four console programs
with their actual .NET execution, checks the PSP drawing bridge, and requires explicit
diagnostics for four failure cases. It runs every fixture on all three engines, plus unit
checks for decoding, stack verification, arithmetic, branch encoding and a non-nop MIPS delay
slot. Neither the test nor the runtime invokes V or PPSSPP.

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
