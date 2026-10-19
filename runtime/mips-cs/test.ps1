param([switch]$SkipBuild, [string]$NativeExe)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
Push-Location $repo
try {
    if (!$SkipBuild) {
        & dotnet build src/ProLang/ProLang.csproj -c Release --nologo -v quiet
        if ($LASTEXITCODE) { throw 'Compiler build failed' }
        & dotnet build "$PSScriptRoot/ProLang.Mips.csproj" -c Release -p:NuGetAudit=false --nologo -v quiet
        if ($LASTEXITCODE) { throw 'Runtime build failed' }
    }
    $runtime = "$PSScriptRoot/bin/Release/net10.0/ProLang.Mips.dll"
    function Invoke-Runtime([string[]]$RuntimeArgs) {
        if ($NativeExe) { & $NativeExe @RuntimeArgs } else { & dotnet $runtime @RuntimeArgs }
        if ($LASTEXITCODE) { throw "Runtime failed: $RuntimeArgs" }
    }
    Invoke-Runtime @('self-test')
    foreach ($case in 'smoke', 'arithmetic', 'boxed', 'structs', 'psp_api', 'assert_fail', 'unsupported', 'div_zero', 'call_depth') {
        $dir = "$repo/artifacts/mips-cs/tests/$case"
        New-Item -ItemType Directory -Force $dir | Out-Null
        & dotnet src/ProLang/bin/Release/net10.0/ProLang.dll "$repo/runtime/psp-v/fixtures/$case.prl" -o "$dir/program.dll"
        if ($LASTEXITCODE) { throw "Fixture compilation failed: $case" }
        foreach ($engine in 'il', 'mips', 'optimized') {
            $programArgs = if ($case -eq 'smoke') { @('alpha', 'beta') } else { @() }
            $commandArgs = @('run', "$dir/program.dll", "--engine=$engine", '--') + $programArgs
            $actual = if ($NativeExe) { & $NativeExe @commandArgs 2>&1 | Out-String } else { & dotnet $runtime @commandArgs 2>&1 | Out-String }
            $status = $LASTEXITCODE
            if ($case -in 'smoke', 'arithmetic', 'boxed', 'structs') {
                $expected = & dotnet "$dir/program.dll" @programArgs | Out-String
                if ($LASTEXITCODE) { throw ".NET oracle failed: $case" }
                $actual = $actual.Replace("`r`n", "`n").Replace("PROLANG OK`n", '').TrimEnd()
                if ($status -or $actual -cne $expected.Replace("`r`n", "`n").TrimEnd()) { throw "$engine differs from .NET on $case`n$actual`nExpected:`n$expected" }
            } elseif ($case -eq 'psp_api') {
                if ($status -or $actual -notmatch 'PROLANG OK' -or $actual -notmatch 'FRAME 1 lines=1 rects=1 texts=1') { throw "PSP bridge failed: $engine`n$actual" }
            } else {
                $diagnostic = @{ assert_fail = 'assertion failed: expected failure'; unsupported = 'unsupported signature type'; div_zero = 'division by zero'; call_depth = 'call depth limit exceeded' }[$case]
                if (!$status -or $actual -notmatch $diagnostic) { throw "Missing $engine diagnostic for ${case}: $actual" }
            }
            Write-Host "PASS $engine : $case"
        }
    }
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
