# Builds the pixel editor and its test suite.
#
# Usage:
#   .\build.ps1          build both
#   .\build.ps1 -Run     build, then start the editor
#   .\build.ps1 -Test    build, then run the test suite and report its exit code
#
# Only two files are handed to the compiler. `import` is transitive, so naming main.prl pulls in
# the other eleven modules, and naming tests/run_tests.prl pulls in the harness and every test.

[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Test
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$outDir  = Join-Path $root "bin"

# Building the compiler also builds WinFormsHelper and drops it into the compiler's lib/
# directory, which is how `import "winforms"` resolves. See the BuildWinFormsHelper target in
# src/ProLang/ProLang.csproj.
Write-Host "==> Building the ProLang compiler and WinFormsHelper..." -ForegroundColor Cyan
dotnet build $prolang -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "compiler build failed" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# --target=winexe selects the windows subsystem, so no console window opens behind the form, and
# implies the Microsoft.WindowsDesktop.App framework in the emitted runtimeconfig.json. Before
# those flags existed this script had to rewrite that file after the compiler had written it.
Write-Host "==> Compiling the editor..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "main.prl") `
    "--target=winexe" `
    "-o=$(Join-Path $outDir 'pixel-editor.dll')"
if ($LASTEXITCODE -ne 0) { throw "editor compilation failed" }

# The tests are a console program: they print and are read from a terminal.
Write-Host "==> Compiling the test suite..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "tests\run_tests.prl") `
    "-o=$(Join-Path $outDir 'pixel-editor-tests.dll')"
if ($LASTEXITCODE -ne 0) { throw "test compilation failed" }

Write-Host ""
Write-Host "Built:" -ForegroundColor Green
Write-Host "  $(Join-Path $outDir 'pixel-editor.dll')"
Write-Host "  $(Join-Path $outDir 'pixel-editor-tests.dll')"

if ($Test) {
    Write-Host ""
    Write-Host "==> Running the test suite..." -ForegroundColor Cyan
    dotnet (Join-Path $outDir "pixel-editor-tests.dll")

    # A failing check throws from the `assert` builtin, so a non-zero exit code here is the
    # suite reporting failure rather than the runtime having crashed.
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
    Write-Host "tests passed" -ForegroundColor Green
}

if ($Run) {
    Write-Host ""
    Write-Host "==> Starting the editor..." -ForegroundColor Cyan
    dotnet (Join-Path $outDir "pixel-editor.dll")
}
