param([string]$VCompiler)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
Push-Location $repo
try {
    if (!$VCompiler) {
        $localV = "$repo/artifacts/toolchains/v_0.4.12/v/v.exe"
        $VCompiler = if (Test-Path -LiteralPath $localV) { $localV } else { 'v' }
    }
    New-Item -ItemType Directory -Force artifacts | Out-Null
    & dotnet build src/ProLang/ProLang.csproj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'ProLang build failed' }
    & $VCompiler -gc none -o artifacts/prolang-msil.exe $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw 'V runtime build failed' }
    foreach ($case in 'smoke','arithmetic','boxed','structs','psp_api','assert_fail','unsupported','div_zero','call_depth') {
        & dotnet src/ProLang/bin/Release/net10.0/ProLang.dll "$PSScriptRoot/fixtures/$case.prl" -o "artifacts/msil-$case/program.dll"
        if ($LASTEXITCODE -ne 0) { throw "Fixture compilation failed: $case" }
    }
    # Run the test file directly: avoids the separate V test tool's
    # dependency on a platform-specific Boehm GC library.
    & $VCompiler -gc none -o artifacts/msil-unit-tests.exe "$PSScriptRoot/runtime_test.v"
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed' }
    Write-Host 'PASS: loader, signatures, branches, arithmetic, UTF-16, heap and struct address unit tests'
    foreach ($case in 'smoke','arithmetic','boxed','structs') {
        $programArgs = if ($case -eq 'smoke') { @('alpha','beta') } else { @() }
        $expected = (& dotnet "artifacts/msil-$case/program.dll" @programArgs | Out-String).Replace("`r`n", "`n")
        if ($LASTEXITCODE -ne 0) { throw ".NET fixture failed: $case" }
        $actual = (& ./artifacts/prolang-msil.exe "artifacts/msil-$case/program.dll" @programArgs | Out-String).Replace("`r`n", "`n")
        if ($LASTEXITCODE -ne 0 -or $actual -cne $expected) { throw "Output differs from .NET: $case`nExpected: $expected`nActual: $actual" }
        Write-Host "PASS: $case matches .NET output"
    }
    $failures = @{ assert_fail = 'before failure[\s\S]*assertion failed: expected failure'; unsupported = 'unsupported signature type'; div_zero = 'division by zero'; call_depth = 'call depth limit exceeded' }
    foreach ($case in $failures.Keys) {
        $actual = & ./artifacts/prolang-msil.exe "artifacts/msil-$case/program.dll" | Out-String
        if ($LASTEXITCODE -eq 0 -or $actual -notmatch $failures[$case]) { throw "Expected diagnostic/exit status missing: $case`n$actual" }
        Write-Host "PASS: $case fails with an explicit diagnostic"
    }
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
