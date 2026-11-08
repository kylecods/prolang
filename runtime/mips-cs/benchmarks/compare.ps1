[CmdletBinding()]
param(
    [switch]$Desktop,
    [switch]$Psp,
    [int]$Frames = 600,
    [int]$WarmupFrames = 100,
    [int]$DesktopSamples = 5,
    [int]$PspSamples = 3,
    [int]$TimeoutSeconds = 300,
    [string]$OutputDirectory = '',
    [string]$BaselineDirectory = '',
    [string]$Emulator = 'C:/Program Files/PPSSPP/PPSSPPWindows64.exe',
    [switch]$NaturalGc
)
$ErrorActionPreference = 'Stop'
if (!$Desktop -and !$Psp) { throw 'Select -Desktop, -Psp, or both.' }
if ($WarmupFrames -lt 1 -or $Frames -le $WarmupFrames -or $DesktopSamples -lt 1 -or $PspSamples -lt 1 -or $TimeoutSeconds -lt 1) { throw 'Require 1 <= WarmupFrames < Frames and positive sample counts and timeout.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$baseline = if ($BaselineDirectory) { [IO.Path]::GetFullPath($BaselineDirectory) } else { Join-Path $repo 'artifacts/mips-cs/safe-core-before' }
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/mips-cs'))
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $artifactRoot 'safe-core-bench' }
# Benchmark outputs may replace files beneath this directory; never target the snapshot or other artifacts.
$relativeOut = [IO.Path]::GetRelativePath($artifactRoot, $out)
if ($relativeOut -notmatch '^safe-core-bench(?:[\\/]|$|[-_])' -or $relativeOut -match '(^|[\\/])\.\.([\\/]|$)') { throw 'OutputDirectory must be within artifacts/mips-cs and begin with safe-core-bench.' }
$baseline = [IO.Path]::GetFullPath($baseline).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$out = [IO.Path]::GetFullPath($out).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$comparison = [StringComparison]::OrdinalIgnoreCase
if ($baseline.Equals($out, $comparison) -or $baseline.StartsWith($out + [IO.Path]::DirectorySeparatorChar, $comparison) -or $out.StartsWith($baseline + [IO.Path]::DirectorySeparatorChar, $comparison)) { throw 'BaselineDirectory and OutputDirectory must not overlap.' }
$guest = Join-Path $baseline 'psp/program.dll'
$source = @{ before = (Join-Path $baseline 'source/portable'); after = (Join-Path $repo 'runtime/mips-cs/portable') }
$pbp = @{ before = (Join-Path $baseline 'psp/EBOOT.PBP'); after = (Join-Path $repo 'artifacts/mips-cs/psp/EBOOT.PBP') }
New-Item -ItemType Directory -Force $out | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$manifest = [Collections.Generic.List[object]]::new()
$processStats = [Collections.Generic.List[object]]::new()
foreach ($stale in 'summary.json', 'report.md', 'frames.csv', 'process-stats.csv', 'sample-summary.csv', 'manifest.json', 'environment.json') { $path = Join-Path $out $stale; if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
function Hash-File([string]$label, [string]$path) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $label : $path" }
    $manifest.Add([pscustomobject]@{ label=$label; path=$path; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash; bytes=(Get-Item -LiteralPath $path).Length })
}
function Percentile($sorted, [double]$p) { return $sorted[[int][Math]::Floor(($sorted.Count - 1) * $p)] }
function Frame-Records([string]$log, [string]$engine, [string]$variant, [int]$sample) {
    $matches = [regex]::Matches($log, '(?m)^FRAME (\d+) lines=(\d+) rects=(\d+) texts=(\d+) geometry=([0-9a-fA-F]+)[^\r\n]*')
    $framesFound = @{}
    foreach ($match in $matches) {
        $n = [int]$match.Groups[1].Value
        if ($n -gt $Frames) { continue }
        if ($framesFound.ContainsKey($n)) { throw "Duplicate frame $n in $engine $variant sample $sample" }
        $framesFound[$n] = $match
    }
    $frequency = 0
    $bench = @{}
    if ($engine -eq 'desktop') {
        $fm = [regex]::Match($log, 'BENCH frequency=(\d+)')
        if (!$fm.Success) { throw 'Missing clock frequency' }
        $frequency = [double]$fm.Groups[1].Value
        foreach ($m in [regex]::Matches($log, '(?m)^BENCH (\d+) ticks=(\d+) bytes=(\d+)')) { $bench[[int]$m.Groups[1].Value] = $m }
    }
    for ($n = 1; $n -le $Frames; $n++) {
        if (!$framesFound.ContainsKey($n)) { throw "Missing frame $n in $engine $variant sample $sample" }
        $m = $framesFound[$n]
        $elapsed = $null; $bytes = $null
        if ($engine -eq 'desktop' -and $n -gt 1) {
            if (!$bench.ContainsKey($n)) { throw "Missing desktop measurement for frame $n" }
            $elapsed = [double]$bench[$n].Groups[2].Value * 1000000 / $frequency
            $bytes = [long]$bench[$n].Groups[3].Value
        } elseif ($engine -ne 'desktop') {
            $wm = [regex]::Match($m.Value, 'work_us=(\d+)')
            if (!$wm.Success) { throw "Missing PSP work_us frame $n" }
            $elapsed = [double]$wm.Groups[1].Value
        }
        $rows.Add([pscustomobject]@{ engine=$engine; variant=$variant; sample=$sample; frame=$n; lines=[int]$m.Groups[2].Value; rects=[int]$m.Groups[3].Value; texts=[int]$m.Groups[4].Value; geometry=$m.Groups[5].Value.ToLowerInvariant(); work_us=$elapsed; allocated_bytes=$bytes })
    }
}
function Prepare-Desktop([string]$variant) {
    $dir = Join-Path $out $variant
    New-Item -ItemType Directory -Force $dir | Out-Null
    Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Extension -in '.cs', '.csproj' } | Remove-Item -Force
    foreach ($file in Get-ChildItem -LiteralPath $source[$variant] -File | Where-Object { $_.Extension -in '.cs', '.csproj' }) {
        Hash-File "$variant/source/$($file.Name)" $file.FullName
        Copy-Item -LiteralPath $file.FullName -Destination $dir -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BenchmarkClock.cs.txt') -Destination (Join-Path $dir 'BenchmarkClock.cs') -Force
    $hostFile = Join-Path $dir 'DesktopHost.cs'
    $text = [IO.File]::ReadAllText($hostFile)
    $pattern = '(public static void Present\(int steps, int calls\)\s*\{)'
    if ([regex]::Matches($text, $pattern).Count -ne 1) { throw "Present hook changed in $variant" }
    $text = [regex]::Replace($text, $pattern, '$1' + "`n        BenchmarkClock.Stop(frames + 1);")
    $pattern = '(geometry = 2166136261;\s*)(\}\s*public static void CollectStats)'
    if ([regex]::Matches($text, $pattern).Count -ne 1) { throw "Present exit hook changed in $variant" }
    $text = [regex]::Replace($text, $pattern, '$1' + "`n        BenchmarkClock.Start();`n    " + '$2')
    [IO.File]::WriteAllText($hostFile, $text)
    $programFile = Join-Path $dir 'DesktopProgram.cs'
    $text = [IO.File]::ReadAllText($programFile)
    $needle = 'Console.WriteLine("PROLANG OK");'
    if (($text.Split(@($needle), [StringSplitOptions]::None).Length - 1) -ne 1) { throw "Report hook changed in $variant" }
    [IO.File]::WriteAllText($programFile, $text.Replace($needle, "BenchmarkClock.Report();`n            $needle"))
    $nuget = Join-Path $dir 'NuGet.Config'
    [IO.File]::WriteAllText($nuget, '<configuration><packageSources><clear /></packageSources></configuration>')
    $oldAppData = $env:APPDATA
    try {
        $env:APPDATA = $dir
        & dotnet build (Join-Path $dir 'Portable.csproj') -c Release --nologo --configfile $nuget
        $buildExit = $LASTEXITCODE
    } finally { $env:APPDATA = $oldAppData }
    if ($buildExit -ne 0) { throw "Desktop $variant build failed" }
    Hash-File "$variant/instrumented-portable" (Join-Path $dir 'bin/Release/net10.0/Portable.dll')
}
function Run-Desktop([string]$variant, [int]$sample) {
    $dir = Join-Path $out "desktop/$variant/$sample"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $binary = Join-Path $out "$variant/bin/Release/net10.0/Portable.dll"
    $stdout = Join-Path $dir 'runtime.log'
    $stderr = Join-Path $dir 'stderr.log'
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$binary`"", "`"$guest`"", "$Frames") -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    try {
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(); throw "Desktop timed out: $dir" }
        $exit = $process.ExitCode
    } finally { $process.Dispose() }
    $log = [IO.File]::ReadAllText($stdout)
    if ($exit -ne 0 -or $log -notmatch 'PROLANG OK') { throw "Desktop $variant sample $sample failed: $log $([IO.File]::ReadAllText($stderr))" }
    $gc = [regex]::Match($log, 'gc0=(\d+) gc1=(\d+) gc2=(\d+) peak_working_set_bytes=(\d+) private_memory_bytes=(\d+)')
    if (!$gc.Success) { throw 'Missing desktop GC counts' }
    $processStats.Add([pscustomobject]@{ engine='desktop'; variant=$variant; sample=$sample; peak_working_set_bytes=[long]$gc.Groups[4].Value; private_memory_bytes=[long]$gc.Groups[5].Value; gc0=[int]$gc.Groups[1].Value; gc1=[int]$gc.Groups[2].Value; gc2=[int]$gc.Groups[3].Value; heap_max_bytes=$null; collections=$null; total_code_bytes=$null })
    Frame-Records $log 'desktop' $variant $sample
}
function Read-Shared([string]$path) {
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try { $reader = [IO.StreamReader]::new($stream); try { return $reader.ReadToEnd() } finally { $reader.Dispose() } } finally { $stream.Dispose() }
}
function Run-Psp([string]$engine, [string]$variant, [int]$sample, [string]$emulatorExe) {
    $dir = Join-Path $out "psp/$engine/$variant/$sample"
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($name in 'runtime.log', 'first-frame.bmp', 'last-frame.bmp', 'jit-mips.bin', 'jit-off.txt', 'natural-gc.txt') {
        $path = Join-Path $dir $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    Copy-Item -LiteralPath $pbp[$variant] -Destination (Join-Path $dir 'EBOOT.PBP') -Force
    Copy-Item -LiteralPath $guest -Destination (Join-Path $dir 'program.dll') -Force
    Set-Content -LiteralPath (Join-Path $dir 'test-frames.txt') -Value $Frames
    if ($engine -eq 'interpreter') { Set-Content -LiteralPath (Join-Path $dir 'jit-off.txt') -Value 'interpret' }
    if ($NaturalGc) { Set-Content -LiteralPath (Join-Path $dir 'natural-gc.txt') -Value 'automatic collection only' }
    Set-Content -LiteralPath (Join-Path $dir 'test.ini') -Value "[General]`nPauseOnLostFocus = False`n[Graphics]`nGraphicsBackend = 2"
    $emulatorArgs = @("--log=`"$dir/emulator.log`"", "--appendconfig=`"$dir/test.ini`"", '-direct3d11', '--windowed', "`"$dir/EBOOT.PBP`"")
    $process = Start-Process -FilePath $emulatorExe -ArgumentList $emulatorArgs -WorkingDirectory $dir -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $log = ''
        do {
            Start-Sleep -Milliseconds 250
            $path = Join-Path $dir 'runtime.log'
            if (Test-Path -LiteralPath $path) { $log = Read-Shared $path }
            if ($log -match 'PROLANG (OK|FAIL)') { break }
            if ($process.HasExited) { throw "PPSSPP exited early: $dir`n$log" }
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($log -notmatch 'PROLANG OK' -or $log -notmatch 'RUNTIME csharp-portable' -or $log -notmatch "ENGINE $engine") { throw "PPSSPP failure, wrong engine, or timeout: $dir`n$log" }
        $code = [regex]::Match($log, 'JIT total_code_bytes=(\d+)')
        if (!$code.Success) { throw "Missing PSP code byte total: $dir" }
        $codeBytes = [long]$code.Groups[1].Value
        if (($engine -eq 'jit' -and $codeBytes -le 0) -or ($engine -eq 'interpreter' -and $codeBytes -ne 0)) { throw "Unexpected generated code size $codeBytes for $engine : $dir" }
        $heap = @([regex]::Matches($log, 'HEAP bytes=(\d+) blocks=(\d+) collections=(\d+)'))
        if (!$heap.Count) { throw "Missing PSP heap statistics: $dir" }
        $processStats.Add([pscustomobject]@{ engine="psp-$engine"; variant=$variant; sample=$sample; peak_working_set_bytes=$null; private_memory_bytes=$null; gc0=$null; gc1=$null; gc2=$null; heap_max_bytes=($heap | ForEach-Object { [long]$_.Groups[1].Value } | Measure-Object -Maximum).Maximum; collections=($heap | ForEach-Object { [int]$_.Groups[3].Value } | Measure-Object -Maximum).Maximum; total_code_bytes=$codeBytes })
        Frame-Records $log "psp-$engine" $variant $sample
    } finally {
        if (!$process.HasExited) { $null = $process.CloseMainWindow(); if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() } }
        $process.Dispose()
    }
}
Hash-File 'guest' $guest
Hash-File 'clock helper' (Join-Path $PSScriptRoot 'BenchmarkClock.cs.txt')
Hash-File 'benchmark script' $PSCommandPath
if ($Desktop) {
    Hash-File 'before/archived-portable' (Join-Path $baseline 'portable/Portable.dll')
    Prepare-Desktop 'before'
    Prepare-Desktop 'after'
    for ($i = 1; $i -le $DesktopSamples; $i++) {
        $order = if ($i % 2) { @('before','after') } else { @('after','before') }
        foreach ($variant in $order) { Run-Desktop $variant $i }
    }
}
if ($Psp) {
    foreach ($variant in 'before','after') {
        $tree = if ($variant -eq 'before') { Join-Path $baseline 'source' } else { Join-Path $repo 'runtime/mips-cs' }
        foreach ($subdir in 'portable', 'psp', 'cs-to-c') {
            Get-ChildItem -LiteralPath (Join-Path $tree $subdir) -Recurse -File | Where-Object { $_.Extension -in '.cs', '.csproj', '.h', '.sh' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object { Hash-File "$variant/psp-source/$([IO.Path]::GetRelativePath($tree, $_.FullName))" $_.FullName }
        }
    }
    foreach ($variant in 'before','after') { Hash-File "$variant/psp-eboot" $pbp[$variant] }
    $portable = Join-Path $repo 'artifacts/mips-cs/emulator'
    $emulatorExe = Join-Path $portable 'PPSSPPWindows64.exe'
    if (!(Test-Path -LiteralPath $emulatorExe) -or !(Test-Path -LiteralPath (Join-Path $portable 'assets'))) {
        New-Item -ItemType Directory -Force $portable | Out-Null
        Copy-Item -LiteralPath $Emulator -Destination $emulatorExe -Force
        Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $Emulator) 'assets') -Destination $portable -Recurse -Force
    }
    Hash-File 'ppsspp' $emulatorExe
    foreach ($engine in 'interpreter','jit') {
        for ($i = 1; $i -le $PspSamples; $i++) {
            $order = if ($i % 2) { @('before','after') } else { @('after','before') }
            foreach ($variant in $order) { Run-Psp $engine $variant $i $emulatorExe }
        }
    }
}
$rows | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $out 'frames.csv')
$processStats | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $out 'process-stats.csv')
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'manifest.json')
$envInfo = [ordered]@{ date_utc=[DateTime]::UtcNow.ToString('o'); os=[Environment]::OSVersion.VersionString; cpu=$env:PROCESSOR_IDENTIFIER; dotnet=((& dotnet --info) | Out-String); dotnet_environment=@(Get-ChildItem Env: | Where-Object { $_.Name -match '^(DOTNET|COMPlus)_' } | ForEach-Object { "$($_.Name)=$($_.Value)" }); frames=$Frames; warmup_frames=$WarmupFrames; desktop_samples=$DesktopSamples; psp_samples=$PspSamples; natural_gc=[bool]$NaturalGc }
$envInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'environment.json')
# Every sample must match the first baseline, including warmup, before computing timings.
foreach ($engine in @($rows.engine | Select-Object -Unique)) {
    $reference = @{}
    foreach ($r in $rows | Where-Object { $_.engine -eq $engine -and $_.variant -eq 'before' -and $_.sample -eq 1 }) { $reference[$r.frame] = $r }
    foreach ($r in $rows | Where-Object { $_.engine -eq $engine }) {
        $e = $reference[$r.frame]
        if ($r.lines -ne $e.lines -or $r.rects -ne $e.rects -or $r.texts -ne $e.texts -or $r.geometry -ne $e.geometry) { throw "Geometry mismatch: $engine $($r.variant) sample $($r.sample), frame $($r.frame). Raw CSV retained; timing comparison withheld." }
    }
}
$sampleSummary = foreach ($group in ($rows | Where-Object { $_.frame -gt $WarmupFrames } | Group-Object engine,variant,sample)) {
    $times = @($group.Group | ForEach-Object { [double]$_.work_us } | Sort-Object)
    [pscustomobject]@{ engine=$group.Group[0].engine; variant=$group.Group[0].variant; sample=$group.Group[0].sample; p50_us=(Percentile $times 0.5); p99_us=(Percentile $times 0.99); max_us=$times[-1] }
}
$sampleSummary | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $out 'sample-summary.csv')
$summary = foreach ($group in ($rows | Where-Object { $_.frame -gt $WarmupFrames } | Group-Object engine,variant)) {
    $times = @($group.Group | ForEach-Object { [double]$_.work_us } | Sort-Object)
    $alloc = @($group.Group | Where-Object { $null -ne $_.allocated_bytes } | ForEach-Object { [long]$_.allocated_bytes })
    [pscustomobject]@{ engine=$group.Group[0].engine; variant=$group.Group[0].variant; measured_frames=$times.Count; median_sample_p50_us=(Percentile @($sampleSummary | Where-Object { $_.engine -eq $group.Group[0].engine -and $_.variant -eq $group.Group[0].variant } | ForEach-Object { $_.p50_us } | Sort-Object) 0.5); p50_us=(Percentile $times 0.5); p99_us=(Percentile $times 0.99); max_us=$times[-1]; mean_allocated_bytes_per_frame=$(if ($alloc.Count) { ($alloc | Measure-Object -Average).Average } else { $null }) }
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'summary.json')
$report = @"
# MIPS C# benchmark

