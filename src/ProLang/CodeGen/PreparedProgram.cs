using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Parse;

namespace ProLang.CodeGen;

/// <summary>
/// The result of running every phase up to code generation: either a bound program ready to emit,
/// or the diagnostics that stopped it.
/// </summary>
/// <remarks>
/// Each backend — MSIL, C99, PSP — used to repeat the same four-stage guard before emitting:
/// check import diagnostics, then parse diagnostics, then binding diagnostics, then the bound
/// program's own diagnostics. Three copies meant three places for the order to drift.
/// </remarks>
internal readonly struct PreparedProgram
{
    private PreparedProgram(BoundProgram? program, ImmutableArray<Diagnostic> diagnostics)
    {
        Program = program;
        Diagnostics = diagnostics;
    }

    /// <summary>The program to emit, or <see langword="null"/> if a phase reported diagnostics.</summary>
    public BoundProgram? Program { get; }

    /// <summary>Diagnostics from the first phase that reported any. Empty on success.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>True when <see cref="Program"/> is available and emission may proceed.</summary>
    public bool IsReady => Program != null;

    /// <summary>Creates a successful result.</summary>
    public static PreparedProgram Success(BoundProgram program) => new(program, []);

    /// <summary>Creates a failed result carrying the diagnostics that stopped compilation.</summary>
    public static PreparedProgram Failed(ImmutableArray<Diagnostic> diagnostics) => new(null, diagnostics);
}
