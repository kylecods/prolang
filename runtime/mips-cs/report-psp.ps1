param([string]$Distro = 'Ubuntu')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$out = "$repo/artifacts/mips-cs/psp-report"
New-Item -ItemType Directory -Force $out | Out-Null
function Read-Frames([string]$Path) {
    $text = [IO.File]::ReadAllText($Path)
    @([regex]::Matches($text, 'FRAME (\d+) lines=(\d+) rects=(\d+) texts=(\d+) geometry=([0-9a-f]+)[^\r\n]*work_us=(\d+)') | ForEach-Object {
        [pscustomobject]@{ Frame=[int]$_.Groups[1].Value; Lines=[int]$_.Groups[2].Value; Rects=[int]$_.Groups[3].Value; Texts=[int]$_.Groups[4].Value; Hash=$_.Groups[5].Value; Work=[int]$_.Groups[6].Value }
    })
}
function Percentile($values, [double]$fraction) { $sorted = @($values | Sort-Object); return $sorted[[int][Math]::Floor(($sorted.Count-1)*$fraction)] }
$jitPath = "$repo/artifacts/mips-cs/psp-tests/jit/cube/runtime.log"
if (!(Test-Path -LiteralPath "$repo/artifacts/mips-cs/psp-tests/jit/cube/natural-gc.txt")) { throw 'Run the 2400-frame -NaturalGc test before writing this report' }
$jit = Read-Frames $jitPath
$c = Read-Frames "$repo/artifacts/mips-cs/c-baseline/runtime.log"
$compare = [Math]::Min($jit.Count, $c.Count)
if ($compare -lt 2) { throw 'At least two C/JIT comparison frames are required' }
for ($n=0; $n -lt $compare; $n++) {
    if (($jit[$n].Hash -cne $c[$n].Hash) -or $jit[$n].Lines -ne $c[$n].Lines -or $jit[$n].Rects -ne $c[$n].Rects -or $jit[$n].Texts -ne $c[$n].Texts) { throw "C/JIT geometry differs at frame $($n+1)" }
}
$interpreterPath = "$repo/artifacts/mips-cs/psp-tests/interpreter/cube/runtime.log"
if (Test-Path -LiteralPath $interpreterPath) {
    $interpreter = Read-Frames $interpreterPath
    for ($n=0; $n -lt [Math]::Min($jit.Count,$interpreter.Count); $n++) {
        if ($jit[$n].Hash -cne $interpreter[$n].Hash) { throw "Interpreter/JIT mismatch: frame $($n+1)" }
    }
}
$rows = [Collections.Generic.List[object]]::new()
$inputs = @(
    @{ Name='Initial C# JIT'; Path="$repo/artifacts/mips-cs/psp-baseline/runtime.log" },
    @{ Name='C# interpreter'; Path="$repo/artifacts/mips-cs/psp-tests/interpreter/cube/runtime.log" },
    @{ Name='C# JIT forced GC'; Path="$repo/artifacts/mips-cs/psp-forced-final/runtime.log" },
    @{ Name='C# JIT automatic GC'; Path=$jitPath },
    @{ Name='Direct C (-O3)'; Path="$repo/artifacts/mips-cs/c-baseline/runtime.log" }
)
$allFrames = [Collections.Generic.List[object]]::new()
foreach ($input in $inputs) {
    if (!(Test-Path -LiteralPath $input.Path)) { continue }
    $frames = @(Read-Frames $input.Path)
    $values = @($frames | Where-Object Frame -gt 1 | Select-Object -ExpandProperty Work)
    $misses = @($values | Where-Object { $_ -gt 16667 }).Count
    $rows.Add([pscustomobject]@{ Engine=$input.Name; Frames=$values.Count; P50_us=(Percentile $values 0.5); P99_us=(Percentile $values 0.99); Max_us=($values | Measure-Object -Maximum).Maximum; Over_16_7ms=$misses })
    foreach ($frame in $frames) { $allFrames.Add([pscustomobject]@{ Engine=$input.Name; Frame=$frame.Frame; Work_us=$frame.Work; Geometry=$frame.Hash }) }
}
$rows | Export-Csv -LiteralPath "$out/summary.csv" -NoTypeInformation
$allFrames | Export-Csv -LiteralPath "$out/frames.csv" -NoTypeInformation
$summary = $rows | Format-Table -AutoSize | Out-String -Width 180
$jitMedian = ($rows | Where-Object Engine -eq 'C# JIT automatic GC').P50_us
$cMedian = ($rows | Where-Object Engine -eq 'Direct C (-O3)').P50_us
$assemblyHash = (Get-FileHash -Algorithm SHA256 "$repo/artifacts/mips-cs/psp-tests/jit/cube/program.dll").Hash
$runtimeHash = (Get-FileHash -Algorithm SHA256 "$repo/artifacts/mips-cs/psp-tests/jit/cube/EBOOT.PBP").Hash
$currentLog = [IO.File]::ReadAllText($jitPath)
$initialLog = [IO.File]::ReadAllText("$repo/artifacts/mips-cs/psp-baseline/runtime.log")
$initialBytes = [regex]::Match($initialLog,'JIT total_code_bytes=(\d+)').Groups[1].Value
$finalBytes = [regex]::Match($currentLog,'JIT total_code_bytes=(\d+)').Groups[1].Value
$heap = @([regex]::Matches($currentLog,'HEAP bytes=(\d+) blocks=(\d+) collections=(\d+)'))
$heapMaximum = ($heap | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Maximum).Maximum
$collections = $heap[-1].Groups[3].Value
$text = @"
ProLang on-device C# IL interpreter + MIPS JIT

