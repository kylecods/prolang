param(
    [int]$Frames = 60,
    [int]$Samples = 5,
    [string]$VLog,
    [string]$DumpBin,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$out = "$repo/artifacts/mips-cs"
Push-Location $repo
$savedTier = $env:DOTNET_TieredCompilation
$savedReady = $env:DOTNET_ReadyToRun
$savedDisasm = $env:DOTNET_JitDisasm
$savedFile = $env:DOTNET_JitStdOutFile
try {
    New-Item -ItemType Directory -Force "$out/jit", "$out/aot" | Out-Null
    if (!$SkipBuild) {
        & dotnet build src/ProLang/ProLang.csproj -c Release --nologo -v quiet
        if ($LASTEXITCODE) { throw 'Compiler build failed' }
        & dotnet build "$PSScriptRoot/ProLang.Mips.csproj" -c Release -p:NuGetAudit=false --nologo -v quiet
        if ($LASTEXITCODE) { throw 'Runtime build failed' }
        & dotnet publish "$PSScriptRoot/ProLang.Mips.csproj" -c Release -r win-x64 -p:PublishAot=true -p:NuGetAudit=false -p:IlcGenerateMapFile=true --ignore-failed-sources -o "$out/native" --nologo -v minimal
        if ($LASTEXITCODE) { throw 'NativeAOT publish failed' }
    }
    & dotnet src/ProLang/bin/Release/net10.0/ProLang.dll examples/18-psp-cube/psp_cube.prl -o "$out/cube/program.dll"
    if ($LASTEXITCODE) { throw 'Cube compilation failed' }
    # Every measured CoreCLR method uses its fully optimized compilation from the start.
    $env:DOTNET_TieredCompilation = '0'
    $env:DOTNET_ReadyToRun = '0'
    $runtime = "$PSScriptRoot/bin/Release/net10.0/ProLang.Mips.dll"
    $native = "$out/native/ProLang.Mips.exe"
    # Cecil is already shipped with the compiler. This tooling reads the C# host's
    # original IL, before NativeAOT trimming, independently of guest IL decoding.
    Add-Type -Path "$repo/src/ProLang/bin/Release/net10.0/Mono.Cecil.dll"
    $hostAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($runtime)
    try {
        $hostIl = [Collections.Generic.List[string]]::new()
        $hostIl.Add('C# host IL before trimming/AOT; see il.txt for the cube guest IL.')
        foreach ($type in $hostAssembly.MainModule.Types) {
            foreach ($method in $type.Methods) {
                if (($type.FullName -eq 'ProLang.Mips.MipsCpu' -and $method.Name -eq 'Execute') -or ($type.FullName -eq 'ProLang.Mips.Vm' -and $method.Name -eq 'Step')) {
                    $hostIl.Add("`n$($method.FullName)")
                    foreach ($instruction in $method.Body.Instructions) { $hostIl.Add($instruction.ToString()) }
                }
            }
        }
        if ($hostIl.Count -lt 50) { throw 'C# host IL capture failed' }
        $hostIl | Set-Content -Encoding utf8 "$out/jit/host-il.txt"
        Copy-Item -LiteralPath "$out/jit/host-il.txt" -Destination "$out/aot/host-il.txt" -Force
    } finally { $hostAssembly.Dispose() }
    [string[]]$referenceArgs = @(if ($VLog) { "--v-log=$([IO.Path]::GetFullPath($VLog))" })
    if ($referenceArgs.Count) { Write-Host "V reference: $VLog" }
    # Collect disassembly in an independent run, outside the timing runs.
    $env:DOTNET_JitDisasm = 'ProLang.Mips.MipsCpu:Execute ProLang.Mips.Vm:Step'
    $env:DOTNET_JitStdOutFile = "$out/jit/host-jit.txt"
    & dotnet $runtime run "$out/cube/program.dll" --frames=2 --engine=optimized | Out-Null
    if ($LASTEXITCODE -or !(Test-Path "$out/jit/host-jit.txt")) { throw 'Host JIT disassembly capture failed' }
    $env:DOTNET_JitDisasm = $null
    $env:DOTNET_JitStdOutFile = $null
    & dotnet $runtime profile "$out/cube/program.dll" "--frames=$Frames" "--samples=$Samples" "--output=$out/jit" @referenceArgs
    if ($LASTEXITCODE) { throw 'CoreCLR cube profile failed' }
    & $native self-test
    if ($LASTEXITCODE) { throw 'NativeAOT self-test failed' }
    & $native profile "$out/cube/program.dll" "--frames=$Frames" "--samples=$Samples" "--output=$out/aot" @referenceArgs
    if ($LASTEXITCODE) { throw 'NativeAOT cube profile failed' }
    if (!$DumpBin) {
        $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
        if (Test-Path $vswhere) { $DumpBin = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC/Tools/MSVC/*/bin/Hostx64/x64/dumpbin.exe' | Select-Object -First 1 }
    }
    if (!$DumpBin -or !(Test-Path $DumpBin)) { throw 'Use -DumpBin with the Visual C++ dumpbin.exe path to capture NativeAOT machine instructions' }
    & $DumpBin /DISASM:NOBYTES $native | Set-Content -Encoding utf8 "$out/aot/host-aot.txt"
    if ($LASTEXITCODE) { throw 'NativeAOT disassembly failed' }
    Copy-Item -LiteralPath "$out/jit/host-jit.txt" -Destination "$out/aot/host-jit.txt" -Force
    Copy-Item -LiteralPath "$out/aot/host-aot.txt" -Destination "$out/jit/host-aot.txt" -Force
    $hot = [Collections.Generic.List[string]]::new()
    $inMethod = $false
    foreach ($line in [IO.File]::ReadLines("$out/aot/host-aot.txt")) {
        if ($line -match '^\S.*:$') { $inMethod = $line -match '^ProLang_Mips_ProLang_Mips_(MipsCpu__Execute|Vm__Step):$' }
        if ($inMethod) { $hot.Add($line) }
    }
    if ($hot.Count -lt 50) { throw 'Could not locate named runtime methods in NativeAOT disassembly (keep its PDB beside the executable)' }
    $hot | Set-Content -Encoding utf8 "$out/aot/host-aot-hot.txt"
    Copy-Item -LiteralPath "$out/aot/host-aot-hot.txt" -Destination "$out/jit/host-aot-hot.txt" -Force
    foreach ($mode in 'jit', 'aot') {
        $htmlPath = "$out/$mode/report.html"
        $html = [IO.File]::ReadAllText($htmlPath)
        $html += "<h2>Host instructions</h2><p><a href='host-il.txt'>Original C# host IL</a> · <a href='host-aot-hot.txt'>Named NativeAOT hot methods</a> · <a href='host-jit.txt'>Complete CoreCLR hot methods</a></p>"
        foreach ($listing in 'host-il.txt', 'host-jit.txt', 'host-aot-hot.txt') {
            $excerpt = ([IO.File]::ReadLines("$out/$mode/$listing") | Select-Object -First 45) -join "`n"
            $html += "<h3>$listing (excerpt)</h3><pre>$([Net.WebUtility]::HtmlEncode($excerpt))</pre>"
        }
        # Put a short original IL / optimized MIPS comparison directly in the report.
        foreach ($listing in 'il.txt', 'optimized.txt') {
            $lines = [IO.File]::ReadAllLines("$out/$mode/$listing")
            $begin = 0
            for ($n = 0; $n -lt $lines.Length; $n++) { if ($lines[$n] -match '^(Program::cube_fp_mul\(|cube_fp_mul file-offset=)') { $begin = $n; break } }
            $end = [Math]::Min($begin + 45, $lines.Length - 1)
            $excerpt = $lines[$begin..$end] -join "`n"
            $html += "<h3>Cube fixed-point multiplication: $listing (excerpt)</h3><pre>$([Net.WebUtility]::HtmlEncode($excerpt))</pre>"
        }
        [IO.File]::WriteAllText($htmlPath, $html)
    }
    Write-Host "NativeAOT report: $out/aot/report.html"
    Write-Host "CoreCLR comparison: $out/jit/report.html"
    $global:LASTEXITCODE = 0
} finally {
    $env:DOTNET_TieredCompilation = $savedTier
    $env:DOTNET_ReadyToRun = $savedReady
    $env:DOTNET_JitDisasm = $savedDisasm
    $env:DOTNET_JitStdOutFile = $savedFile
    Pop-Location
}
