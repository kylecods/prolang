# Builds the pixel editor and its test suite.
#
# Usage:
#   .\build.ps1           build both
#   .\build.ps1 -Run      build, then start the editor
#   .\build.ps1 -Test     build, then run the test suite and report its exit code
#   .\build.ps1 -Install  build, then install it for the current user with a Start Menu entry
#
# Only two files are handed to the compiler. `import` is transitive, so naming main.prl pulls in
# the editor's four modules and, through them, the whole of the ui/ library under std/.
#
# The editor's own tests cover only tools.prl now: everything it is built on lives in std/ and is
# covered by tests/std/run_tests.prl at the repository root.

[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Test,
    [switch]$Install
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

# The icon has to exist before the executable that carries it is built, so it is written by a
# small program of its own. makeicon.prl draws appicon.prl at each of the six sizes Windows asks
# for and writes one multi-resolution .ico; nothing is committed, the icon is drawn from source
# like everything else here.
#
# It is a console program that uses the shim to write the file, so it needs the desktop framework
# without the windows subsystem — the one combination --target=winexe does not cover.
Write-Host "==> Drawing the program icon..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "makeicon.prl") `
    "--target=console" `
    "--framework=windowsdesktop" `
    "-o=$(Join-Path $outDir 'makeicon.dll')"
if ($LASTEXITCODE -ne 0) { throw "icon generator compilation failed" }

Push-Location $root
try {
    dotnet (Join-Path $outDir "makeicon.dll")
    if ($LASTEXITCODE -ne 0) { throw "icon generation failed" }
} finally {
    Pop-Location
}

$icon = Join-Path $outDir "pixel-editor.ico"

# --target=winexe selects the windows subsystem, so no console window opens behind the form, and
# implies the Microsoft.WindowsDesktop.App framework in the emitted runtimeconfig.json. Before
# those flags existed this script had to rewrite that file after the compiler had written it.
#
# --apphost writes pixel-editor.exe beside the assembly, so the editor starts like any other
# program rather than as `dotnet pixel-editor.dll`. It also carries the windows subsystem across
# into the launcher, which is what actually keeps the console window away: starting a GUI through
# `dotnet` opens one regardless, because `dotnet` is itself a console application.
Write-Host "==> Compiling the editor..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "main.prl") `
    "--target=winexe" `
    "--apphost" `
    "--icon=$icon" `
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
Write-Host "  $(Join-Path $outDir 'pixel-editor.exe')"
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

if ($Install) {
    Write-Host ""
    & (Join-Path $root "..\..\tools\install-app.ps1") `
        -Path (Join-Path $outDir "pixel-editor.exe") `
        -Name "ProLang Pixel Editor"
}

if ($Run) {
    Write-Host ""
    Write-Host "==> Starting the editor..." -ForegroundColor Cyan
    & (Join-Path $outDir "pixel-editor.exe")
}
