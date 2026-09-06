# Builds and runs the ProLang OpenGL UI Widgets Demo.
#
# Usage:
#   .\build.ps1        build
#   .\build.ps1 -Run   build and launch the demo

[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$outDir  = Join-Path $root "bin"

Write-Host "==> Building ProLang compiler and libraries..." -ForegroundColor Cyan
dotnet build $prolang -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "compiler build failed" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "==> Compiling OpenGL UI Widgets demo..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "main.prl") `
    "--target=winexe" `
    "--apphost" `
    "-o=$(Join-Path $outDir 'opengl_widgets.dll')"
if ($LASTEXITCODE -ne 0) { throw "demo compilation failed" }

Write-Host ""
Write-Host "Built: $(Join-Path $outDir 'opengl_widgets.exe')" -ForegroundColor Green

if ($Run) {
    Write-Host ""
    Write-Host "==> Starting OpenGL UI Widgets demo..." -ForegroundColor Cyan
    & (Join-Path $outDir "opengl_widgets.exe")
}