The EBOOT contains the portable C# runtime compiled through a restricted, type-checked
C#->C backend and PSPSDK. It reads program.dll and emits executable MIPS on the PSP.
No ProLang program is statically transpiled into this EBOOT. The same EBOOT passed all
nine fixtures by replacing only program.dll. NativeAOT does not support the PSP;
the earlier NativeAOT desktop experiment is a separate project and measurement.

MEASURED IN PPSSPP, Direct3D 11, normal emulation speed. This is not physical hardware.
The clock is the emulated PSP clock. Work starts after one present and ends before
the next GPU sync/vblank. Initialization and first frame are excluded. Both forced
GC every 30 frames and a separate automatic-GC production run are included below.
No collection frames are filtered from p99/max or frame-budget misses.
EBOOT SHA256: $runtimeHash
Test assembly SHA256: $assemblyHash

$summary
Correctness: $compare frames have identical hashes AND draw counts between direct C and
the MIPS JIT. The first 61 frames also matched the historical V reference.

CPU work ratio JIT / direct C: $([Math]::Round($jitMedian/$cMedian,2))x. Smooth 60 fps
does not mean C-equivalent throughput. The 'almost as fast as C' CPU-work target is
not yet achieved. The run demonstrates display-budget headroom without collector
frames crossing that budget; physical PSP validation remains required.
Automatic-GC run: $collections total collections; managed heap peak $heapMaximum bytes.
This excludes permanent image/metadata and JIT code, not total process memory.
Initial/final emitted code: $initialBytes / $finalBytes bytes. Inlining spends code size
to reduce steady calls from about 514 to 226 per frame.

OPTIMIZATIONS
The initial C# JIT already used 8-byte values, permanent metadata, verified fixed
offsets, constant folding/fusion and one-level 40-operation inlining. Subsequent
changes added the integer facts/register cache, fast comparisons/division and
two-level 80-operation inlining. Median work fell from 3651 to $jitMedian us.

* Eight-byte values on PSP instead of the V runtime's sixteen-byte slots.
* Immutable decoder/image/code data outside the managed heap, with explicit live VM
  roots. Collections trace managed objects rather than the entire loader working set.
* A 16 KiB managed-allocation growth interval bounds automatic collection work for
  this workload, instead of accumulating seconds of temporary strings. The V
  runtime's default collector growth interval remains unchanged.
* Fixed frame offsets from verified stack depths, constant folding, fused immediates.
* Bounded transitive inlining (80 IL operations, two levels), including UI helpers.
* Integer facts with checks at typed argument/store/return boundaries.
* Six callee-saved registers cache integer slots across straight-line operations.
* Power-of-two signed division/remainder preserve truncation toward zero; fixed-point
  cube multiplication divides by 4096 using shifts/bias rather than a slow bridge.
* Common integer comparisons execute directly in MIPS.

