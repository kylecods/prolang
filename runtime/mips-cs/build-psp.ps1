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
    & dotnet runtime/mips-cs/cs-to-c/bin/Release/net10.0/CsToC.dll "$out/runtime.c" "$PSScriptRoot/portable/Model.cs" "$PSScriptRoot/portable/DesktopHost.cs" "$PSScriptRoot/portable/ValueKind.cs" "$PSScriptRoot/portable/ElementType.cs" "$PSScriptRoot/portable/TypeEncoding.cs" "$PSScriptRoot/portable/Il.cs" "$PSScriptRoot/portable/Limits.cs" "$PSScriptRoot/portable/JitAbi.cs" "$PSScriptRoot/portable/MetadataTable.cs" "$PSScriptRoot/portable/MetadataToken.cs" "$PSScriptRoot/portable/MipsOp.cs" "$PSScriptRoot/portable/MipsFunct.cs" "$PSScriptRoot/portable/MipsReg.cs" "$PSScriptRoot/portable/JitFrame.cs" "$PSScriptRoot/portable/Metadata.cs" "$PSScriptRoot/portable/TypeSystem.cs" "$PSScriptRoot/portable/Loader.cs" "$PSScriptRoot/portable/Decoder.cs" "$PSScriptRoot/portable/Verifier.cs" "$PSScriptRoot/portable/Planner.cs" "$PSScriptRoot/portable/Natives.cs" "$PSScriptRoot/portable/Objects.cs" "$PSScriptRoot/portable/Heap.cs" "$PSScriptRoot/portable/Arithmetic.cs" "$PSScriptRoot/portable/Interpreter.cs" "$PSScriptRoot/portable/MipsAssembler.cs" "$PSScriptRoot/portable/RegisterCache.cs" "$PSScriptRoot/portable/Jit.cs" "$PSScriptRoot/portable/Runtime.cs"
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
