# Build script for WinForms examples
# Usage: .\build.ps1
# Run each compiled example with: dotnet bin\<name>.dll

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$helperProj = Join-Path $root "helper\WinFormsHelper.csproj"
$helperDll  = Join-Path $root "helper\bin\WinFormsHelper.dll"
$outDir     = Join-Path $root "bin"

# Step 1 — build the C# WinForms helper library
Write-Host "==> Building WinFormsHelper..." -ForegroundColor Cyan
dotnet build $helperProj -c Debug -o (Join-Path $root "helper\bin") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "WinFormsHelper build failed" }

# Step 2 — compile each prolang example
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
# Copy WinFormsHelper.dll next to the compiled example DLLs so the runtime can find it
Copy-Item -Force (Join-Path $root "helper\bin\WinFormsHelper.dll") $outDir

$examples = @(
    "01_hello_world",
    "02_message_box",
    "03_input_dialog",
    "04_counter",
    "05_color_demo",
    "06_native_enum",
    "07_paint_demo"
)

foreach ($name in $examples) {
    $src = Join-Path $root "$name.prl"
    $out = Join-Path $outDir "$name.dll"
    Write-Host "==> Compiling $name.prl ..." -ForegroundColor Cyan
    dotnet run --project $prolang --no-build -- "$src" "--o=$out"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $name.prl" }

    # Patch runtimeconfig.json to use Microsoft.WindowsDesktop.App so WinForms loads
    $rcPath = Join-Path $outDir "$name.runtimeconfig.json"
    $rc = @{
        runtimeOptions = @{
            tfm = "net10.0"
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
