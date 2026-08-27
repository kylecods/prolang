<#
.SYNOPSIS
    Installs the ProLang compiler as a command-line application for the current user.

.DESCRIPTION
    Publishes the compiler and everything it needs at run time into a directory of its own, then
    puts that directory on the user's PATH so `prolang` works from any shell.

    Three things travel with the compiler and it does not work without them, which is why this
    installs a directory rather than a single file:

      std\      the prolang standard library, resolved by name from beside the executable
      runtime\  ProLang.Runtime.dll, copied next to every program the compiler emits
      lib\      WinFormsHelper.dll, which is what `import "winforms"` resolves to

    Nothing is written outside the install directory and the user's PATH. No administrator rights
    are needed and no machine-wide state is touched, so this cannot disturb another user or a
    system .NET installation.

.PARAMETER Prefix
    Where to install. Defaults to %LOCALAPPDATA%\Programs\ProLang.

.PARAMETER NoPath
    Install without touching PATH. Use it when you would rather add the directory yourself.

.PARAMETER Uninstall
    Remove a previous installation and its PATH entry.

.EXAMPLE
    .\install.ps1

.EXAMPLE
    .\install.ps1 -Prefix D:\tools\prolang

.EXAMPLE
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Prefix = (Join-Path $env:LOCALAPPDATA 'Programs\ProLang'),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$NoPath,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\ProLang\ProLang.csproj'
$outDir  = Join-Path $root "src\ProLang\bin\$Configuration\net10.0"

# .NET resolves a relative path against the *process* working directory, which PowerShell does not
# keep in step with Get-Location, so a relative -Prefix would land somewhere unexpected. Resolving
# it once here also means PATH entries are compared and removed by their canonical form; a trailing
# slash or a relative segment would otherwise leave a duplicate behind on every reinstall.
function Resolve-FullPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).ProviderPath $path))
}

$Prefix = Resolve-FullPath $Prefix

function Get-UserPathEntries {
    $value = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not $value) { return @() }
    return @($value -split ';' | Where-Object { $_ -ne '' })
}

function Set-UserPathEntries([string[]]$Entries) {
    [Environment]::SetEnvironmentVariable('Path', ($Entries -join ';'), 'User')
}

function Test-SamePath([string]$a, [string]$b) {
    try   { return (Resolve-FullPath $a).TrimEnd('\') -ieq (Resolve-FullPath $b).TrimEnd('\') }
    catch { return $false }
}

# ── Uninstall ────────────────────────────────────────────────────────────────

if ($Uninstall) {
    if (Test-Path $Prefix) {
        Write-Host "==> Removing $Prefix" -ForegroundColor Cyan
        Remove-Item $Prefix -Recurse -Force
    } else {
        Write-Host "Nothing installed at $Prefix" -ForegroundColor Yellow
    }

    $kept = @(Get-UserPathEntries | Where-Object { -not (Test-SamePath $_ $Prefix) })

    if ($kept.Count -ne (Get-UserPathEntries).Count) {
        Write-Host "==> Removing it from your PATH" -ForegroundColor Cyan
        Set-UserPathEntries $kept
    }

    Write-Host ""
    Write-Host "ProLang uninstalled. Open a new terminal for the PATH change to take effect." -ForegroundColor Green
    return
}

# ── Install ──────────────────────────────────────────────────────────────────

Write-Host "==> Building the compiler, the runtime and WinFormsHelper..." -ForegroundColor Cyan
dotnet build $project -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

Write-Host "==> Publishing to $Prefix" -ForegroundColor Cyan

if (Test-Path $Prefix) { Remove-Item $Prefix -Recurse -Force }

# Native AOT publish: a single self-contained ProLang.exe with no .NET runtime dependency. The
# compiler is AOT-compatible because interop discovery reads assemblies as metadata only and the
# emitter writes IL with Mono.Cecil. The RID is fixed to win-x64; add more RIDs here to ship them.
#
# No --no-build here: the AOT native compile is a publish-time step, so skipping the build would
# silently produce a framework-dependent layout instead of the self-contained executable.
dotnet publish $project -c $Configuration --nologo -v q -r win-x64 -p:PublishAot=true -o $Prefix
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# runtime\ and lib\ are produced by AfterTargets steps in ProLang.csproj that copy into the build
# output. They are not project items, so `dotnet publish` does not know about them and they have
# to be brought across by hand. Without runtime\, every program the compiler emits fails to start.
foreach ($extra in 'runtime', 'lib') {
    $source = Join-Path $outDir $extra

    if (Test-Path $source) {
        Copy-Item $source -Destination $Prefix -Recurse -Force
    } elseif ($extra -eq 'runtime') {
        throw "expected $source to exist after the build; the compiler cannot emit runnable programs without it"
    } else {
        Write-Host "    (no lib\ — WinFormsHelper is Windows-only)" -ForegroundColor DarkGray
    }
}

# ── PATH ─────────────────────────────────────────────────────────────────────

if (-not $NoPath) {
    $entries = Get-UserPathEntries

    if ($entries | Where-Object { Test-SamePath $_ $Prefix }) {
        Write-Host "==> Already on your PATH" -ForegroundColor Cyan
    } else {
        Write-Host "==> Adding it to your PATH" -ForegroundColor Cyan
        Set-UserPathEntries ($entries + $Prefix)
    }

    # The process copy is set separately: the change above lands in the registry and reaches new
    # processes only, so without this the `prolang` below would not be found in this session.
    if (-not ($env:Path -split ';' | Where-Object { Test-SamePath $_ $Prefix })) {
        $env:Path = "$env:Path;$Prefix"
    }
}

# ── Verify ───────────────────────────────────────────────────────────────────

$exe = Join-Path $Prefix 'ProLang.exe'

if (-not (Test-Path $exe)) { throw "publish did not produce $exe" }

# Both install routes put a `prolang` on PATH, and which one answers depends on the order of the
# entries — usually the tool, since .dotnet\tools tends to come first. Two copies is not an error,
# but silently compiling with the older of them is a long afternoon.
$tool = Join-Path $env:USERPROFILE '.dotnet\tools\prolang.exe'

if (Test-Path $tool) {
    Write-Host ""
    Write-Host "Note: a ProLang global tool is also installed, at" -ForegroundColor Yellow
    Write-Host "  $tool" -ForegroundColor Yellow
    Write-Host "Whichever comes first on PATH is the one 'prolang' runs. Check with" -ForegroundColor Yellow
    Write-Host "  (Get-Command prolang).Source" -ForegroundColor Yellow
    Write-Host "and remove the one you do not want with 'dotnet tool uninstall --global ProLang.Compiler'." -ForegroundColor Yellow
}

$modules = @(Get-ChildItem (Join-Path $Prefix 'std') -Recurse -Filter *.prl -ErrorAction SilentlyContinue).Count

Write-Host ""
Write-Host "ProLang installed." -ForegroundColor Green
Write-Host "  location        $Prefix"
Write-Host "  command         prolang"
Write-Host "  library modules $modules under std\"
Write-Host ""
Write-Host "Try it:" -ForegroundColor Cyan
Write-Host '  prolang --help'
Write-Host '  prolang hello.prl --target=console --apphost -o hello.dll'
Write-Host '  .\hello.exe'

if (-not $NoPath) {
    Write-Host ""
    Write-Host "Open a new terminal for the PATH change to apply to other shells." -ForegroundColor DarkGray
}
