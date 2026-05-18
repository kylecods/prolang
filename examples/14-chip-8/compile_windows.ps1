$ErrorActionPreference = "Stop"
$root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang  = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$outDir   = Join-Path $root "bin"

# Step 1 — build ProLang (the AfterTargets="Build" target in ProLang.csproj builds
#           WinFormsHelper and copies it to the compiler's lib/ directory on Windows)
Write-Host "==> Building ProLang (+ WinFormsHelper stdlib)..." -ForegroundColor Cyan
dotnet build $prolang -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "ProLang build failed" }

$libDll = Join-Path (Split-Path $prolang -Parent) "bin\Debug\net10.0\lib\WinFormsHelper.dll"
if (-not (Test-Path $libDll)) {
    throw "WinFormsHelper.dll was not produced at '$libDll'. Ensure you are on Windows and the AfterTargets build step succeeded."
}

# Step 2 — prepare output directory
# (WinFormsHelper.dll is copied next to each compiled DLL automatically by the compiler)
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$examples = @(
    "chip-8"
)

# Step 3 — compile each example
foreach ($name in $examples) {
    $src = Join-Path $root "$name.prl"
    $out = Join-Path $outDir "$name.dll"
    Write-Host "==> Compiling $name.prl ..." -ForegroundColor Cyan
    dotnet run --project $prolang --no-build -- "$src" "--o=$out"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $name.prl" }

    # Patch runtimeconfig.json: compiled DLLs need the Windows Desktop framework
    # (WinFormsHelper.dll depends on System.Windows.Forms)
    $rcPath = Join-Path $outDir "$name.runtimeconfig.json"
    $rc = @{
        runtimeOptions = @{
            tfm       = "net10.0"
            framework = @{
                name    = "Microsoft.WindowsDesktop.App"
                version = "10.0.0"
            }
        }
    } | ConvertTo-Json -Depth 5
    Set-Content -Path $rcPath -Value $rc -Encoding UTF8

    Write-Host "    -> $out" -ForegroundColor Green
}

Write-Host ""
Write-Host "All examples built successfully." -ForegroundColor Green
Write-Host "Run an example with: dotnet bin\<name>.dll"
