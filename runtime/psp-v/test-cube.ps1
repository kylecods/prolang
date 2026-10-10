param(
    [string]$Emulator = 'C:/Program Files/PPSSPP/PPSSPPWindows64.exe',
    [int]$Frames = 60,
    [int]$TimeoutSeconds = 180,
    [ValidateSet('s','2','3')][string]$Optimization = '3',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
if ($Frames -lt 2) { throw 'At least two frames are required to verify rotation' }
Push-Location $repo
try {
    if (!$SkipBuild) { & "$PSScriptRoot/build.ps1" -Program "$repo/examples/18-psp-cube/psp_cube.prl" -Optimization $Optimization }
    $out = "$repo/artifacts/psp-tests/cube"
    $portable = "$repo/artifacts/psp-tests/emulator"
    New-Item -ItemType Directory -Force $out, $portable | Out-Null
    Copy-Item -LiteralPath $Emulator -Destination "$portable/PPSSPPWindows64.exe" -Force
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $Emulator) 'assets') -Destination $portable -Recurse -Force
    Copy-Item -LiteralPath artifacts/msil-psp/EBOOT.PBP -Destination "$out/EBOOT.PBP" -Force
    Copy-Item -LiteralPath artifacts/msil-psp/program.dll -Destination "$out/program.dll" -Force
    foreach ($name in 'runtime.log','first-frame.bmp','last-frame.bmp') {
        if (Test-Path -LiteralPath "$out/$name") { Remove-Item -LiteralPath "$out/$name" }
    }
    Set-Content -LiteralPath "$out/test-frames.txt" -Value $Frames
    Set-Content -LiteralPath "$out/test.ini" -Value "[General]`nPauseOnLostFocus = False`n[Graphics]`nGraphicsBackend = 2"
    $emuArgs = @("--log=`"$out/emulator.log`"", "--appendconfig=`"$out/test.ini`"", '-direct3d11', '--windowed', "`"$out/EBOOT.PBP`"")
    $process = Start-Process -FilePath "$portable/PPSSPPWindows64.exe" -ArgumentList $emuArgs -WorkingDirectory $out -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $log = ''
        do {
            Start-Sleep -Milliseconds 500
            if (Test-Path -LiteralPath "$out/runtime.log") { $log = Get-Content -LiteralPath "$out/runtime.log" -Raw }
            if ($process.HasExited) { throw 'PPSSPP exited before the cube reported completion' }
        } until ($log -match 'PROLANG (OK|FAIL)' -or [DateTime]::UtcNow -ge $deadline)
        if ($log -notmatch 'PROLANG OK') { throw "Cube failed or timed out:`n$log`nSee $out/emulator.log" }
        $draws = [regex]::Matches($log, 'FRAME (\d+) lines=(\d+) rects=(\d+) texts=(\d+) geometry=([0-9a-f]+)')
        if ($draws.Count -lt $Frames) { throw 'Cube did not present the requested frames' }
        foreach ($frame in $draws) {
            if ([int]$frame.Groups[2].Value -lt 12 -or [int]$frame.Groups[3].Value -lt 4 -or [int]$frame.Groups[4].Value -lt 4) {
                throw "Missing cube edges or UI drawing: $frame"
            }
        }
        $geometry = @($draws | ForEach-Object { $_.Groups[5].Value } | Sort-Object -Unique)
        if ($geometry.Count -lt 2) { throw 'Cube geometry did not rotate' }
        $collections = [regex]::Matches($log, 'collections=(\d+)')
        if (!$collections.Count -or [int]$collections[-1].Groups[1].Value -lt 1) { throw 'Memory reclamation was not exercised' }
        if ($Frames -ge 30 -and [int]$collections[-1].Groups[1].Value -le [int]$collections[0].Groups[1].Value) {
            throw 'Collection was not exercised while persistent cube state was live'
        }
        foreach ($heap in [regex]::Matches($log, 'HEAP bytes=(\d+)')) {
            if ([int]$heap.Groups[1].Value -gt 8 * 1024 * 1024) { throw 'Cube temporary allocations exceeded the expected memory budget' }
        }
        Write-Host "PASS in PPSSPP: cube, $($draws.Count) frames, $($geometry.Count) distinct orientations, $($collections[-1].Groups[1].Value) collections"
        # Exclude initialization and the frame after each forced test collection.
        # Screenshot readback is already excluded by the host's timing boundary.
        $timings = @([regex]::Matches($log, 'FRAME (\d+)[^\r\n]* time_ms=(\d+)') |
            Where-Object { [int]$_.Groups[1].Value -gt 1 -and ([int]$_.Groups[1].Value - 1) % 30 -ne 0 } |
            ForEach-Object { [int]$_.Groups[2].Value } | Sort-Object)
        if ($timings.Count) {
            $middle = [int][math]::Floor($timings.Count / 2)
            $median = if ($timings.Count % 2) { $timings[$middle] } else { ($timings[$middle - 1] + $timings[$middle]) / 2 }
            Write-Host "Frame time: median $median ms (about $([math]::Round(1000 / [math]::Max(1, $median), 1)) fps), $($timings.Count) steady frames"
        }
        Write-Host "Frame captures: $out/first-frame.bmp and $out/last-frame.bmp"
    } finally {
        if (!$process.HasExited) {
            $null = $process.CloseMainWindow()
            if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id }
        }
        $process.Dispose()
    }
} finally { Pop-Location }
