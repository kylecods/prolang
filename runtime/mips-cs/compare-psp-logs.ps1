# Compares PSP run logs (test-psp.ps1 output) with a recorded baseline, for both engines and all
# thirteen guests (the cube plus the twelve fixtures).
#
#   compare-psp-logs.ps1 [-Baseline DIR] [-Actual DIR] [-MaxFrame 600] [-Strict]
#
# Both directories hold <engine>/<case>/runtime.log, engine being 'jit' or 'interpreter'. The
# defaults are artifacts/mips-cs/refactor-baseline (recorded before the refactor) and
# artifacts/mips-cs/psp-tests (where test-psp.ps1 writes). artifacts/mips-cs/refactor-baseline-v2 is the
# same set recorded after the dead MethodBody.Machine field was removed (the HEAP lines are 4 bytes per
# method lower) and pvm_at was forced inline (psp/arrays.h), which restored the interpreter's speed and so
# its instruction counts; later phases compare against it with -Strict -Baseline artifacts/mips-cs/refactor-baseline-v2.
#
# Each log is reduced to a canonical form and the two forms must be identical, line for line:
#
#   compared       RUNTIME and ENGINE lines; every `METHOD <id> <name> code_bytes=<n>` line;
#                  `JIT total_code_bytes`; the PROLANG OK / PROLANG FAIL line with its message;
#                  everything the guest printed; and for every FRAME up to -MaxFrame the frame
#                  number, lines, rects, texts and geometry.
#   dropped        FRAME lines numbered above -MaxFrame (a 600-frame cube run logs a terminal
#                  frame 601), and for every kept FRAME its time_ms and work_us, which are clocks.
#   dropped unless -Strict
#                  each FRAME's instructions= and calls= (they depend on how the guest's own
#                  time_millis() reads fall, so a different emulator speed may move them) and the
#                  HEAP lines (collector size statistics).
#
# Without -Strict the instruction and call counts are still reported as an informational line, so
# drift is visible. The exit status is non-zero on any difference or any missing log.
#
# A log in -Actual that is older than the build output (-Eboot, default artifacts/mips-cs/psp/EBOOT.PBP)
# fails as stale: it was left by an earlier build, typically because test-psp.ps1 stopped partway or
# because only some of its four variants (cube or -Fixtures, with or without -NoJit) were run after the
# last build. Every one of the 26 logs must be current; running a subset of engines or guests is not
# supported, and a missing log is a failure too. -AllowStale turns the age check off, for logs that were
# copied or archived on purpose.
param(
    [string]$Baseline = "$PSScriptRoot/../../artifacts/mips-cs/refactor-baseline",
    [string]$Actual = "$PSScriptRoot/../../artifacts/mips-cs/psp-tests",
    [string]$Eboot = "$PSScriptRoot/../../artifacts/mips-cs/psp/EBOOT.PBP",
    [int]$MaxFrame = 600,
    [switch]$Strict,
    [switch]$AllowStale
)
$ErrorActionPreference = 'Stop'
# Relative paths are taken from PowerShell's current location, not from the process directory.
$Baseline = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Baseline)
$Actual = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Actual)
$Eboot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Eboot)
$ebootTime = $null
if (!$AllowStale) {
    if (!(Test-Path -LiteralPath $Eboot -PathType Leaf)) {
        Write-Host "compare-psp-logs: cannot tell whether the logs are stale: no build output at $Eboot (run build-psp.ps1, or pass -AllowStale)"
        exit 1
    }
    $ebootTime = (Get-Item -LiteralPath $Eboot).LastWriteTimeUtc
}
$engines = 'jit', 'interpreter'
$cases = 'cube', 'smoke', 'arithmetic', 'boxed', 'structs', 'psp_api', 'div_zero', 'assert_fail', 'call_depth', 'unsupported', 'array_bounds', 'gc_roots', 'cache_effects'
$frameRx = '^FRAME (\d+) lines=(\d+) rects=(\d+) texts=(\d+) geometry=(\w+) time_ms=(\d+) work_us=(\d+) instructions=(\d+) calls=(\d+)\s*$'

