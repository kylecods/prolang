# Builds the widget-toolkit counter.
#
# Usage:
#   .\build.ps1             build WinForms version
#   .\build.ps1 -OpenGL     build OpenGL version
#   .\build.ps1 -Run        build WinForms version and run
#   .\build.ps1 -OpenGL -Run build OpenGL version and run
#
# Only counter.prl or counter_gl.prl is handed to the compiler: `import` is transitive,
# pulling in the host and the whole toolkit under std/ui/.

[CmdletBinding()]
param(
    [switch]$OpenGL,
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$outDir  = Join-Path $root "bin"

# Building the compiler also builds WinFormsHelper and GLHelper into the compiler's lib/ directory,
# and copies std/**/*.prl beside it.
Write-Host "==> Building the ProLang compiler, WinFormsHelper, and GLHelper..." -ForegroundColor Cyan
dotnet build $prolang -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "compiler build failed" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$source = if ($OpenGL) { "counter_gl.prl" } else { "counter.prl" }
$exeName = if ($OpenGL) { "counter_gl.exe" } else { "counter.exe" }
$dllName = if ($OpenGL) { "counter_gl.dll" } else { "counter.dll" }

Write-Host "==> Compiling the counter ($source)..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root $source) `
    "--target=winexe" `
    "--apphost" `
    "-o=$(Join-Path $outDir $dllName)"
if ($LASTEXITCODE -ne 0) { throw "counter compilation failed" }

Write-Host ""
Write-Host "Built: $(Join-Path $outDir $exeName)" -ForegroundColor Green

if ($Run) {
    Write-Host ""
    Write-Host "==> Starting the counter..." -ForegroundColor Cyan
    & (Join-Path $outDir $exeName)
}
