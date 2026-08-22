<#
.SYNOPSIS
    Installs a compiled ProLang application for the current user.

.DESCRIPTION
    Takes the directory a ProLang program was built into and installs it as a real application:
    copied somewhere permanent, given a Start Menu entry if it opens a window, and put on PATH if
    it is a command-line tool.

    It installs the whole build directory, not just the assembly. A compiled program is an
    assembly plus its .runtimeconfig.json, ProLang.Runtime.dll, and any interop assemblies the
    compiler deployed beside it — WinFormsHelper.dll for anything that imports "winforms". Moving
    the assembly on its own produces a program that fails to start.

    Nothing outside the install directory, the Start Menu and the user's PATH is touched, and no
    administrator rights are needed.

.PARAMETER Path
    The built .exe, or the .dll if the program was compiled without --apphost.

.PARAMETER Name
    Display and directory name. Defaults to the file's own name.

.PARAMETER Prefix
    Where applications are installed. Defaults to %LOCALAPPDATA%\Programs.

.PARAMETER Shortcut
    Add a Start Menu entry. The default for a program built with --target=winexe.

.PARAMETER AddToPath
    Put the application's directory on PATH, so it can be launched by name from a shell.

.PARAMETER Uninstall
    Remove a previous installation, its shortcut and its PATH entry.

.EXAMPLE
    .\tools\install-app.ps1 -Path examples\16-pixel-editor\bin\pixel-editor.exe -Name "ProLang Pixel Editor"

.EXAMPLE
    .\tools\install-app.ps1 -Path bin\my-tool.exe -AddToPath

.EXAMPLE
    .\tools\install-app.ps1 -Name "ProLang Pixel Editor" -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Path,
    [string]$Name,
    [string]$Prefix = (Join-Path $env:LOCALAPPDATA 'Programs'),
    [switch]$Shortcut,
    [switch]$AddToPath,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

if (-not $Name -and -not $Path) {
    throw 'give -Path (to install) or -Name (to uninstall)'
}

if (-not $Name) {
    $Name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
}

# .NET resolves a relative path against the *process* working directory, which PowerShell does not
# keep in step with Get-Location — so `-Path bin\app.exe` from a subdirectory would be looked for
# under wherever the shell started. Every path here goes through this instead. It works for paths
# that do not exist yet, which Resolve-Path does not.
function Resolve-FullPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).ProviderPath $path))
}

$target    = Join-Path (Resolve-FullPath $Prefix) $Name
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$linkPath  = Join-Path $startMenu "$Name.lnk"

function Get-UserPathEntries {
    $value = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not $value) { return @() }
    return @($value -split ';' | Where-Object { $_ -ne '' })
}

function Test-SamePath([string]$a, [string]$b) {
    try   { return (Resolve-FullPath $a).TrimEnd('\') -ieq (Resolve-FullPath $b).TrimEnd('\') }
    catch { return $false }
}

# ── Uninstall ────────────────────────────────────────────────────────────────

if ($Uninstall) {
    if (Test-Path $target) {
        Write-Host "==> Removing $target" -ForegroundColor Cyan
        Remove-Item $target -Recurse -Force
    } else {
        Write-Host "Nothing installed at $target" -ForegroundColor Yellow
    }

    if (Test-Path $linkPath) {
        Write-Host "==> Removing the Start Menu entry" -ForegroundColor Cyan
        Remove-Item $linkPath -Force
    }

    $entries = Get-UserPathEntries
    $kept    = @($entries | Where-Object { -not (Test-SamePath $_ $target) })

    if ($kept.Count -ne $entries.Count) {
        Write-Host "==> Removing it from your PATH" -ForegroundColor Cyan
        [Environment]::SetEnvironmentVariable('Path', ($kept -join ';'), 'User')
    }

    Write-Host ""
    Write-Host "$Name uninstalled." -ForegroundColor Green
    return
}

# ── Install ──────────────────────────────────────────────────────────────────

if (-not (Test-Path $Path)) { throw "no such file: $Path" }

$full      = Resolve-FullPath $Path
$source    = Split-Path -Parent $full
$assembly  = [System.IO.Path]::ChangeExtension($full, '.dll')

if (-not (Test-Path $assembly)) {
    throw "expected $assembly beside it; -Path should name a program the prolang compiler built"
}

# A missing runtimeconfig is the one failure that produces no useful error at run time: the host
# reports that it cannot find a framework, without saying which file is absent.
$config = [System.IO.Path]::ChangeExtension($assembly, '.runtimeconfig.json')
if (-not (Test-Path $config)) {
    throw "$config is missing; the program was not emitted by the prolang compiler, or the build is incomplete"
}

# Whether it opens a window is read off the launcher rather than asked for. The compiler already
# recorded it in the PE subsystem field when --apphost ran, and a flag here could disagree with it.
$isGui = $false
$launcher = [System.IO.Path]::ChangeExtension($assembly, '.exe')

if (Test-Path $launcher) {
    $bytes = [System.IO.File]::ReadAllBytes($launcher)
    $pe    = [BitConverter]::ToInt32($bytes, 0x3C)
    $isGui = [BitConverter]::ToUInt16($bytes, $pe + 24 + 68) -eq 2
} else {
    $launcher = $null
}

Write-Host "==> Installing $Name to $target" -ForegroundColor Cyan

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null

Copy-Item (Join-Path $source '*') -Destination $target -Recurse -Force

$installedLauncher = if ($launcher) { Join-Path $target (Split-Path -Leaf $launcher) } else { $null }

# ── Start Menu ───────────────────────────────────────────────────────────────

if ($Shortcut -or ($isGui -and -not $AddToPath)) {
    if (-not $installedLauncher) {
        Write-Host "    (no shortcut: build with --apphost so there is an .exe to point at)" -ForegroundColor Yellow
    } else {
        Write-Host "==> Adding a Start Menu entry" -ForegroundColor Cyan

        $shell = New-Object -ComObject WScript.Shell
        $link  = $shell.CreateShortcut($linkPath)
        $link.TargetPath       = $installedLauncher
        $link.WorkingDirectory = $target
        $link.Description      = "$Name (built with ProLang)"
        $link.Save()
    }
}

# ── PATH ─────────────────────────────────────────────────────────────────────

if ($AddToPath) {
    $entries = Get-UserPathEntries

    if ($entries | Where-Object { Test-SamePath $_ $target }) {
        Write-Host "==> Already on your PATH" -ForegroundColor Cyan
    } else {
        Write-Host "==> Adding it to your PATH" -ForegroundColor Cyan
        [Environment]::SetEnvironmentVariable('Path', (($entries + $target) -join ';'), 'User')
    }
}

# ── Report ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "$Name installed." -ForegroundColor Green
Write-Host "  location $target"
Write-Host "  kind     $(if ($isGui) { 'windowed application' } else { 'console application' })"

if ($installedLauncher) {
    Write-Host "  run      $installedLauncher"
} else {
    Write-Host "  run      dotnet `"$(Join-Path $target (Split-Path -Leaf $assembly))`""
    Write-Host ""
    Write-Host "Compile with --apphost to get a launcher that runs without 'dotnet'." -ForegroundColor DarkGray
}
