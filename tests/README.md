# ProLang test programs

`.prl` programs used as the compiler's test corpus. They are inputs, not a test runner — the
assertions live in [`src/ProLang.Tests`](../src/ProLang.Tests).

## How these are used

Every `.prl` file in this directory and in [`examples/`](../examples) is classified in
[`TestCorpus.cs`](../src/ProLang.Tests/Infrastructure/TestCorpus.cs) as one of:

| Classification | What is asserted |
|---|---|
| `Runnable` | Compiles, and running it produces the expected stdout |
| `CompileOnly` | Compiles and its IL is snapshotted, but it is not run (library, interactive, or needs a runtime pack) |
| `KnownBroken` | Does not compile today, and is asserted to still fail so the breakage stays visible |

`CorpusIntegrityTests` fails if a `.prl` file exists without being classified, so a new test
program cannot silently escape coverage.

```bash
dotnet test src/ProLang.Tests/ProLang.Tests.csproj -c Release
```

## Adding a test program

1. Add the `.prl` file here.
2. Classify it in `TestCorpus.cs`. Anything not `Runnable` needs a stated reason.
3. Capture its baselines:
   ```bash
   PROLANG_UPDATE_SNAPSHOTS=1 dotnet test src/ProLang.Tests/ProLang.Tests.csproj -c Release
   ```
4. Review the generated files, then re-run normally to confirm they assert.

## Layout

```
tests/
├── cast-*.prl                 Cast expression behaviour, including negative cases
├── print-types.prl            Printing each built-in type
└── language/
    ├── arrays/                Array literals and indexing
    ├── compiler/              Compiler-specific regressions
    ├── datatypes/             Built-in type coverage
    ├── entry-point/           main() forms, including negative cases
    ├── generics/              Generic structs and functions
    ├── lexer/                 Tokenization edge cases
    ├── parser/                Parser regressions
    └── string-methods/        String builtins
```

## Known-broken programs

Several programs here predate the rule that all statements must sit inside an explicit `main()`,
and no longer compile. They are tracked as `KnownBroken` in `TestCorpus.cs` with the reason
recorded on each entry, rather than deleted, so that the coverage gap is visible.

Two are genuine compiler bugs rather than corpus rot:

- `language/parser/test-parser-simple.prl` and `test-parse-result.prl` crash the binder with a
  `NullReferenceException` in `BindReturnStatement` on a top-level `return`.
- `language/entry-point/main-with-args.prl` cannot resolve `length(args)` on `array<string>`.

If one of these starts compiling, `CorpusIntegrityTests` fails to tell you — promote it out of
`KnownBroken` and capture its baseline.

## The old shell runners

`run_tests.sh` and `run_tests.ps1` predate the xUnit suite. They compile each program and grep the
compiler's stdout for four error patterns; they never run the compiled output, so a program that
crashed at runtime was reported as passing, as was any program that failed to compile in a way
that did not match one of the four patterns. Use `dotnet test` instead.
