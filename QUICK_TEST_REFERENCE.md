# Quick Test Reference Card

## One-Liner Commands

### Linux/macOS/WSL
```bash
bash tests/run_tests.sh
```

### Windows PowerShell
```powershell
powershell -ExecutionPolicy Bypass -File tests\run_tests.ps1
```

## Before Every Commit
✅ Run tests and verify all pass
✅ Check output shows `0 Failed`
✅ Look for `✓ All tests passed!`

## Test Status
```
Total:  3
Passed: 3 ✓
Failed: 0
```

## Run Single Test

**Linux/macOS/WSL:**
```bash
cd src && dotnet run -c Release --project ProLang/ProLang.csproj -- ../tests/language/generics/single_parameter.pl
```

**Windows:**
```powershell
cd src
dotnet run -c Release --project ProLang\ProLang.csproj -- ..\tests\language\generics\single_parameter.pl
```

## Add New Test
1. Create `.pl` file in `tests/language/feature/`
2. Run test suite
3. Verify new test shows `✓ PASSED`

Example location: `tests/language/generics/my_test.pl`

## If Test Fails

1. Run failing test manually (see "Run Single Test" above)
2. Check error message for actual problem
3. Fix code or update test as needed
4. Run full test suite to verify fix

## File Locations

```
tests/language/
├── generics/        (Generic struct tests)
├── arrays/          (Array syntax tests)
└── structs/         (Struct tests - future)

Documentation:
├── TESTING.md                   (Full guide)
├── CROSS_PLATFORM_TESTING.md    (Setup guide)
├── TEST_CHECKLIST.md            (Quick checklist)
└── TESTING_INFRASTRUCTURE.md    (Architecture)
```

## What Tests Check

✅ **Syntax errors** - No "Unexpected token"
✅ **Type binding** - Generic types resolve correctly
✅ **No critical errors** - Compilation succeeds structurally

❌ **Failure indicators** - Unexpected tokens, type errors, binding errors

**Note**: Missing print() function ≠ test failure

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "PowerShell execution policy" | Use: `powershell -ExecutionPolicy Bypass -File tests\run_tests.ps1` |
| "dotnet not found" | Install .NET SDK 8.0+ |
| "Test file not found" | Check path with: `ls tests/language/` (or `dir tests\language\` on Windows) |
| Test still failing | Run manually to see actual error message |

## Development Workflow

```
1. Make code changes
2. Run: bash tests/run_tests.sh (or PowerShell on Windows)
3. Check: All tests show ✓ PASSED
4. Commit: Safe to commit if tests pass
```

## Quick Facts

- 3 tests total
- All 3 currently passing ✅
- Tests: generics (2), arrays (1)
- Both bash and PowerShell runners supported
- Exit code: 0 = pass, 1 = fail
- ~15s runtime typical

## Documentation

Need help? Read:
- **Quick help** → `TEST_CHECKLIST.md`
- **How to run** → `CROSS_PLATFORM_TESTING.md`
- **Full details** → `TESTING.md`
- **Architecture** → `TESTING_INFRASTRUCTURE.md`

---

**TL;DR**: Run `bash tests/run_tests.sh` (or PowerShell on Windows), verify all show ✓ PASSED before committing.
