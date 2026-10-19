param(
    [string]$Program = "$PSScriptRoot/../../examples/18-psp-cube/psp_cube.prl",
    [string]$Distro = 'Ubuntu',
    [switch]$GenerateOnly
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$out = "$repo/artifacts/mips-cs/psp"
Push-Location $repo
try {
    foreach ($project in 'src/ProLang/ProLang.csproj','runtime/mips-cs/portable/Portable.csproj','runtime/mips-cs/cs-to-c/CsToC.csproj') {
        & dotnet build $project -c Release --nologo -v quiet
        if ($LASTEXITCODE) { throw "Build failed: $project" }
    }
    New-Item -ItemType Directory -Force $out | Out-Null
    & dotnet src/ProLang/bin/Release/net10.0/ProLang.dll $Program -o "$out/program.dll"
    if ($LASTEXITCODE) { throw 'ProLang MSIL compilation failed' }
    & dotnet runtime/mips-cs/cs-to-c/bin/Release/net10.0/CsToC.dll "$out/runtime.c" "$PSScriptRoot/portable/Core.cs" "$PSScriptRoot/portable/DesktopHost.cs" "$PSScriptRoot/portable/Decode.cs" "$PSScriptRoot/portable/Execute.cs" "$PSScriptRoot/portable/Jit.cs"
    if ($LASTEXITCODE) { throw 'Portable C# translation failed' }
    if (!$GenerateOnly) {
        $script = (& wsl -d $Distro --exec wslpath -a "$PSScriptRoot/psp/build.sh").Trim()
        if ($LASTEXITCODE) { throw 'Cannot access WSL PSP build script' }
        & wsl -d $Distro --exec bash $script
        if ($LASTEXITCODE) { throw 'PSP EBOOT build failed' }
    }
    Write-Host "PSP runtime: $out/EBOOT.PBP"
    Write-Host "Replaceable IL program: $out/program.dll"
} finally { Pop-Location }
