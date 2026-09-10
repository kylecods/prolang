# Build script for ProLang OpenGL Cube Example
# Usage:
#   .\build.ps1          # Build compiler and compile cube.prl
#   .\build.ps1 -Run     # Build and immediately run the OpenGL cube

param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"
$root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang  = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$outDir   = Join-Path $root "bin"
$outDll   = Join-Path $outDir "cube.dll"

Write-Host "==> Building ProLang compiler (+ GLHelper)..." -ForegroundColor Cyan
dotnet build $prolang -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "ProLang build failed" }

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

Write-Host "==> Compiling cube.prl..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release -- (Join-Path $root "cube.prl") -o $outDll
if ($LASTEXITCODE -ne 0) { throw "Compilation of cube.prl failed" }

Write-Host "==> Build successful: $outDll" -ForegroundColor Green

if ($Run) {
    Write-Host "==> Launching OpenGL Cube..." -ForegroundColor Cyan
    & dotnet $outDll
}
