# Builds the text editor and its test suite.
#
# Usage:
#   .\build.ps1                 build both
#   .\build.ps1 -Run            build, then start the editor
#   .\build.ps1 -Run file.prl   build, then open file.prl in the editor
#   .\build.ps1 -Test           build, then run the test suite and report its exit code
#   .\build.ps1 -Install        build, then install it for the current user with a Start Menu entry
#
# Only one file is handed to the compiler. `import` is transitive, so naming main.prl pulls in the
# editor's four modules — keys, buffer, syntax, editor — and, through them, the OpenGL host and the
# whole of the ui/ library under std/.
#
# The test suite covers the three pure modules; the window loop itself cannot run under a harness.
# The same suite runs under `dotnet test` through tests/run_tests.prl's entry in
# src/ProLang.Tests/Infrastructure/TestCorpus.cs.

[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Test,
    [switch]$Install,
    [string]$File
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$outDir  = Join-Path $root "bin"

# Building the compiler also builds GLHelper and drops it into the compiler's lib/ directory,
# which is how `import "gl"` — and through it the whole OpenGL host — resolves.
Write-Host "==> Building the ProLang compiler and GLHelper..." -ForegroundColor Cyan
dotnet build $prolang -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "compiler build failed" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# --target=winexe selects the windows subsystem, so no console window opens behind the OpenGL
# one, and implies the Microsoft.WindowsDesktop.App framework in the emitted runtimeconfig.json.
#
# --apphost writes editor.exe beside the assembly, so the editor starts like any other program
# rather than as `dotnet editor.dll`. It also carries the windows subsystem across into the
# launcher: starting a GUI through `dotnet` opens a console window regardless, because `dotnet`
# is itself a console application.
Write-Host "==> Compiling the editor..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "main.prl") `
    "--target=winexe" `
    "--apphost" `
    "-o=$(Join-Path $outDir 'editor.dll')"
if ($LASTEXITCODE -ne 0) { throw "editor compilation failed" }

# The tests are a console program: they print and are read from a terminal.
Write-Host "==> Compiling the test suite..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "tests\run_tests.prl") `
    "-o=$(Join-Path $outDir 'editor-tests.dll')"
if ($LASTEXITCODE -ne 0) { throw "test compilation failed" }

Write-Host ""
Write-Host "Built:" -ForegroundColor Green
Write-Host "  $(Join-Path $outDir 'editor.exe')"
Write-Host "  $(Join-Path $outDir 'editor-tests.dll')"

if ($Test) {
    Write-Host ""
    Write-Host "==> Running the test suite..." -ForegroundColor Cyan
    dotnet (Join-Path $outDir "editor-tests.dll")

    # A failing check throws from the `assert` builtin, so a non-zero exit code here is the
    # suite reporting failure rather than the runtime having crashed.
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
    Write-Host "tests passed" -ForegroundColor Green
}

if ($Install) {
    Write-Host ""
    & (Join-Path $root "..\..\tools\install-app.ps1") `
        -Path (Join-Path $outDir "editor.exe") `
        -Name "ProLang Text Editor"
}

if ($Run) {
    Write-Host ""
    Write-Host "==> Starting the editor..." -ForegroundColor Cyan
    if ($File) {
        & (Join-Path $outDir "editor.exe") $File
    } else {
        & (Join-Path $outDir "editor.exe")
    }
}
