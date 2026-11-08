# MIPS-C# benchmark harness

The baseline is an **input artifact**, not a file supplied by this repository. Preserve or create it from a pre-change checkout and keep that checkout's commit, local changes, toolchain and build configuration with your records. The harness cannot establish that existing binaries correspond to the source: make a clean build and verify that yourself before capturing. Both versions run the exact same `program.dll` from the snapshot.

## Prepare the baseline

On Windows with .NET 10, PowerShell, the PSP build toolchain described by the checkout, and PPSSPP installed, build the pre-change checkout's portable core and PSP runtime. For example, if that checkout supports these commands:

```powershell
Set-Location C:/path/to/pre-change-checkout
dotnet build runtime/mips-cs/portable/Portable.csproj -c Release
& runtime/mips-cs/build-psp.ps1
```

The build script also creates the cube guest at `artifacts/mips-cs/psp/program.dll`. Use the guest appropriate for both versions and keep its SHA-256. From the current checkout, copy the already-built artifacts and source without changing the old checkout:

```powershell
Set-Location C:/path/to/current-checkout
& runtime/mips-cs/benchmarks/capture-baseline.ps1 `
    -Checkout C:/path/to/pre-change-checkout `
    -OutputDirectory artifacts/mips-cs/safe-core-before-example
```

This captures the `runtime/mips-cs` source (excluding `bin` and `obj`), Release portable DLL, deps and runtimeconfig, PSP EBOOT, guest, generated C and ELF. It writes a SHA-256 manifest of the captured files. It does not build either checkout or prove the files belong together. The output defaults to `artifacts/mips-cs/safe-core-before`. Existing snapshots are refused; `-Force` explicitly replaces one. Capture paths are limited to direct `safe-core-before*` children of this checkout's `artifacts/mips-cs` directory.

For the snapshot used in this work, independently computed SHA-256 values were:

| File | SHA-256 |
| --- | --- |
| `portable/Portable.dll` | `27E6106A9E69B7849E7297BDE25CF7320AE06D972DCB93BE009ED2ACF49AACE4` |
| `psp/EBOOT.PBP` | `1AAB9B4DC5B302E8EAE9044DEB2A34FA53B4D2C974B205D6E1CF7A2B9087BB4A` |
| `psp/program.dll` | `4B295428DC0814F34B49BA87A71F2E30B761D258697662C6985F2C5147A9392B` |
| `psp/runtime.c` | `CEA02221DF177F5941C25F977BD4D83C3114CCF514C5642893E10845AE6A35EC` |
| `psp/prolang-csharp-runtime.elf` | `D3C3B684886E9F2823F0060E6ED01A3B91D925EACDF5B74E6E505E3D0F0887E3` |

These identify the archived input; they do not make it available to a fresh checkout. The comparison writes its own source and binary manifest for each run.

## Run a comparison

Build the current checkout's PSP runtime with `runtime/mips-cs/build-psp.ps1`. The benchmark separately builds instrumented copies of both portable sources. Ensure no competing builds or benchmarks are running. Run from the current checkout:

```powershell
& runtime/mips-cs/benchmarks/compare.ps1 -Desktop -Psp `
    -BaselineDirectory artifacts/mips-cs/safe-core-before-example `
    -OutputDirectory artifacts/mips-cs/safe-core-bench-example `
    -Frames 1200 -WarmupFrames 500 -DesktopSamples 5 -PspSamples 3
```

Default settings are 600 requested frames, 100 warmup frames, five desktop samples and three PSP samples. `-Desktop` and `-Psp` can be used separately; provide different output directories to retain separate runs. Baseline defaults to `artifacts/mips-cs/safe-core-before`, output to `artifacts/mips-cs/safe-core-bench`. Output must be under `artifacts/mips-cs/safe-core-bench*`; each run replaces its own summary files and sample logs. `-Emulator` changes the PPSSPP installation used when creating the shared emulator copy; once the copy exists it is reused. `-NaturalGc` disables the old baseline test host's forced collection every 30 frames; the safe runtime ignores that flag and performs its own pressure-triggered collection. With the flag, each uses its normal automatic collection, but their collector implementations and thresholds differ. `-TimeoutSeconds` bounds each process.

The script alternates before/after order and runs one emulator session at a time. It requires every requested frame, including warmup, to match the first baseline sample's draw counts and geometry before writing timing summaries. The PSP can emit one additional terminal frame; only the first requested frames are compared and measured. It reports per-sample and pooled p50/p99/max, the median sample p50, allocated bytes per measured desktop frame, process and PSP statistics, source and binary hashes, .NET/OS/CPU details and raw logs. Percentiles select `floor((n-1)*p)` of the sorted observations. Collection frames remain in the timings.

Desktop measures current-thread elapsed time and managed allocations from the previous `Present` exit to the next entry; startup and Present logging are excluded. Frame one has no desktop measurement. Managed allocation counts omit native allocations, including the baseline's `NativeMemory`, so they do not measure total allocation equally. Desktop process peak working set, private memory at completion and GC counts cover the whole process, including startup and warmup. PSP `work_us` is the existing host measurement under PPSSPP, not physical PSP timing. PSP heap statistics after the rewrite include metadata and backing-array payloads but exclude array headers; the old collector reports guest allocations. Those heap sizes are not directly comparable. Emulation and host scheduling can affect timings; report the environment and sample distribution with results.
