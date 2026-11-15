# Desktop regression harness for the portable MIPS-C# runtime (runtime/mips-cs/portable).
#
#   golden.ps1 -Record   compile the guests and record the expected outputs
#   golden.ps1 [-Check]  re-run everything and compare it with the recording (the default)
#
# Per guest (the cube plus the twelve fixtures) it records two things:
#   <name>.run.txt   the desktop run: Portable.dll <guest> [600 for the cube]. For the cube that is
#                    every FRAME line, geometry=, instructions= and calls= included; a fixed 17 ms
#                    clock makes them deterministic.
#   <name>.jit.txt   Portable.dll --jit-golden <guest>: one METHOD line per method plus a TOTAL
#                    over the words the JIT emitted (see portable/JitGoldenTests.cs).
# Each file holds stdout, stderr and the exit code in labelled sections, so the order in which two
# pipes happen to be drained can never make a recording flaky. A guest that fails to load or run is
# recorded as such; that is expected output too.
#
# Guests live in artifacts/mips-cs/golden/guests, recordings in .../expected, and a check writes
# .../actual. Output is compared exactly after CRLF -> LF. The exit status is non-zero on any
# difference, any missing file, any process that outlives -TimeoutSeconds (it is killed, and its item
# fails; a recording never accepts one), and any recording in .../expected that no current guest
# produces (an orphan, left behind by a guest that was renamed or removed).
param(
    [switch]$Record,
    [switch]$Check,
    [switch]$SkipBuild,
    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
if ($Record -and $Check) { throw 'Pass -Record or -Check, not both.' }
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$golden = "$repo/artifacts/mips-cs/golden"
$guestDir = "$golden/guests"
$expectedDir = "$golden/expected"
$actualDir = "$golden/actual"
$compiler = "$repo/src/ProLang/bin/Release/net10.0/ProLang.dll"
$portable = "$repo/runtime/mips-cs/portable/bin/Release/net10.0/Portable.dll"
$cubeFrames = '600'

$guests = @(
    [pscustomobject]@{ Name = 'cube'; Source = "$repo/examples/18-psp-cube/psp_cube.prl"; Args = @($cubeFrames) }
)
foreach ($case in 'smoke', 'arithmetic', 'boxed', 'structs', 'psp_api', 'assert_fail', 'unsupported', 'div_zero', 'call_depth') {
    $guests += [pscustomobject]@{ Name = $case; Source = "$repo/runtime/psp-v/fixtures/$case.prl"; Args = @() }
}
foreach ($case in 'array_bounds', 'gc_roots', 'cache_effects') {
    $guests += [pscustomobject]@{ Name = $case; Source = "$PSScriptRoot/fixtures/$case.prl"; Args = @() }
}

# Runs `dotnet <arguments>` and returns an object with Text, stdout, stderr and the exit code as one
# labelled text, and TimedOut, true when the process had not exited after $TimeoutSeconds. A process
# that times out is killed together with its children, and Text then holds whatever it had written.
function Invoke-Captured([string[]]$ProcessArgs) {
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($argument in $ProcessArgs) { $info.ArgumentList.Add($argument) }
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $info.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = !$process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        try { $process.Kill($true) } catch { }
        $null = $process.WaitForExit(5000)
    }
    $out = if ($stdout.Wait(5000)) { $stdout.Result } else { '' }
    $err = if ($stderr.Wait(5000)) { $stderr.Result } else { '' }
    $exit = if ($timedOut) { "timed out after $TimeoutSeconds s" } else { $process.ExitCode }
    $text = "== stdout ==`n$out`n== stderr ==`n$err`n== exit ==`n$exit`n"
    $process.Dispose()
    return [pscustomobject]@{ Text = $text.Replace("`r`n", "`n"); TimedOut = $timedOut }
}

function Get-Short([string]$Line) { if ($Line.Length -gt 200) { $Line.Substring(0, 200) + '...' } else { $Line } }

function Find-FirstDifference([string]$Expected, [string]$Actual) {
    $expectedLines = $Expected -split "`n"
    $actualLines = $Actual -split "`n"
    $count = [Math]::Max($expectedLines.Count, $actualLines.Count)
    for ($i = 0; $i -lt $count; $i++) {
        $e = if ($i -lt $expectedLines.Count) { $expectedLines[$i] } else { '<end of output>' }
        $a = if ($i -lt $actualLines.Count) { $actualLines[$i] } else { '<end of output>' }
        if ($e -cne $a) { return "line $($i + 1): expected [$(Get-Short $e)] actual [$(Get-Short $a)]" }
    }
    return 'texts differ'
}

