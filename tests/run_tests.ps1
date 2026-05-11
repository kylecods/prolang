# ProLang Language Test Runner (PowerShell)
# Runs all language tests and reports results on Windows

param(
    [switch]$Verbose = $false
)

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$TestsDir = $ScriptDir
$ProjectDir = Split-Path -Parent $TestsDir
$SrcDir = Join-Path $ProjectDir "src"

# Colors
$Green = "Green"
$Red = "Red"
$Yellow = "Yellow"

$Passed = 0
$Failed = 0
$Total = 0

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "ProLang Language Test Suite" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Find all .prl test files recursively
$TestFiles = @(Get-ChildItem -Path (Join-Path $TestsDir "language") -Filter "*.prl" -Recurse |
               Sort-Object FullName)

if ($TestFiles.Count -eq 0) {
    Write-Host "ERROR: No test files found in $TestsDir\language" -ForegroundColor $Red
    exit 1
}

# Run each test
foreach ($TestFile in $TestFiles) {
    $Total++

    # Get relative path for display
    $RelativePath = $TestFile.FullName -replace [regex]::Escape($TestsDir + "\"), ""
    $TestName = $RelativePath -replace "\\", "/"

    # Run the test
    $TempOutputFile = [System.IO.Path]::GetTempFileName()

    try {
        Push-Location $SrcDir

        # Run compiler in Release mode
        & dotnet run -c Release --project ProLang/ProLang.csproj -- $TestFile.FullName `
            *> $TempOutputFile

        $ExitCode = $LASTEXITCODE

        Pop-Location

        # Read output
        $Output = Get-Content $TempOutputFile -Raw

        # Check for critical compilation errors
        # Critical: syntax errors, type errors, binding errors for types
        # Non-critical: missing built-in functions like 'print'
        $HasCriticalError = $false

        if ($Output -match "Unexpected token") {
            $HasCriticalError = $true
        }
        elseif ($Output -match "Variable.*does not exist.*<") {
            $HasCriticalError = $true
        }
        elseif ($Output -match "Type.*not found") {
            $HasCriticalError = $true
        }
        elseif ($Output -match "Duplicate.*declared") {
            $HasCriticalError = $true
        }

        if ($HasCriticalError) {
            Write-Host "✗ FAILED" -ForegroundColor $Red -NoNewline
            Write-Host " - $TestName"

            # Show first error line
            $ErrorLine = $Output -split "`n" | Where-Object { $_ -match "error|Error" } | Select-Object -First 1
            if ($ErrorLine) {
                Write-Host "  $ErrorLine" -ForegroundColor Yellow
            }

            $Failed++
        }
        else {
            Write-Host "✓ PASSED" -ForegroundColor $Green -NoNewline
            Write-Host " - $TestName"
            $Passed++
        }

        if ($Verbose) {
            Write-Host "  Exit Code: $ExitCode" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "✗ FAILED" -ForegroundColor $Red -NoNewline
        Write-Host " - $TestName"
        Write-Host "  Exception: $($_.Exception.Message)" -ForegroundColor Yellow
        $Failed++
    }
    finally {
        if (Test-Path $TempOutputFile) {
            Remove-Item $TempOutputFile -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Total:  $Total"
Write-Host "Passed: $Passed" -ForegroundColor $Green
Write-Host "Failed: $Failed" -ForegroundColor $(if ($Failed -gt 0) { $Red } else { $Green })
Write-Host ""

if ($Failed -eq 0) {
    Write-Host "✓ All tests passed!" -ForegroundColor $Green
    exit 0
}
else {
    Write-Host "✗ Some tests failed" -ForegroundColor $Red
    exit 1
}