All requested frames matched their engine's baseline draw counts and geometry. Results pool frames across fresh process samples, exclude the first $WarmupFrames frames, and retain collection frames. Samples alternate before/after order. Percentiles use the lower observed value at floor((n-1)*p).

Desktop measures the portable core with identical temporary instrumentation around Present: elapsed time and current-thread managed allocations from the prior Present exit to the next Present entry. It excludes Present logging and startup; frame one has no measurement. Native allocations are not included: the archived baseline uses NativeMemory, so managed allocation counts understate its total allocations. Peak process working set, private memory at completion, and whole-process CLR GC counts are recorded per desktop sample in process-stats.csv; they include startup and warmup. CLR GC counts do not measure the guest collector. PSP work_us is the existing host measurement; interpreter and JIT run sequentially in PPSSPP emulation, not on physical PSP hardware. PSP collection: $(if ($NaturalGc) { 'the baseline test host forced 30-frame collections are disabled' } else { 'the baseline test host may force collection every 30 frames' }); the safe runtime ignores this flag and uses pressure-triggered collection. The collector implementations and thresholds differ. PSP heap statistics after the rewrite include metadata and backing-array payloads but exclude array headers, while the old collector reports guest allocations, so the values are not directly comparable. Per-sample heap maxima, collection counts, and generated code bytes are in process-stats.csv; EBOOT sizes and hashes are in manifest.json. Per-sample timing percentiles are in sample-summary.csv. The same archived guest DLL is used for every run. Runtime configuration, raw per-frame data, binaries and logs are retained beside this report.

$(($summary | Format-Table -AutoSize | Out-String).Trim())
"@
Set-Content -LiteralPath (Join-Path $out 'report.md') -Value $report
Write-Host "Benchmark complete: $out/report.md"
