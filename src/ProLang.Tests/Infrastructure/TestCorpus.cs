namespace ProLang.Tests.Infrastructure;

/// <summary>
/// How a corpus program is expected to behave, which decides what may be asserted about it.
/// </summary>
internal enum CorpusKind
{
    /// <summary>Compiles and runs to completion deterministically. Gets both an IL snapshot and an output assertion.</summary>
    Runnable,

    /// <summary>Compiles, but cannot be executed here (library, interactive, or needs a runtime pack we do not ship). IL snapshot only.</summary>
    CompileOnly,

    /// <summary>Does not compile today. Asserted to still fail, so the breakage stays visible instead of silently passing.</summary>
    KnownBroken,
}

/// <summary>One ProLang program under test.</summary>
/// <param name="RelativePath">Path relative to the repository root, using forward slashes.</param>
/// <param name="Kind">What may be asserted about it.</param>
/// <param name="Note">Why it is classified this way. Required for anything not <see cref="CorpusKind.Runnable"/>.</param>
internal sealed record CorpusEntry(string RelativePath, CorpusKind Kind, string? Note = null)
{
    /// <summary>Stable identifier used for snapshot and expected-output file names.</summary>
    public string Name => RelativePath.Replace('/', '.').Replace(".prl", string.Empty);

    /// <summary>Absolute path to the source file.</summary>
    public string FullPath => Path.Combine(TestPaths.RepoRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));

    public override string ToString() => RelativePath;
}

