using System;
using System.Runtime.InteropServices;

namespace ProLang.Portable;

/// <summary>
/// Desktop-only. Pins the numbers the C glue (<c>psp/host.h</c> and <c>psp/entry.h</c>) depends on, so a change to
/// a constant in <c>JitAbi</c>, <c>Limits</c> or <c>JitFrame</c> cannot silently desynchronise the PSP build from
/// the C literals that must equal it. Run by <c>--self-test</c>.
/// </summary>
internal static class AbiTests
{
    /// <summary>
    /// Checks the helper numbers, the trap reasons (and that two of them equal <c>ValueKind</c> values), the value
    /// stack and handle table limits, and the layout of <c>Value</c>, against the numbers the C side assumes.
    /// </summary>
    internal static void TestAbi()
    {
        // Helper numbers: Host_Helper in psp/entry.h maps each to its function. Number 4 has no case there;
        // it is the default arm, pvm_trap.
        Expect("JitAbi.HelperEnter", JitAbi.HelperEnter, 1);
        Expect("JitAbi.HelperLeave", JitAbi.HelperLeave, 2);
        Expect("JitAbi.HelperSlow", JitAbi.HelperSlow, 3);
        Expect("JitAbi.HelperTrap", JitAbi.HelperTrap, 4);
        Expect("JitAbi.HelperArrayElement", JitAbi.HelperArrayElement, 5);
        Expect("JitAbi.HelperFieldElement", JitAbi.HelperFieldElement, 6);
        Expect("JitAbi.HelperArrayLength", JitAbi.HelperArrayLength, 7);

        // Trap reasons: pvm_trap in psp/host.h turns each into a message. psp/host.h is included before the
        // generated code, so it keeps these as literals.
        Expect("JitAbi.TrapExpectedInt32", JitAbi.TrapExpectedInt32, 1);
        Expect("JitAbi.TrapExpectedArray", JitAbi.TrapExpectedArray, 3);
        Expect("JitAbi.TrapBudget", JitAbi.TrapBudget, 10);
        Expect("JitAbi.TrapIndexOutOfRange", JitAbi.TrapIndexOutOfRange, 11);
        Expect("JitAbi.TrapDivideByZero", JitAbi.TrapDivideByZero, 12);
        Expect("JitAbi.TrapDivisionOverflow", JitAbi.TrapDivisionOverflow, 13);

        // Jit.EmitKindCheck passes the expected value kind to the trap helper as the reason. Two traps therefore share
        // a number with a ValueKind, and must keep doing so.
        Expect("JitAbi.TrapExpectedInt32 == ValueKind.Int32", JitAbi.TrapExpectedInt32, ValueKind.Int32);
        Expect("JitAbi.TrapExpectedArray == ValueKind.Array", JitAbi.TrapExpectedArray, ValueKind.Array);

        // The value stack ends, and the globals area begins, at this slot. psp/entry.h bounds every JIT frame
        // index (pvm_frame_index, Host_Call) and every global slot (Host_GlobalSlot) by it.
        Expect("Limits.ValueStackSlots", Limits.ValueStackSlots, 16384);
        // A separate limit, deliberately not tied to the one above.
        Expect("Limits.HandleTableSize", Limits.HandleTableSize, 16384);

        // Generated MIPS indexes a Value as two int32 words: Kind at 0 and Data at 4, eight bytes in all.
        Expect("JitFrame.SlotSize", JitFrame.SlotSize, Marshal.SizeOf<Value>());
        Expect("JitFrame.KindOffset", JitFrame.KindOffset, (int)Marshal.OffsetOf<Value>("Kind"));
        Expect("JitFrame.DataOffset", JitFrame.DataOffset, (int)Marshal.OffsetOf<Value>("Data"));
        Expect("JitFrame.SlotSize", JitFrame.SlotSize, 8);

        Console.WriteLine("PASS portable ABI constants");
    }

    /// <summary>Fails, naming the constant, if <c>actual</c> is not <c>expected</c>.</summary>
    private static void Expect(string name, int actual, int expected)
    {
        if (actual != expected)
        {
            throw new Exception("ABI constant " + name + " is " + actual + ", expected " + expected);
        }
    }
}