The initial C# JIT observation and final observation have different run lengths and
were not interleaved randomized trials. Raw logs and frame CSVs are included. GC is
still stop-the-world; this is a bounded stress result, not a guarantee of zero jank.

INSTRUCTIONS
cube-il.txt: actual IL loaded by the runtime.
jit-mips.txt: actual MIPS code emitted on-device, disassembled from jit-mips.bin.
jit-methods.txt: method names and byte offsets into that JIT binary.
runtime-mips.txt: native MIPS of the C# interpreter/compiler itself (C#->C->PSPSDK).
The earlier desktop report's x64 JIT is not the PSP's MIPS JIT.

REPRODUCE
./runtime/mips-cs/build-psp.ps1
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Frames 600
Copy the forced-GC runtime.log into artifacts/mips-cs/psp-forced-final/runtime.log.
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Frames 2400 -NaturalGc -TimeoutSeconds 120
./runtime/mips-cs/test-psp.ps1 -SkipBuild -Fixtures
./runtime/mips-cs/test-psp.ps1 -SkipBuild -NoJit -Frames 120
./runtime/mips-cs/test-psp.ps1 -SkipBuild -NoJit -Fixtures
./runtime/mips-cs/profile-c.ps1 -Frames 600
./runtime/mips-cs/report-psp.ps1
"@
[IO.File]::WriteAllText("$out/report.txt", $text)
Copy-Item -LiteralPath "$repo/artifacts/mips-cs/psp-tests/jit/cube/jit-mips.bin" -Destination "$out/jit-mips.bin" -Force
Copy-Item -LiteralPath "$repo/artifacts/mips-cs/psp/runtime-mips.txt" -Destination "$out/runtime-mips.txt" -Force
$offset = 0
$methodMap = [Collections.Generic.List[string]]::new()
foreach ($m in [regex]::Matches([IO.File]::ReadAllText($jitPath), 'METHOD (\d+) (\S+) code_bytes=(\d+)')) {
    $size = [int]$m.Groups[3].Value
    $methodMap.Add(('offset=0x{0:x8} bytes={1} method={2} {3}' -f $offset,$size,$m.Groups[1].Value,$m.Groups[2].Value))
    $offset += $size
}
$methodMap | Set-Content -LiteralPath "$out/jit-methods.txt"
& dotnet "$repo/src/ProLang/bin/Release/net10.0/ProLang.dll" "--msil=$repo/artifacts/mips-cs/psp-tests/jit/cube/program.dll" | Set-Content -LiteralPath "$out/cube-il.txt"
if ($LASTEXITCODE) { throw 'Guest IL listing failed' }
$wslOut = (& wsl -d $Distro --exec wslpath -a $out).Trim()
& wsl -d $Distro --exec bash -lc 'export PSPDEV="$HOME/pspdev-tmp/pspdev"; "$PSPDEV/bin/psp-objdump" -D -b binary -m mips:allegrex "$1/jit-mips.bin" > "$1/jit-mips.txt"' -- $wslOut
if ($LASTEXITCODE) { throw 'On-device JIT disassembly failed' }
foreach ($name in 'first-frame.bmp','last-frame.bmp','runtime.log') { Copy-Item -LiteralPath "$repo/artifacts/mips-cs/psp-tests/jit/cube/$name" -Destination "$out/$name" -Force }
$html = "<!doctype html><meta charset='utf-8'><title>PSP C# IL / MIPS JIT</title><style>body{background:#111827;color:#e5e7eb;font:15px system-ui;margin:2rem}pre{white-space:pre-wrap;background:#1f2937;padding:1.4rem}a{color:#67e8f9}img{width:480px;max-width:100%}</style><h1>On-device C# IL interpreter and MIPS JIT</h1><img src='first-frame.bmp' alt='First cube frame'> <img src='last-frame.bmp' alt='Last cube frame'><p><a href='cube-il.txt'>Cube IL</a> · <a href='jit-mips.txt'>Actual on-device JIT instructions</a> · <a href='jit-methods.txt'>Method map</a> · <a href='runtime-mips.txt'>Native runtime instructions</a> · <a href='frames.csv'>Frame measurements</a></p><pre>$([Net.WebUtility]::HtmlEncode($text))</pre>"
[IO.File]::WriteAllText("$out/report.html", $html)
Write-Host $summary
Write-Host "Report: $out/report.html"
