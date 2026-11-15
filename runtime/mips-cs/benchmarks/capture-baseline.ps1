[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Checkout,
    [string]$OutputDirectory = '',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$checkoutRoot = [IO.Path]::GetFullPath($Checkout)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/mips-cs'))
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $artifactRoot 'safe-core-before' }
# Only replace explicitly named, direct benchmark snapshot children under our artifact root.
$relative = [IO.Path]::GetRelativePath($artifactRoot, $out)
if ($relative -notmatch '^safe-core-before(?:[-_][A-Za-z0-9_-]+)?$') { throw 'OutputDirectory must be a direct safe-core-before* child of artifacts/mips-cs.' }
$src = Join-Path $checkoutRoot 'runtime/mips-cs'
$portable = Join-Path $src 'portable/bin/Release/net10.0'
$psp = Join-Path $checkoutRoot 'artifacts/mips-cs/psp'
$requiredPortable = @('Portable.dll','Portable.deps.json','Portable.runtimeconfig.json')
$requiredPsp = @('EBOOT.PBP','program.dll','runtime.c','prolang-csharp-runtime.elf')
if (!(Test-Path -LiteralPath (Join-Path $src 'portable/Portable.csproj') -PathType Leaf)) { throw "Not a built MIPS-C# checkout: $checkoutRoot" }
foreach ($file in $requiredPortable) { if (!(Test-Path -LiteralPath (Join-Path $portable $file) -PathType Leaf)) { throw "Missing built portable file: $portable/$file" } }
foreach ($file in $requiredPsp) { if (!(Test-Path -LiteralPath (Join-Path $psp $file) -PathType Leaf)) { throw "Missing PSP build artifact: $psp/$file" } }
if (Test-Path -LiteralPath $out) {
    if (!$Force) { throw "Snapshot already exists: $out. Use -Force only to replace it." }
    $existing = Get-Item -LiteralPath $out -Force
    if (($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing to replace a reparse point.' }
    Remove-Item -LiteralPath $out -Recurse -Force
}
New-Item -ItemType Directory -Force (Join-Path $out 'source'), (Join-Path $out 'portable'), (Join-Path $out 'psp') | Out-Null
Get-ChildItem -LiteralPath $src -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
    $relativeFile = [IO.Path]::GetRelativePath($src, $_.FullName)
    $destination = Join-Path (Join-Path $out 'source') $relativeFile
    New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination
}
foreach ($file in $requiredPortable) { Copy-Item -LiteralPath (Join-Path $portable $file) -Destination (Join-Path $out 'portable') }
foreach ($file in $requiredPsp) { Copy-Item -LiteralPath (Join-Path $psp $file) -Destination (Join-Path $out 'psp') }
$manifest = @(Get-ChildItem -LiteralPath $out -Recurse -File | ForEach-Object {
    [pscustomobject]@{ path=[IO.Path]::GetRelativePath($out, $_.FullName); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'manifest.json')
Write-Host "Captured source and existing build products from $checkoutRoot into $out. Ensure they were built from that checkout and the desired guest."
