namespace ProLang.Portable;

/// <summary>
/// A minimal MIPS32 assembler for the JIT: a buffer of instruction words for the method being compiled, labels,
/// branch fixups and the instruction encoders.
/// </summary>
/// <remarks>
/// <para><c>Begin</c> starts a method, <c>Emit</c> and the helpers below append words, <c>Label</c> and <c>Bind</c>
///   mark positions, and <c>ResolveFixups</c> patches every branch once all labels are bound. A branch offset is 16
///   bits, so a method whose code is too long fails with 'JIT branch range'.</para>
/// </remarks>
internal static class MipsAssembler
{
    // Bit positions of the fields of an instruction word: opcode, rs, rt, rd and shift amount.
    internal const int OpShift = 26;
    internal const int RsShift = 21;
    internal const int RtShift = 16;
    internal const int RdShift = 11;
    internal const int ShamtShift = 6;
    // A 16-bit immediate: how many bits it has, the range it can hold as a signed value, and the mask
    // that keeps its low half. The upper half of a 32-bit constant is loaded with lui.
    internal const int ImmediateBits = 16;
    internal const int ImmediateMin = -32768;
    internal const int ImmediateMax = 32767;
    internal const int ImmediateMask = 65535;
    // The upper half of an instruction word: the opcode and registers, which a branch fixup keeps.
    internal const uint HighHalfMask = 0xffff0000U;
    // The instruction word 0, sll $zero, $zero, 0. It fills every branch and call delay slot.
    internal const int Nop = 0;

    // The method being assembled: its words so far, and the buffers Begin allocates for it. Every buffer holds
    // _wordCapacity entries. A label is bound to a word index; a fixup records a branch word and the label it jumps to.
    internal static uint[] Words = null!;
    internal static int WordCount;
    private static int _wordCapacity, _labelCount, _fixupCount;
    private static int[] _labelWordIndex = null!;
    private static int[] _fixupWordIndex = null!;
    private static int[] _fixupLabel = null!;

    /// <summary>
    /// Starts a new method: empties the word, label and fixup buffers and allocates them with room for
    /// <c>capacity</c> entries each. <c>Release</c> hands them back.
    /// </summary>
    internal static void Begin(int capacity)
    {
        _wordCapacity = capacity;
        WordCount = 0;
        _labelCount = 0;
        _fixupCount = 0;
        Words = new uint[_wordCapacity];
        _labelWordIndex = new int[_wordCapacity];
        _fixupWordIndex = new int[_wordCapacity];
        _fixupLabel = new int[_wordCapacity];
    }

    /// <summary>
    /// Patches every recorded branch with the distance to its label, once all labels are bound. A branch to a label
    /// that was never bound, or further than a 16-bit offset reaches, fails with 'JIT branch range'.
    /// </summary>
    internal static void ResolveFixups()
    {
        for (int i = 0; i < _fixupCount; i++)
        {
            int target = _labelWordIndex[_fixupLabel[i]], delta = target - _fixupWordIndex[i] - 1;
            if (target < 0 || delta < ImmediateMin || delta > ImmediateMax)
            {
                Runtime.Fail("JIT branch range");
            }

            Words[_fixupWordIndex[i]] = (Words[_fixupWordIndex[i]] & HighHalfMask) | ((uint)delta & ImmediateMask);
        }
    }

    /// <summary>Hands the buffers <c>Begin</c> allocated back to the host.</summary>
    internal static void Release()
    {
        Host.ReleaseWords(Words);
        Host.ReleaseInts(_labelWordIndex);
        Host.ReleaseInts(_fixupWordIndex);
        Host.ReleaseInts(_fixupLabel);
    }

    /// <summary>Appends one instruction word. Fails with 'JIT code buffer limit' when the buffer is full.</summary>
    internal static void Emit(uint word)
    {
        if (WordCount >= _wordCapacity)
        {
            Runtime.Fail("JIT code buffer limit");
        }

        Words[WordCount++] = word;
    }

    /// <summary>
    /// Loads a 32-bit constant into <c>reg</c>: one <c>addiu</c> when it fits a signed 16-bit immediate, otherwise
    /// <c>lui</c> for the upper half and, if the lower half is not zero, <c>ori</c> for it.
    /// </summary>
    internal static void Li(int reg, int value)
    {
        if (value >= ImmediateMin && value <= ImmediateMax)
        {
            Emit(EncodeI(MipsOp.Addiu, MipsReg.Zero, reg, value));
        }
        else
        {
            Emit(EncodeI(MipsOp.Lui, MipsReg.Zero, reg, (int)((uint)value >> ImmediateBits)));
            if ((value & ImmediateMask) != 0)
            {
                Emit(EncodeI(MipsOp.Ori, reg, reg, value));
            }
        }
    }

    /// <summary>Creates a label that is not yet bound and returns its number.</summary>
    internal static int Label()
    {
        if (_labelCount >= _wordCapacity)
        {
            Runtime.Fail("JIT label limit");
        }

        _labelWordIndex[_labelCount] = -1;
        return _labelCount++;
    }

    /// <summary>Binds <c>label</c> to the next word to be emitted.</summary>
    internal static void Bind(int label)
    {
        _labelWordIndex[label] = WordCount;
    }

    /// <summary>
    /// Emits a branch word followed by a <c>nop</c> in its delay slot, and records a fixup so that
    /// <c>ResolveFixups</c> fills in the distance to the label <c>target</c>.
    /// </summary>
    internal static void Branch(int opcode, int rs, int rt, int target)
    {
        if (_fixupCount >= _wordCapacity)
        {
            Runtime.Fail("JIT fixup limit");
        }

        _fixupWordIndex[_fixupCount] = WordCount;
        _fixupLabel[_fixupCount++] = target;
        Emit(EncodeI(opcode, rs, rt, 0));
        Emit(Nop);
    }

    /// <summary>
    /// An unconditional jump to a label, as <c>beq $zero, $zero</c> with a delay-slot <c>nop</c>.
    /// </summary>
    internal static void Jump(int target)
    {
        Branch(MipsOp.Beq, MipsReg.Zero, MipsReg.Zero, target);
    }

    /// <summary>Copies a register, as <c>addu destination, source, $zero</c>.</summary>
    internal static void Move(int destination, int source)
    {
        Emit(EncodeR(source, MipsReg.Zero, destination, 0, MipsFunct.Addu));
    }

    /// <summary>
    /// Encodes a register-format instruction. Its opcode field is 0, and <c>funct</c> (a <c>MipsFunct</c> value)
    /// selects the operation.
    /// </summary>
    internal static uint EncodeR(int rs, int rt, int rd, int shiftAmount, int funct)
    {
        return (uint)((rs << RsShift) | (rt << RtShift) | (rd << RdShift) | (shiftAmount << ShamtShift) | funct);
    }

    /// <summary>
    /// Encodes an immediate-format instruction from a <c>MipsOp</c> opcode; the immediate keeps only its low 16
    /// bits.
    /// </summary>
    internal static uint EncodeI(int opcode, int rs, int rt, int immediate)
    {
        return (uint)((opcode << OpShift) | (rs << RsShift) | (rt << RtShift) | (immediate & ImmediateMask));
    }
}
