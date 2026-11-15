namespace ProLang.Portable;

/// <summary>
/// Resource limits shared by more than one class. A limit that only one class uses is declared in that class.
/// </summary>
internal static class Limits
{
    // Slots in the value stack. Frames live in [0, ValueStackSlots) and the globals area begins at
    // this index (Interpreter.GlobalsBase). psp/entry.h bounds every JIT frame index by this same number.
    internal const int ValueStackSlots = 16384;

    // Entries in the managed-heap handle table (Heap.HandleTable) and in the collector's mark queue.
    // Deliberately a separate constant from ValueStackSlots: the two only happen to be equal.
    internal const int HandleTableSize = 16384;

    // Instructions a program may execute between two Present calls. The interpreter charges against
    // it and the JIT's budget check compares against the same number.
    internal const int InstructionBudget = 10000000;

    // Depth of nested types in a signature, and of nested struct fields when building a zero value.
    internal const int TypeNesting = 32;

    // Type arguments of one generic instantiation.
    internal const int GenericArity = 16;
}