# Returns the canonical lines, plus counts and the per-frame instruction/call pairs.
function ConvertTo-Canonical([string]$Path) {
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $lines = [System.Collections.Generic.List[string]]::new()
    $work = [System.Collections.Generic.List[string]]::new()
    $frame = 0
    $frames = 0
    $methods = 0
    foreach ($line in ($text -split "`n")) {
        if ($line -match $frameRx) {
            $frame = [int]$Matches[1]
            if ($frame -gt $MaxFrame) { continue }
            $frames++
            $canonical = "FRAME $frame lines=$($Matches[2]) rects=$($Matches[3]) texts=$($Matches[4]) geometry=$($Matches[5])"
            $counts = "instructions=$($Matches[8]) calls=$($Matches[9])"
            $work.Add($counts)
            $lines.Add($(if ($Strict) { "$canonical $counts" } else { $canonical }))
            continue
        }
        if ($line.StartsWith('HEAP ')) {
            if ($Strict -and $frame -le $MaxFrame) { $lines.Add($line) }
            continue
        }
        if ($line.StartsWith('METHOD ')) { $methods++ }
        $lines.Add($line)
    }
    return [pscustomobject]@{ Lines = $lines; Frames = $frames; Methods = $methods; Work = $work }
}

$failures = 0
$stale = 0
$checked = 0
foreach ($engine in $engines) {
    foreach ($case in $cases) {
        $name = "$engine/$case"
        $expectedPath = "$Baseline/$engine/$case/runtime.log"
        $actualPath = "$Actual/$engine/$case/runtime.log"
        $missing = @($expectedPath, $actualPath | Where-Object { !(Test-Path -LiteralPath $_ -PathType Leaf) })
        if ($missing.Count) {
            Write-Host "FAIL $name : missing $($missing -join ', ')"
            $failures++
            continue
        }
        if ($ebootTime) {
            $logTime = (Get-Item -LiteralPath $actualPath).LastWriteTimeUtc
            if ($logTime -lt $ebootTime) {
                Write-Host "FAIL $name : stale log: $actualPath was written $($logTime.ToString('s'))Z, before the build output $Eboot ($($ebootTime.ToString('s'))Z); rerun test-psp.ps1 for this engine and guest"
                $failures++
                $stale++
                continue
            }
        }
        $checked++
        $expectedLog = ConvertTo-Canonical $expectedPath
        $actualLog = ConvertTo-Canonical $actualPath
        $difference = $null
        $count = [Math]::Max($expectedLog.Lines.Count, $actualLog.Lines.Count)
        for ($i = 0; $i -lt $count -and !$difference; $i++) {
            $e = if ($i -lt $expectedLog.Lines.Count) { $expectedLog.Lines[$i] } else { '<end of log>' }
            $a = if ($i -lt $actualLog.Lines.Count) { $actualLog.Lines[$i] } else { '<end of log>' }
            if ($e -cne $a) { $difference = "line $($i + 1): expected [$e] actual [$a]" }
        }
        if ($difference) {
            Write-Host "FAIL $name : $difference"
            $failures++
            continue
        }
        $same = 0
        for ($i = 0; $i -lt [Math]::Min($expectedLog.Work.Count, $actualLog.Work.Count); $i++) {
            if ($expectedLog.Work[$i] -ceq $actualLog.Work[$i]) { $same++ }
        }
        $info = if ($expectedLog.Frames) { "; instructions/calls identical in $same of $($expectedLog.Frames) frames" } else { '' }
        Write-Host "PASS $name ($($expectedLog.Frames) frames, $($expectedLog.Methods) METHOD lines, $($expectedLog.Lines.Count) canonical lines$info)"
    }
}

if ($failures) {
    Write-Host "compare-psp-logs: $failures of $($engines.Count * $cases.Count) logs differ, are missing or are stale$(if ($stale) { " ($stale stale)" })"
    exit 1
}
Write-Host "compare-psp-logs: all $checked logs match$(if ($Strict) { ' (strict)' })"
exit 0
