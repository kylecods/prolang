# Builds the widget-toolkit counter.
#
# Usage:
#   .\build.ps1        build
#   .\build.ps1 -Run   build, then start it
#
# Only counter.prl is handed to the compiler: `import` is transitive, so naming it pulls in
# ui/host_winforms and, through that, the whole toolkit under std/ui/.

[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$prolang = Join-Path $root "..\..\src\ProLang\ProLang.csproj"
$outDir  = Join-Path $root "bin"

# Building the compiler also builds WinFormsHelper into the compiler's lib/ directory, which is
# how `import "winforms"` resolves, and copies std/**/*.prl beside it, which is how `import
# "ui/widget"` resolves. Skipping this step after editing a std module silently uses the old copy.
Write-Host "==> Building the ProLang compiler and WinFormsHelper..." -ForegroundColor Cyan
dotnet build $prolang -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "compiler build failed" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# --target=winexe selects the windows subsystem, so no console window opens behind the form, and
# implies the Microsoft.WindowsDesktop.App framework. --apphost writes counter.exe beside the
# assembly and carries the subsystem into it, which is what actually keeps the console away:
# starting a GUI through `dotnet` opens one regardless, since `dotnet` is a console application.
Write-Host "==> Compiling the counter..." -ForegroundColor Cyan
dotnet run --project $prolang -c Release --no-build -- `
    (Join-Path $root "counter.prl") `
    "--target=winexe" `
    "--apphost" `
    "-o=$(Join-Path $outDir 'counter.dll')"
if ($LASTEXITCODE -ne 0) { throw "counter compilation failed" }

Write-Host ""
Write-Host "Built: $(Join-Path $outDir 'counter.exe')" -ForegroundColor Green

if ($Run) {
    Write-Host ""
    Write-Host "==> Starting the counter..." -ForegroundColor Cyan
    & (Join-Path $outDir "counter.exe")
}