/// <summary>
/// The curated set of ProLang programs the emitter is tested against.
/// </summary>
/// <remarks>
/// <para>
/// The list is explicit rather than discovered by globbing so that adding a <c>.prl</c> file
/// anywhere in the repository does not silently change what is covered.
/// <see cref="CoverageTests"/> asserts the list stays in sync with what is on disk.
/// </para>
/// <para>
/// Classifications were established by compiling and running every <c>.prl</c> file in the
/// repository against the emitter as it stood before the code generation refactor began. The
/// <see cref="CorpusKind.KnownBroken"/> entries are pre-existing failures, not regressions —
/// each one carries the reason it fails.
/// </para>
/// </remarks>
internal static class TestCorpus
{
    /// <summary>Every program under test, in every category.</summary>
    public static IReadOnlyList<CorpusEntry> All { get; } =
    [
        // ── Runs to completion; both IL and stdout are asserted ──────────────────────────
        new("examples/01-syntax-types/01_syntax_types.prl", CorpusKind.Runnable),
        new("examples/02-operators/02_operators.prl", CorpusKind.Runnable),
        new("examples/03-control-flow/03_control_flow.prl", CorpusKind.Runnable),
        new("examples/04-functions/04_functions.prl", CorpusKind.Runnable),
        new("examples/06-structs/structs.prl", CorpusKind.Runnable),
        new("examples/07-strings/strings.prl", CorpusKind.Runnable),
        new("examples/08-ring-buffer/ring-buffer.prl", CorpusKind.Runnable),
        new("examples/09-json-parser/json-parser-tests.prl", CorpusKind.Runnable),
        new("examples/11-std/dynarray_demo.prl", CorpusKind.Runnable),
        new("examples/16-pixel-editor/tests/run_tests.prl", CorpusKind.Runnable),
        new("tests/cast-comprehensive.prl", CorpusKind.Runnable),
        new("tests/cast-expression.prl", CorpusKind.Runnable),
        new("tests/cast-simple.prl", CorpusKind.Runnable),
        new("tests/cast-string.prl", CorpusKind.Runnable),
        new("tests/print-types.prl", CorpusKind.Runnable),
        new("tests/language/arrays/array_syntax.prl", CorpusKind.Runnable),
        new("tests/language/datatypes/types.prl", CorpusKind.Runnable),
        new("tests/language/structs/array-of-structs.prl", CorpusKind.Runnable),
        new("tests/language/structs/nested-field-assignment.prl", CorpusKind.Runnable),
        new("tests/language/entry-point/basic-main.prl", CorpusKind.Runnable),
        new("tests/language/entry-point/multiple-prints.prl", CorpusKind.Runnable),
        new("tests/language/entry-point/nested-calls.prl", CorpusKind.Runnable),
        new("tests/language/entry-point/print-expressions.prl", CorpusKind.Runnable),

        // ── Compiles; IL is asserted but the program is not executed ─────────────────────
        new("examples/09-json-parser/json-parser.prl", CorpusKind.CompileOnly,
            "Library — defines the parser but has no main()."),
        new("examples/10-lox/lox.prl", CorpusKind.CompileOnly,
            "Reads from stdin; not deterministic under a test runner."),
        new("examples/12-snake/snake.prl", CorpusKind.CompileOnly,
            "Interactive game loop; never terminates on its own."),
        new("examples/14-chip-8/chip-8.prl", CorpusKind.CompileOnly,
            "Interactive emulator loop; never terminates on its own."),
        new("examples/13-winforms/01_hello_world.prl", CorpusKind.CompileOnly,
            "Needs the Windows Desktop runtime pack, which the emitted runtimeconfig does not request."),
        new("examples/13-winforms/02_message_box.prl", CorpusKind.CompileOnly, "See 01_hello_world."),
        new("examples/13-winforms/03_input_dialog.prl", CorpusKind.CompileOnly, "See 01_hello_world."),
        new("examples/13-winforms/04_counter.prl", CorpusKind.CompileOnly, "See 01_hello_world."),
        new("examples/13-winforms/05_color_demo.prl", CorpusKind.CompileOnly, "See 01_hello_world."),
        new("examples/13-winforms/06_native_enum.prl", CorpusKind.CompileOnly, "See 01_hello_world."),
        new("examples/13-winforms/07_paint_demo.prl", CorpusKind.CompileOnly, "See 01_hello_world."),

        // ── Pixel editor ─────────────────────────────────────────────────────────────────
        // The modules the editor still owns. Everything general moved to std/, which this corpus
        // does not scan — those are covered by tests/std/run_tests.prl instead, which compiles the
        // whole library graph and asserts on it.
        new("examples/16-pixel-editor/tools.prl", CorpusKind.CompileOnly,
            "Library — pixel editor module; exercised by tests/run_tests.prl."),
        new("examples/16-pixel-editor/appicon.prl", CorpusKind.CompileOnly,
            "Library — draws the program's own icon; exercised by tests/run_tests.prl."),
        new("examples/16-pixel-editor/render.prl", CorpusKind.CompileOnly,
            "Library — hands model state to WinFormsHelper; no main() and no runnable behaviour."),
        new("examples/16-pixel-editor/app.prl", CorpusKind.CompileOnly,
            "Library — window construction and the event loop; no main()."),
        new("examples/16-pixel-editor/main.prl", CorpusKind.CompileOnly,
            "Interactive GUI event loop; needs the Windows Desktop runtime pack, which the "
            + "emitted runtimeconfig does not request without --framework."),
        new("examples/16-pixel-editor/makeicon.prl", CorpusKind.CompileOnly,
            "Build step: writes the program's .ico through the shim, so it needs the Windows "
            + "Desktop runtime pack that the emitted runtimeconfig does not request without "
            + "--framework. Run by build.ps1 before the editor is compiled."),

        // The test programs themselves are libraries; run_tests.prl is the only entry point.
        new("examples/16-pixel-editor/tests/test_tools.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/run_tests.prl."),
        new("examples/16-pixel-editor/tests/test_appicon.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/run_tests.prl."),

        // The standard library's own suite. The modules under std/ are not scanned by this corpus
        // — nothing there is, by design — so this is what compiles and exercises them.
        new("tests/std/run_tests.prl", CorpusKind.Runnable),
        new("tests/std/test_util.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_intstack.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_color.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_document.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_raster.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_shape.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_fill.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_history.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_palette.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_view.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_theme.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_icons.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("tests/std/test_layout.prl", CorpusKind.CompileOnly,
            "Library — test cases; run by tests/std/run_tests.prl."),
        new("examples/05-dotnet-interop-assembly-loading/05_dotnet_interop.prl", CorpusKind.CompileOnly,
            "Prints a freshly generated Guid, so its output is not reproducible. Needs "
            + "test_lib/CSharpFibonacci.csproj built, which ProLang.Tests.csproj does."),
        new("examples/05-dotnet-interop-assembly-loading/csharp_fibonacci.prl", CorpusKind.CompileOnly,
            "Declares the interop reference and has no main()."),
        new("tests/cast-error-test.prl", CorpusKind.CompileOnly,
            "Negative test: compiles, then throws an InvalidCastException at runtime by design."),
        new("tests/cast-invalid.prl", CorpusKind.CompileOnly,
            "Negative test: compiles, then throws an InvalidCastException at runtime by design."),
        new("tests/cast-multiple.prl", CorpusKind.CompileOnly,
            "CODEGEN BUG: compiles and is snapshotted, but `any as int` on a boxed value NREs at "
            + "runtime in __UserMain, so its output cannot be asserted. The snapshot captures the "
            + "faulty IL; fixing the bug is expected to change it."),

        // ── Pre-existing failures, tracked so they cannot regress further or vanish ───────
        new("tests/language/parser/test-parser-simple.prl", CorpusKind.KnownBroken,
            "BINDER CRASH: NullReferenceException in Binder.BindReturnStatement for a top-level return."),
        new("tests/language/parser/test-parse-result.prl", CorpusKind.KnownBroken,
            "BINDER CRASH: same as test-parser-simple.prl."),
        new("tests/language/compiler/test-else-if.prl", CorpusKind.KnownBroken,
            "Corpus rot: global statements, written before main() became mandatory."),
        new("tests/language/compiler/undefined-function-is-not-bcl.prl", CorpusKind.KnownBroken,
            "Negative test by design: a misspelled call must report an undefined function rather "
            + "than binding to a same-named static somewhere in the BCL. If this starts compiling, "
            + "the unqualified-call fallback has gone back to guessing."),
        new("tests/language/lexer/test-lexer-digits.prl", CorpusKind.KnownBroken,
            "Corpus rot: global statements."),
        new("tests/language/generics/single_parameter.prl", CorpusKind.KnownBroken,
            "Corpus rot: global statements."),
        new("tests/language/generics/multiple_parameters.prl", CorpusKind.KnownBroken,
            "Corpus rot: global statements."),
        new("tests/language/string-methods/test-string-methods.prl", CorpusKind.KnownBroken,
            "Corpus rot: global statements."),
        new("tests/language/string-methods/test-string-methods-build.prl", CorpusKind.KnownBroken,
            "Corpus rot: global statements."),
        new("tests/language/string-methods/test-string-methods-compiler.prl", CorpusKind.KnownBroken,
            "Corpus rot: global statements."),
        new("tests/language/string-methods/phase-b-string-methods.prl", CorpusKind.KnownBroken,
            "Corpus rot: global statements."),
        new("tests/language/entry-point/main-with-args.prl", CorpusKind.KnownBroken,
            "MISSING FEATURE: `length(args)` on array<string> does not resolve."),
        new("tests/language/entry-point/global-statements-error.prl", CorpusKind.KnownBroken,
            "Negative test by design: asserts global statements are rejected."),
        new("examples/15-psp-demo/psp_demo.prl", CorpusKind.KnownBroken,
            "PSP target: psp_* builtins have no .NET implementation. Use --emit-psp."),
        new("examples/15-psp-demo/psp_chip8.prl", CorpusKind.KnownBroken,
            "PSP target: see psp_demo.prl."),
        new("examples/09-json-parser/json-file-utils.prl", CorpusKind.KnownBroken,
            "Depends on symbols defined in json-parser.prl; must be compiled together with it."),
    ];

