param(
    [string]$Emulator = 'C:/Program Files/PPSSPP/PPSSPPWindows64.exe',
    [int]$Frames = 120,
    [int]$TimeoutSeconds = 180,
    [switch]$SkipBuild,
    [switch]$NoJit,
    [switch]$NaturalGc,
    [switch]$Fixtures
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
Push-Location $repo
try {
    if (!$SkipBuild) { & "$PSScriptRoot/build-psp.ps1" }
    $portable = "$repo/artifacts/mips-cs/emulator"
    New-Item -ItemType Directory -Force $portable | Out-Null
    Copy-Item -LiteralPath $Emulator -Destination "$portable/PPSSPPWindows64.exe" -Force
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $Emulator) 'assets') -Destination $portable -Recurse -Force
    $cases = if ($Fixtures) { @('smoke','arithmetic','boxed','structs','psp_api','div_zero','assert_fail','call_depth','unsupported') } else { @('cube') }
    foreach ($case in $cases) {
        $out = "$repo/artifacts/mips-cs/psp-tests/$(if ($NoJit) {'interpreter'} else {'jit'})/$case"
        New-Item -ItemType Directory -Force $out | Out-Null
        Copy-Item -LiteralPath "$repo/artifacts/mips-cs/psp/EBOOT.PBP" -Destination "$out/EBOOT.PBP" -Force
        $source = if ($case -eq 'cube') { "$repo/examples/18-psp-cube/psp_cube.prl" } else { "$repo/runtime/psp-v/fixtures/$case.prl" }
        & dotnet src/ProLang/bin/Release/net10.0/ProLang.dll $source -o "$out/program.dll"
        if ($LASTEXITCODE) { throw "Fixture compilation failed: $case" }
        foreach ($name in 'runtime.log','first-frame.bmp','last-frame.bmp','jit-mips.bin') { if (Test-Path -LiteralPath "$out/$name") { Remove-Item -LiteralPath "$out/$name" } }
        Set-Content -LiteralPath "$out/test-frames.txt" -Value $Frames
        if ($NaturalGc) { Set-Content -LiteralPath "$out/natural-gc.txt" -Value 'automatic collection only' }
        elseif (Test-Path -LiteralPath "$out/natural-gc.txt") { Remove-Item -LiteralPath "$out/natural-gc.txt" }
        Set-Content -LiteralPath "$out/test.ini" -Value "[General]`nPauseOnLostFocus = False`n[Graphics]`nGraphicsBackend = 2"
        if ($NoJit) { Set-Content -LiteralPath "$out/jit-off.txt" -Value 'interpret' }
        elseif (Test-Path -LiteralPath "$out/jit-off.txt") { Remove-Item -LiteralPath "$out/jit-off.txt" }
        $emuArgs = @("--log=`"$out/emulator.log`"", "--appendconfig=`"$out/test.ini`"", '-direct3d11', '--windowed', "`"$out/EBOOT.PBP`"")
        $process = Start-Process -FilePath "$portable/PPSSPPWindows64.exe" -ArgumentList $emuArgs -WorkingDirectory $out -WindowStyle Hidden -PassThru
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            $log = ''
            do {
                Start-Sleep -Milliseconds 250
                if (Test-Path -LiteralPath "$out/runtime.log") { $log = Get-Content -LiteralPath "$out/runtime.log" -Raw }
                if ($process.HasExited) { throw "Emulator exited: $case`n$log" }
            } until ($log -match 'PROLANG (OK|FAIL)' -or [DateTime]::UtcNow -ge $deadline)
            $negative = @{ div_zero = 'division by zero'; assert_fail = 'assertion failed'; call_depth = 'call depth limit exceeded'; unsupported = 'unsupported signature type' }
            if ($negative.ContainsKey($case)) {
                if ($log -notmatch 'PROLANG FAIL' -or $log -notmatch $negative[$case]) { throw "Expected failure missing: $case`n$log" }
            } else {
                if ($log -notmatch 'PROLANG OK') { throw "Failed or timed out: $case`n$log" }
                if ($case -eq 'cube') {
                    $draws = [regex]::Matches($log, 'FRAME (\d+) lines=(\d+) rects=(\d+) texts=(\d+) geometry=([0-9a-f]+)')
                    if ($draws.Count -lt $Frames -or @($draws | Where-Object { $_.Groups[2].Value -ne '12' }).Count) { throw 'Cube frames or edges missing' }
                    $reference = "$repo/artifacts/psp-tests/cube/runtime.log"
                    if (Test-Path -LiteralPath $reference) {
                        $expected = [regex]::Matches([IO.File]::ReadAllText($reference), 'FRAME (\d+) lines=(\d+) rects=(\d+) texts=(\d+) geometry=([0-9a-f]+)')
                        for ($n = 0; $n -lt [Math]::Min($draws.Count, $expected.Count); $n++) { if ($draws[$n].Groups[5].Value -ne $expected[$n].Groups[5].Value) { throw "V geometry mismatch: frame $($n+1)" } }
                    }
                    $work = @([regex]::Matches($log, 'FRAME (\d+)[^\r\n]*work_us=(\d+)') | Where-Object { [int]$_.Groups[1].Value -gt 1 } | ForEach-Object { [int]$_.Groups[2].Value } | Sort-Object)
                    $p50 = $work[[int][Math]::Floor(($work.Count-1)*0.5)]
                    $p99 = $work[[int][Math]::Floor(($work.Count-1)*0.99)]
                    Write-Host "Cube work INCLUDING collection frames: p50=$p50 us p99=$p99 us max=$($work[-1]) us"
                }
            }
            if ($log -notmatch 'RUNTIME csharp-portable') { throw 'Wrong runtime' }
            $expectedEngine = if ($NoJit) { 'interpreter' } else { 'jit' }
            if ($log -notmatch "ENGINE $expectedEngine") { throw "Expected $expectedEngine engine" }
            if ($case -in 'smoke','arithmetic','boxed','structs') {
                $expectedOutput = (& dotnet "$out/program.dll" | Out-String).Replace("`r`n","`n").Trim()
                if ($LASTEXITCODE -or !$log.Replace("`r`n","`n").Contains($expectedOutput)) { throw "PSP output differs from .NET: $case" }
            }
            Write-Host "PASS PSP $case : $out/runtime.log"
        } finally {
            if (!$process.HasExited) { $null = $process.CloseMainWindow(); if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id; $process.WaitForExit() } }
            $process.Dispose()
        }
    }
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
