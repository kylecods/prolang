param(
    [string]$VCompiler,
    [string]$Program = "$PSScriptRoot/fixtures/smoke.prl",
    [string]$Distro = 'Ubuntu',
    [ValidateSet('s','2','3')][string]$Optimization = '3',
    [switch]$GenerateOnly
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
Push-Location $repo
try {
    if (!$VCompiler) {
        $localV = "$repo/artifacts/toolchains/v_0.4.12/v/v.exe"
        $VCompiler = if (Test-Path -LiteralPath $localV) { $localV } else { 'v' }
    }
    $version = & $VCompiler version
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^V 0\.4\.12\b') {
        throw 'The PSP C compatibility profile requires V 0.4.12. Pass -VCompiler with its path.'
    }
    $out = "$repo/artifacts/msil-psp"
    New-Item -ItemType Directory -Force $out | Out-Null
    & dotnet build src/ProLang/ProLang.csproj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'ProLang build failed' }
    & dotnet src/ProLang/bin/Release/net10.0/ProLang.dll $Program -o "$out/program.dll"
    if ($LASTEXITCODE -ne 0) { throw 'ProLang assembly compilation failed' }
    & $VCompiler -os linux -arch i386 -d psp -d no_backtrace -d no_segfault_handler -gc none -o "$out/runtime.c" "$PSScriptRoot"
    if ($LASTEXITCODE -ne 0) { throw 'V C generation failed' }
    # V 0.4.12 includes the Unix closure header even when no closures survive
    # dead-code elimination. Exclude only that unavailable header on PSP.
    $c = Get-Content -LiteralPath "$out/runtime.c" -Raw
    $guard = '#if !defined(_VFREESTANDING) && !defined(__vinix__)'
    if (!$c.Contains($guard) -or $c -match '\b(?:mmap|mprotect)\(') {
        throw 'Unexpected V C prelude or active closure code; review the PSP compatibility profile.'
    }
    $c = $c.Replace($guard, "$guard && !defined(__PSP__)")
    [IO.File]::WriteAllText("$out/runtime.c", $c, [Text.UTF8Encoding]::new($false))
    if (!$GenerateOnly) {
        $windowsScript = "$PSScriptRoot/build-psp.sh".Replace('\', '/')
        $wslScript = & wsl -d $Distro --exec wslpath -a $windowsScript
        if ($LASTEXITCODE -ne 0) { throw 'Cannot translate build script path for WSL' }
        & wsl -d $Distro --exec bash $wslScript.Trim() $Optimization
        if ($LASTEXITCODE -ne 0) { throw 'PSP build failed' }
    }
    Write-Host "Runtime files: $out"
} finally { Pop-Location }
