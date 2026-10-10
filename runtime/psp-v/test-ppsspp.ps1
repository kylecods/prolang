param(
    [string]$Emulator = 'C:/Program Files/PPSSPP/PPSSPPWindows64.exe',
    [switch]$SkipBuild,
    [int]$TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
if (!(Test-Path -LiteralPath $Emulator)) { throw 'Pass -Emulator with the PPSSPP executable path' }
Push-Location $repo
try {
    if (!$SkipBuild) {
        & "$PSScriptRoot/test.ps1"
        & "$PSScriptRoot/build.ps1"
    }
    # PPSSPP 1.20.x does not implement --memstick. A portable copy with no
    # installed.txt keeps emulator settings and caches inside artifacts/.
    $portable = "$repo/artifacts/psp-tests/emulator"
    New-Item -ItemType Directory -Force $portable | Out-Null
    Copy-Item -LiteralPath $Emulator -Destination "$portable/PPSSPPWindows64.exe" -Force
    $assets = Join-Path (Split-Path -Parent $Emulator) 'assets'
    if (!(Test-Path -LiteralPath $assets)) { throw 'PPSSPP assets directory is missing' }
    Copy-Item -LiteralPath $assets -Destination $portable -Recurse -Force
    foreach ($case in 'smoke','arithmetic','boxed','structs','psp_api','unsupported','div_zero','assert_fail','call_depth') {
        $out = "$repo/artifacts/psp-tests/$case"
        New-Item -ItemType Directory -Force $out | Out-Null
        Copy-Item -LiteralPath artifacts/msil-psp/EBOOT.PBP -Destination "$out/EBOOT.PBP" -Force
        Copy-Item -LiteralPath "artifacts/msil-$case/program.dll" -Destination "$out/program.dll" -Force
        if (Test-Path -LiteralPath "$out/runtime.log") { Remove-Item -LiteralPath "$out/runtime.log" }
        Set-Content -LiteralPath "$out/test.ini" -Value "[General]`nPauseOnLostFocus = False`n[Graphics]`nGraphicsBackend = 2"
        # Quote each path because Start-Process joins its argument array.
        $emuArgs = @("--log=`"$out/emulator.log`"", "--appendconfig=`"$out/test.ini`"", '-direct3d11', '--windowed', "`"$out/EBOOT.PBP`"")
        $process = Start-Process -FilePath "$portable/PPSSPPWindows64.exe" -ArgumentList $emuArgs -WorkingDirectory $out -WindowStyle Hidden -PassThru
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            $log = ''
            do {
                Start-Sleep -Milliseconds 200
                if (Test-Path -LiteralPath "$out/runtime.log") { $log = Get-Content -LiteralPath "$out/runtime.log" -Raw }
                if ($process.HasExited) { throw "PPSSPP exited before reporting a result: $case" }
            } until ($log -match 'PROLANG (OK|FAIL)' -or [DateTime]::UtcNow -ge $deadline)
            $expected = switch ($case) {
                smoke { 'PASS[\s\S]*PROLANG OK' }
                arithmetic { 'ARITHMETIC PASS[\s\S]*PROLANG OK' }
                boxed { 'BOXED PASS[\s\S]*PROLANG OK' }
                structs { 'STRUCTS PASS[\s\S]*PROLANG OK' }
                psp_api { 'PSP API PASS[\s\S]*PROLANG OK' }
                unsupported { 'unsupported signature type[\s\S]*PROLANG FAIL' }
                div_zero { 'division by zero[\s\S]*PROLANG FAIL' }
                assert_fail { 'before failure[\s\S]*assertion failed: expected failure[\s\S]*PROLANG FAIL' }
                call_depth { 'call depth limit exceeded[\s\S]*PROLANG FAIL' }
            }
            if ($log -notmatch $expected) { throw "PSP test failed or timed out: $case`n$log`nSee $out/emulator.log" }
            Write-Host "PASS in PPSSPP: $case ($out/runtime.log)"
        } finally {
            if (!$process.HasExited) {
                $null = $process.CloseMainWindow()
                if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id }
            }
            $process.Dispose()
        }
    }
} finally { Pop-Location }