    /// <summary>Programs that compile — everything an IL snapshot can be taken of.</summary>
    public static IEnumerable<CorpusEntry> Compilable =>
        All.Where(e => e.Kind is CorpusKind.Runnable or CorpusKind.CompileOnly);

    /// <summary>Programs whose stdout can be asserted.</summary>
    public static IEnumerable<CorpusEntry> Runnable =>
        All.Where(e => e.Kind == CorpusKind.Runnable);

    /// <summary>Programs currently expected to fail compilation.</summary>
    public static IEnumerable<CorpusEntry> KnownBroken =>
        All.Where(e => e.Kind == CorpusKind.KnownBroken);

    /// <summary>
    /// Looks an entry up by its repository-relative path.
    /// </summary>
    /// <remarks>
    /// Theories feed on paths rather than <see cref="CorpusEntry"/> instances because xUnit can
    /// only serialise primitives in <c>[MemberData]</c>; a record would show up as an opaque
    /// type name in the test explorer and trip xUnit1045.
    /// </remarks>
    public static CorpusEntry Get(string relativePath) =>
        All.FirstOrDefault(e => e.RelativePath == relativePath)
        ?? throw new ArgumentException($"'{relativePath}' is not in the test corpus.", nameof(relativePath));

    /// <summary>xUnit <c>[MemberData]</c> feed for <see cref="Compilable"/>.</summary>
    public static TheoryData<string> CompilableData => ToTheoryData(Compilable);

    /// <summary>xUnit <c>[MemberData]</c> feed for <see cref="Runnable"/>.</summary>
    public static TheoryData<string> RunnableData => ToTheoryData(Runnable);

    /// <summary>xUnit <c>[MemberData]</c> feed for <see cref="KnownBroken"/>.</summary>
    public static TheoryData<string> KnownBrokenData => ToTheoryData(KnownBroken);

    private static TheoryData<string> ToTheoryData(IEnumerable<CorpusEntry> entries)
    {
        var data = new TheoryData<string>();

        foreach (var entry in entries)
        {
            data.Add(entry.RelativePath);
        }

        return data;
    }
}