Push-Location $repo
try {
    if (!$SkipBuild) {
        foreach ($project in 'src/ProLang/ProLang.csproj', 'runtime/mips-cs/portable/Portable.csproj') {
            & dotnet build $project -c Release --nologo -v quiet
            if ($LASTEXITCODE) { throw "Build failed: $project" }
        }
    }
    foreach ($file in $compiler, $portable) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing build output: $file (build without -SkipBuild)" }
    }

    # Guests: compiled once on -Record, reused (and required) otherwise.
    if ($Record) {
        if (Test-Path -LiteralPath $guestDir) { Remove-Item -LiteralPath $guestDir -Recurse -Force }
        New-Item -ItemType Directory -Force $guestDir | Out-Null
        foreach ($guest in $guests) {
            & dotnet $compiler $guest.Source -o "$guestDir/$($guest.Name).dll"
            if ($LASTEXITCODE) { throw "Guest compilation failed: $($guest.Name)" }
        }
    } else {
        $missing = @($guests | Where-Object { !(Test-Path -LiteralPath "$guestDir/$($_.Name).dll" -PathType Leaf) })
        if ($missing.Count) { throw "Recorded guests missing ($($missing.Name -join ', ')); run golden.ps1 -Record first." }
    }

    $outDir = if ($Record) { $expectedDir } else { $actualDir }
    if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force $outDir | Out-Null

    # The guests themselves are part of the recording: a changed guest makes every comparison moot.
    $hashes = ($guests | ForEach-Object { "$($_.Name) $((Get-FileHash -LiteralPath "$guestDir/$($_.Name).dll" -Algorithm SHA256).Hash)" }) -join "`n"
    $items = [System.Collections.Generic.List[object]]::new()
    $items.Add([pscustomobject]@{ Name = 'guests.sha256'; Text = $hashes + "`n"; TimedOut = $false })
    foreach ($guest in $guests) {
        $dll = "$guestDir/$($guest.Name).dll"
        $run = Invoke-Captured (@($portable, $dll) + $guest.Args)
        $items.Add([pscustomobject]@{ Name = "$($guest.Name).run"; Text = $run.Text; TimedOut = $run.TimedOut })
        $jit = Invoke-Captured @($portable, '--jit-golden', $dll)
        $items.Add([pscustomobject]@{ Name = "$($guest.Name).jit"; Text = $jit.Text; TimedOut = $jit.TimedOut })
    }

    $failures = 0
    foreach ($item in $items) {
        $path = "$outDir/$($item.Name).txt"
        [IO.File]::WriteAllText($path, $item.Text, [Text.UTF8Encoding]::new($false))
        if ($item.TimedOut) {
            if ($Record) { throw "Cannot record $($item.Name): the process did not finish within $TimeoutSeconds s and was killed." }
            Write-Host "FAIL   $($item.Name): the process did not finish within $TimeoutSeconds s and was killed"
            $failures++
            continue
        }
        if ($Record) {
            $lines = ($item.Text -split "`n").Count - 1
            Write-Host "RECORD $($item.Name) ($lines lines)"
            continue
        }
        $expectedPath = "$expectedDir/$($item.Name).txt"
        if (!(Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
            Write-Host "FAIL   $($item.Name): no recording at $expectedPath"
            $failures++
            continue
        }
        $expected = [IO.File]::ReadAllText($expectedPath).Replace("`r`n", "`n")
        if ($expected -ceq $item.Text) {
            Write-Host "PASS   $($item.Name)"
        } else {
            Write-Host "FAIL   $($item.Name): $(Find-FirstDifference $expected $item.Text)"
            $failures++
        }
    }

    if ($Record) {
        Write-Host "Recorded $($items.Count) items for $($guests.Count) guests into $expectedDir"
        exit 0
    }

    # A recording that no current guest produces is not checked by anything above, so it would rot
    # unnoticed: flag it.
    $produced = @($items | ForEach-Object { "$($_.Name).txt" })
    $orphans = @(Get-ChildItem -LiteralPath $expectedDir -Filter '*.txt' -File -ErrorAction SilentlyContinue | Where-Object { $produced -cnotcontains $_.Name })
    foreach ($orphan in $orphans) {
        Write-Host "FAIL   orphan recording: $($orphan.FullName) matches no current guest (delete it, or run golden.ps1 -Record)"
        $failures++
    }

    if ($failures) {
        Write-Host "golden: $failures failure(s) over $($items.Count) items, with $($orphans.Count) orphan recordings (actual output in $actualDir)"
        exit 1
    }
    Write-Host "golden: all $($items.Count) items match, no orphan recordings"
    exit 0
} finally { Pop-Location }
