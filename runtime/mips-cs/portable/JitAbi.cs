namespace ProLang.Portable;

/// <summary>Numbers shared with the C glue (<c>psp/host.h</c> and <c>psp/entry.h</c>).</summary>
/// <remarks>
/// <para>Generated MIPS passes a helper number to <c>Host.Helper</c> and a trap reason to the trap helper; the C
///   side switches on the same values. <c>AbiTests</c> pins every one of them.</para>
/// </remarks>
internal static class JitAbi
{
    // Helper numbers: the argument of Host.Helper. psp/entry.h maps them to functions in Host_Helper.
    internal const int HelperEnter = 1;
    internal const int HelperLeave = 2;
    internal const int HelperSlow = 3;
    internal const int HelperTrap = 4;
    internal const int HelperArrayElement = 5;
    internal const int HelperFieldElement = 6;
    internal const int HelperArrayLength = 7;

    // Trap reasons: the argument of the trap helper, mapped to a message in pvm_trap (psp/host.h).
    // Jit.EmitKindCheck passes the expected value kind as the reason, so the first two are ValueKind values.
    internal const int TrapExpectedInt32 = 1;
    internal const int TrapExpectedArray = 3;
    internal const int TrapBudget = 10;
    // Nothing emits this reason today (array accesses are range-checked by Objects.ValidateArray inside
    // pvm_array_element), but pvm_trap in psp/host.h still maps it to a message.
    internal const int TrapIndexOutOfRange = 11;
    internal const int TrapDivideByZero = 12;
    internal const int TrapDivisionOverflow = 13;
}
