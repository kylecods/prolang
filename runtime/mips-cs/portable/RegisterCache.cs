namespace ProLang.Portable;

/// <summary>
/// The JIT's six-register integer cache: which frame slots currently live in <c>$s2</c> to <c>$s7</c> instead of in
/// memory.
/// </summary>
/// <remarks>
/// <para>It is a compile-time model, not run-time state. While emitting a method, <c>_cachedSlots[i]</c> names the
///   frame slot whose integer is held in register <c>$s(2 + i)</c>, or -1 for none. A cached slot is dirty: memory
///   still holds an older value, and the register is the truth. Only the <c>Data</c> word is cached; the kind of a
///   cached slot is implicitly <c>Int32</c>, and <c>FlushEntry</c> writes both words back.</para>
/// <para>The cache must be flushed (<c>FlushCache</c>) wherever generated code could observe the slot in memory or
///   reach the same point by another path: at the start and end of every method body, at every basic block start (a
///   control-flow join, whose incoming states can differ), before every branch, before a call to another method,
///   around instructions that read or write the frame through memory, and before any helper call whose effects need
///   the frame (see <c>Jit.HelperEffects</c>). <c>$s2</c> to <c>$s7</c> are callee-saved, so calls preserve the
///   cached integers, and each method's prologue and epilogue save and restore them.</para>
/// <para>When all six registers are in use, the next integer to be cached evicts the registers in turn
///   (<c>_cacheVictim</c>), flushing the one it takes.</para>
/// </remarks>
internal static class RegisterCache
{
    // How many dirty integer values the cache holds, in callee-saved $s2..$s7.
    internal const int Size = 6;

    // _cachedSlots[i] is the frame slot index whose Int32 number lives in register $s(2 + i), or -1 when that
    // register holds nothing. The kind tag is implicit. _cacheVictim counts evictions, which take the registers in
    // turn. Frame-observing helpers and control-flow joins require the cache to be flushed.
    private static int[] _cachedSlots = null!;
    private static int _cacheVictim;

    /// <summary>
    /// Allocates the table of which frame slot each register holds. <c>Jit.Compile</c> calls it once, before any
    /// method.
    /// </summary>
    internal static void Initialize()
    {
        _cachedSlots = new int[Size];
    }

    /// <summary>
    /// Empties the cache: no register holds a slot, and the next eviction starts at register 0. It runs at the
    /// start of each method, because the generated code begins with nothing cached.
    /// </summary>
    internal static void Clear()
    {
        for (int i = 0; i < Size; i++)
        {
            _cachedSlots[i] = -1;
        }

        _cacheVictim = 0;
    }

    /// <summary>
    /// Emits code that writes every cached slot back to the frame in memory, and empties the cache.
    /// </summary>
    internal static void FlushCache()
    {
        for (int i = 0; i < Size; i++)
        {
            if (_cachedSlots[i] >= 0)
            {
                FlushEntry(i);
            }
        }
    }

    /// <summary>
    /// Emits the write-back of one cached slot: its kind (<c>Int32</c>, through <c>$t8</c>) and its data (from the
    /// cache register), then forgets the entry.
    /// </summary>
    private static void FlushEntry(int entryIndex)
    {
        MipsAssembler.Li(MipsReg.T8, ValueKind.Int32);
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.S0, MipsReg.T8, _cachedSlots[entryIndex] * JitFrame.SlotSize));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.S0, MipsReg.S2 + entryIndex, _cachedSlots[entryIndex] * JitFrame.SlotSize + JitFrame.DataOffset));
        _cachedSlots[entryIndex] = -1;
    }

    /// <summary>
    /// Loads one word of a frame slot into <c>reg</c>: <c>JitFrame.KindOffset</c> or <c>JitFrame.DataOffset</c>. A
    /// cached slot is served from its register, as a register move for its data and as the constant <c>Int32</c>
    /// for its kind; any other slot is loaded from memory.
    /// </summary>
    internal static void Lw(int reg, int slot, int fieldOffset)
    {
        for (int i = 0; i < Size; i++)
        {
            if (slot == _cachedSlots[i])
            {
                if (fieldOffset == JitFrame.KindOffset)
                {
                    MipsAssembler.Li(reg, ValueKind.Int32);
                }
                else
                {
                    MipsAssembler.Move(reg, MipsReg.S2 + i);
                }

                return;
            }
        }

        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.S0, reg, slot * JitFrame.SlotSize + fieldOffset));
    }

    /// <summary>
    /// Stores one word of a frame slot from <c>reg</c>. A cached copy of that slot is flushed and dropped first, so
    /// that memory holds both words of the slot and the store lands on top of a complete value.
    /// </summary>
    internal static void Sw(int reg, int slot, int fieldOffset)
    {
        for (int i = 0; i < Size; i++)
        {
            if (slot == _cachedSlots[i])
            {
                FlushEntry(i);
            }
        }

        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.S0, reg, slot * JitFrame.SlotSize + fieldOffset));
    }

    /// <summary>
    /// Copies a whole value between two frame slots, through <c>$t0</c> and <c>$t1</c>, honouring the cache on both
    /// sides.
    /// </summary>
    internal static void Copy(int source, int destination)
    {
        Lw(MipsReg.T0, source, JitFrame.KindOffset);
        Lw(MipsReg.T1, source, JitFrame.DataOffset);
        Sw(MipsReg.T0, destination, JitFrame.KindOffset);
        Sw(MipsReg.T1, destination, JitFrame.DataOffset);
    }

    /// <summary>
    /// Defines <c>slot</c> as an <c>Int32</c> whose number is in <c>reg</c>, by recording it in a cache register;
    /// nothing is written to memory for <c>slot</c> itself. The register moves into the cache register that already
    /// holds <c>slot</c>, else a free one, else the round-robin victim, which is flushed first.
    /// </summary>
    internal static void StoreInt(int slot, int reg)
    {
        int freeIndex = -1;
        for (int i = 0; i < Size; i++)
        {
            if (_cachedSlots[i] == slot)
            {
                MipsAssembler.Move(MipsReg.S2 + i, reg);
                return;
            }

            if (_cachedSlots[i] < 0)
            {
                freeIndex = i;
            }
        }

        if (freeIndex < 0)
        {
            freeIndex = _cacheVictim++ % Size;
            FlushEntry(freeIndex);
        }

        MipsAssembler.Move(MipsReg.S2 + freeIndex, reg);
        _cachedSlots[freeIndex] = slot;
    }

    /// <summary>
    /// Copies the number of an integer slot into another slot, which becomes cached. Only the data word is read, so
    /// the source must be known to be an <c>Int32</c>.
    /// </summary>
    internal static void CopyInt(int source, int destination)
    {
        Lw(MipsReg.T0, source, JitFrame.DataOffset);
        StoreInt(destination, MipsReg.T0);
    }

    /// <summary>Stores a constant value, both its kind and its data, into a frame slot.</summary>
    internal static void StoreValue(int slot, Value value)
    {
        MipsAssembler.Li(MipsReg.T0, value.Kind);
        Sw(MipsReg.T0, slot, JitFrame.KindOffset);
        MipsAssembler.Li(MipsReg.T1, (int)value.Data);
        Sw(MipsReg.T1, slot, JitFrame.DataOffset);
    }
}
