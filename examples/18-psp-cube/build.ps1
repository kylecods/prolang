# Builds the cube demo.
#
# Usage:
#   .\build.ps1        build the desktop version and the test suite
#   .\build.ps1 -Run   build, then start the desktop version
#   .\build.ps1 -Test  build, then run the test suite and report its exit code
#   .\build.ps1 -Psp   transpile the PSP version to C99 under .prolang/psp/
#
# The cube itself is in cube.prl and names no platform: it is a scene painter that appends
# display-list records. Both the desktop and PSP programs below import that one file.

[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Test,
    [switch]$Psp
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$outDir  = Join-Path $root "bin"

# Building the compiler also builds WinFormsHelper into its lib/ directory and copies std/**/*.prl
# beside it. Skipping this after editing a std module silently compiles against the old copy.
Write-Host "==> Building the ProLang compiler and WinFormsHelper..." -ForegroundColor Cyan
dotnet build $prolang -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "compiler build failed" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "==> Compiling the desktop cube..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "cube_desktop.prl") `
    "--target=winexe" `
    "--apphost" `
    "-o=$(Join-Path $outDir 'cube.dll')"
if ($LASTEXITCODE -ne 0) { throw "desktop compilation failed" }

Write-Host "==> Compiling the test suite..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "tests\run_tests.prl") `
    "-o=$(Join-Path $outDir 'cube-tests.dll')"
if ($LASTEXITCODE -ne 0) { throw "test compilation failed" }

Write-Host ""
Write-Host "Built:" -ForegroundColor Green
Write-Host "  $(Join-Path $outDir 'cube.exe')"
Write-Host "  $(Join-Path $outDir 'cube-tests.dll')"

if ($Psp) {
    Write-Host ""
    Write-Host "==> Transpiling the PSP version..." -ForegroundColor Cyan
    dotnet run --project $prolang -c Release --no-build -- `
        (Join-Path $root "psp_cube.prl") "--emit-psp"
    if ($LASTEXITCODE -ne 0) { throw "PSP transpilation failed" }

    Write-Host "Now build it with the pspdev toolchain:" -ForegroundColor Yellow
    Write-Host "  cd .prolang/psp && make -f Makefile.psp"
}

if ($Test) {
    Write-Host ""
    Write-Host "==> Running the test suite..." -ForegroundColor Cyan
    dotnet (Join-Path $outDir "cube-tests.dll")

    # A failing check throws from the `assert` builtin, so a non-zero exit code here is the suite
    # reporting failure rather than the runtime having crashed.
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
    Write-Host "tests passed" -ForegroundColor Green
}

if ($Run) {
    Write-Host ""
    Write-Host "==> Starting the cube..." -ForegroundColor Cyan
    & (Join-Path $outDir "cube.exe")
}
