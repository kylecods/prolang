namespace ProLang.Portable;

/// <summary>The MIPS JIT: compiles every loaded method to MIPS32 words and hands them to the host to run.</summary>
/// <remarks>
/// <para><c>Compile</c> first folds calls of constant leaf methods and reserves scratch slots for inlining, then
///   compiles each method with <c>CompileMethod</c>: prologue, body, branch fixups, then <c>Host.CodeReady</c>.
///   Each method becomes a native function that takes the frame pointer in <c>$a0</c> and the result pointer in
///   <c>$a1</c>. The value stack stays in memory (<c>$s0</c> is the frame, see <c>JitFrame</c>); only integers are
///   cached in registers, by <c>RegisterCache</c>.</para>
/// <para>Integer arithmetic, locals, arguments, constants, branches, direct calls (inlined when the callee is a
///   small leaf), static fields and array and field access have native templates. Anything else is compiled as a
///   call of the Slow helper, which runs that one instruction in <c>Interpreter.ExecuteResolved</c>. Failed checks
///   call the trap helper.</para>
/// <para>Generated code calls back into C through numbered helpers (<c>JitAbi</c>). <c>HelperEffects</c> says what
///   each can do to the frame, and so when the register cache must be flushed before it.</para>
/// </remarks>
internal static class Jit
{
    // Leaf inlining (see IsLeaf): a method is a leaf when it has at most LeafMaxOps instructions and calls only
    // leaves, which may nest LeafMaxNesting levels deep. A call is inlined while fewer than MaxInlineDepth
    // inlinings enclose it. Compile makes ScratchPasses passes to reserve the inlined callees' scratch slots.
    internal const int LeafMaxOps = 80;
    internal const int LeafMaxNesting = 1;
    internal const int MaxInlineDepth = 2;
    internal const int ScratchPasses = 2;
    // The code buffer for one method holds WordsPerOp words per IL instruction plus BaseWords. The label and
    // branch fixup tables are the same size.
    internal const int WordsPerOp = 256;
    internal const int BaseWords = 2048;
    // A division or remainder by a constant power of two no larger than this is emitted as shifts and masks.
    internal const int MaxPowerOfTwoImmediate = 32768;

    /// <summary>
    /// 1 once <c>Compile</c> has produced code for every method, 0 before that and after <c>Reset</c>. While it is
    /// 1, <c>Interpreter.Invoke</c> runs a method by calling its compiled code through <c>Host.Call</c> instead of
    /// interpreting it.
    /// </summary>
    internal static int Enabled;

    /// <summary>Helper effect bit: the helper reads slots of the frame in memory.</summary>
    private const int ReadsFrame = 1;
    /// <summary>Helper effect bit: the helper writes slots of the frame in memory.</summary>
    private const int WritesFrame = 2;
    /// <summary>Helper effect bit: the helper may allocate guest objects.</summary>
    private const int Allocates = 4;
    /// <summary>Helper effect bit: the helper may run the garbage collector.</summary>
    private const int Collects = 8;
    /// <summary>
    /// Helper effect bit: the helper never returns to the generated code. The trap helper is the only one.
    /// </summary>
    internal const int NoReturn = 16;
    /// <summary>
    /// The effects that require the register cache to be flushed before the helper is called: <c>ReadsFrame |
    /// WritesFrame | Allocates | Collects</c>. It is written as a single literal because cs-to-c needs one numeric
    /// literal for a constant.
    /// </summary>
    internal const int NeedsPublishedFrame = 15;

    /// <summary>Disables the JIT, until <c>Compile</c> runs again.</summary>
    internal static void Reset()
    {
        Enabled = 0;
    }

    /// <summary>
    /// The effects of helper number <c>number</c> (a <c>JitAbi.Helper*</c> value), as a set of <c>ReadsFrame</c>,
    /// <c>WritesFrame</c>, <c>Allocates</c>, <c>Collects</c> and <c>NoReturn</c>.
    /// </summary>
    /// <remarks>
    /// <para>This is the helper effect contract. Cached integers exist only in <c>$s2</c> to <c>$s7</c> until they
    ///   are flushed, so any helper that can read or write the frame, allocate or collect must be preceded by a
    ///   flush, and <c>EmitHelperCall</c> flushes whenever the result has a bit of <c>NeedsPublishedFrame</c>.
    ///   Enter, Leave and Slow do; the array element, field element and array length helpers only read the handle
    ///   table, and the PSP ABI preserves <c>$s2</c> to <c>$s7</c> across every native helper call, so for them the
    ///   effects are empty and the cache stays live. A trap never rejoins the fast path, so it is <c>NoReturn</c>
    ///   and needs no flush.</para>
    /// <para>An unknown helper number is treated conservatively, as needing the frame. At present only the
    ///   <c>NeedsPublishedFrame</c> mask is tested; the individual bits are not distinguished.</para>
    /// </remarks>
    internal static int HelperEffects(int number)
    {
        if (number == JitAbi.HelperEnter)
        {
            return ReadsFrame | WritesFrame;
        }
        if (number == JitAbi.HelperLeave)
        {
            return WritesFrame;
        }
        if (number == JitAbi.HelperSlow)
        {
            return ReadsFrame | WritesFrame | Allocates | Collects;
        }
        if (number == JitAbi.HelperTrap)
        {
            return NoReturn;
        }
        if (number == JitAbi.HelperArrayElement || number == JitAbi.HelperFieldElement || number == JitAbi.HelperArrayLength)
        {
            return 0;
        }
        return NeedsPublishedFrame;
    }

    /// <summary>
    /// Emits a call of helper <c>number</c>: a cache flush first if <c>HelperEffects</c> requires it, then
    /// <c>jalr</c> through the address <c>Host.Helper</c> returns, in <c>$t9</c>, with a <c>nop</c> in the delay
    /// slot. The arguments are already in <c>$a0</c> to <c>$a2</c>.
    /// </summary>
    private static void EmitHelperCall(int number)
    {
        if ((HelperEffects(number) & NeedsPublishedFrame) != 0)
        {
            RegisterCache.FlushCache();
        }
        MipsAssembler.Li(MipsReg.T9, Host.Helper(number));
        MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T9, MipsReg.Zero, MipsReg.Ra, 0, MipsFunct.Jalr));
        MipsAssembler.Emit(MipsAssembler.Nop);
    }

    /// <summary>
    /// Emits a call of the trap helper with <c>reason</c> in <c>$a0</c>. The helper does not return.
    /// </summary>
    private static void EmitTrap(int reason)
    {
        MipsAssembler.Li(MipsReg.A0, reason);
        EmitHelperCall(JitAbi.HelperTrap);
    }

    /// <summary>
    /// Emits a check that the kind word of frame slot <c>slot</c> is <c>kind</c>, and a trap if it is not. The trap
    /// reason is the expected kind itself.
    /// </summary>
    private static void EmitKindCheck(int slot, int kind)
    {
        int okLabel = MipsAssembler.Label();
        RegisterCache.Lw(MipsReg.T8, slot, JitFrame.KindOffset);
        MipsAssembler.Li(MipsReg.T9, kind);
        MipsAssembler.Branch(MipsOp.Beq, MipsReg.T8, MipsReg.T9, okLabel);
        // The trap reason is the expected value kind: JitAbi.TrapExpectedInt32 equals ValueKind.Int32 and
        // JitAbi.TrapExpectedArray equals ValueKind.Array, which AbiTests pins.
        EmitTrap(kind);
        MipsAssembler.Bind(okLabel);
    }

    /// <summary>
    /// Emits the instruction budget charge for a basic block: it adds <c>instructionCount</c> to the step counter
    /// in memory (<c>Host.Counter</c>, the same variable the interpreter charges as <c>Interpreter.Steps</c>) and
    /// traps with <c>JitAbi.TrapBudget</c> once the count passes <c>Limits.InstructionBudget</c>.
    /// </summary>
    private static void EmitBudgetCheck(int instructionCount)
    {
        int okLabel = MipsAssembler.Label();
        MipsAssembler.Li(MipsReg.T9, Host.Counter());
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.T9, MipsReg.T8, 0));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Addiu, MipsReg.T8, MipsReg.T8, instructionCount));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.T9, MipsReg.T8, 0));
        MipsAssembler.Li(MipsReg.T9, Limits.InstructionBudget);
        MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T9, MipsReg.T8, MipsReg.T8, 0, MipsFunct.Slt));
        MipsAssembler.Branch(MipsOp.Beq, MipsReg.T8, MipsReg.Zero, okLabel);
        EmitTrap(JitAbi.TrapBudget);
        MipsAssembler.Bind(okLabel);
    }

    /// <summary>True for <c>ldc.i4.m1</c> through <c>ldc.i4</c>: an instruction that pushes a constant.</summary>
    private static bool IsConstant(PlanOp op)
    {
        return (op.Code >= Il.LdcI4_M1 && op.Code <= Il.LdcI4);
    }

    /// <summary>
    /// The number that a constant-loading instruction pushes: the short forms (<c>ldc.i4.m1</c> to <c>ldc.i4.8</c>)
    /// encode it in the opcode, <c>ldc.i4.s</c> and <c>ldc.i4</c> in the operand.
    /// </summary>
    private static int ConstantValue(PlanOp op)
    {
        return op.Code < Il.LdcI4S ? op.Code - Il.LdcI4_0 : op.Operand;
    }

    /// <summary>
    /// Rewrites a call instruction, in place, into <c>ldc.i4 value</c>. The plan's flags are left as they are: a
    /// call of a parameterless method that returns an int has the same stack effect as the constant load that
    /// replaces it.
    /// </summary>
    internal static void FoldConstantCall(ref PlanOp op, int value)
    {
        op.Code = Il.LdcI4;
        op.Operand = value;
    }

    /// <summary>
    /// True when the method can be inlined: it has at most <c>LeafMaxOps</c> instructions, no <c>callvirt</c> or
    /// <c>newobj</c>, and every <c>call</c> in it is to a method of this program that is itself a leaf, to a depth
    /// of <c>LeafMaxNesting</c>.
    /// </summary>
    private static bool IsLeaf(int methodId, int nesting)
    {
        ref MethodBody body = ref Loader.Bodies[methodId];
        if (body.OpCount > LeafMaxOps || nesting > LeafMaxNesting)
        {
            return false;
        }

        for (int i = 0; i < body.OpCount; i++)
        {
            ref PlanOp op = ref body.Plan[i];
            ColdOp coldOp = body.Cold[op.ColdIndex];
            if (op.Code == Il.Callvirt || op.Code == Il.Newobj)
            {
                return false;
            }

            if (op.Code == Il.Call && (coldOp.MethodIndex < 0 || !IsLeaf(coldOp.MethodIndex, nesting + 1)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Emits a call of the Slow helper for instruction <c>pc</c> of method <c>methodId</c>: the method id and
    /// <c>pc</c> in <c>$a0</c> and <c>$a1</c>, and in <c>$a2</c> the address of the frame slot <c>origin</c> (the
    /// start of the frame being compiled). The result, a branch decision, comes back in <c>$v0</c>.
    /// </summary>
    private static void EmitSlowCall(int methodId, int pc, int origin)
    {
        MipsAssembler.Li(MipsReg.A0, methodId);
        MipsAssembler.Li(MipsReg.A1, pc);
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Addiu, MipsReg.S0, MipsReg.A2, origin * JitFrame.SlotSize));
        EmitHelperCall(JitAbi.HelperSlow);
    }

    /// <summary>
    /// Emits an arithmetic or comparison instruction (<c>Il.Add</c> to <c>Il.ShrUn</c>, <c>Il.Cgt</c> to
    /// <c>Il.CltUn</c>). The operands are the frame slots <c>stackPointer - 2</c> and <c>stackPointer - 1</c>, or,
    /// when a constant load has been fused with it (<c>hasImmediate</c>), the slot <c>stackPointer - 1</c> and
    /// <c>immediate</c>. An operand known to be an <c>Int32</c> (<c>leftKnownInt</c>, <c>rightKnownInt</c>) skips
    /// its kind check. The result is cached in the left operand's slot.
    /// </summary>
    /// <remarks>
    /// <para>The switch case labels are <c>Il</c> opcodes as numeric literals, each with a comment naming the
    ///   mnemonic, because a C case label cannot be a variable.</para>
    /// </remarks>
    private static void EmitArithmetic(int code, int stackPointer, int immediate, int hasImmediate, int leftKnownInt, int rightKnownInt)
    {
        int left = hasImmediate != 0 ? stackPointer - 1 : stackPointer - 2;
        if (leftKnownInt == 0)
        {
            EmitKindCheck(left, ValueKind.Int32);
        }

        RegisterCache.Lw(MipsReg.T0, left, JitFrame.DataOffset);
        if (hasImmediate != 0)
        {
            MipsAssembler.Li(MipsReg.T1, immediate);
        }
        else
        {
            if (rightKnownInt == 0)
            {
                EmitKindCheck(stackPointer - 1, ValueKind.Int32);
            }

            RegisterCache.Lw(MipsReg.T1, stackPointer - 1, JitFrame.DataOffset);
        }

        // Case labels are Il opcodes (Il.Add to Il.CltUn), kept as literals because a C case label cannot be a variable.
        switch (code)
        {
            case 0x58: // Add
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.T0, 0, MipsFunct.Addu));
                break;
            case 0x59: // Sub
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.T0, 0, MipsFunct.Subu));
                break;
            case 0x5a: // Mul
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.Zero, 0, MipsFunct.Mult));
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.Zero, MipsReg.Zero, MipsReg.T0, 0, MipsFunct.Mflo));
                break;
            case 0x5b: // Div
            case 0x5c: // DivUn
            case 0x5d: // Rem
            case 0x5e: // RemUn
                EmitDivision(code, immediate, hasImmediate);
                break;
            case 0x5f: // And
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.T0, 0, MipsFunct.And));
                break;
            case 0x60: // Or
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.T0, 0, MipsFunct.Or));
                break;
            case 0x61: // Xor
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.T0, 0, MipsFunct.Xor));
                break;
            case 0x62: // Shl
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T1, MipsReg.T0, MipsReg.T0, 0, MipsFunct.Sllv));
                break;
            case 0x63: // Shr
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T1, MipsReg.T0, MipsReg.T0, 0, MipsFunct.Srav));
                break;
            case 0x64: // ShrUn
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T1, MipsReg.T0, MipsReg.T0, 0, MipsFunct.Srlv));
                break;
            case 0xfe02: // Cgt
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T1, MipsReg.T0, MipsReg.T0, 0, MipsFunct.Slt));
                break;
            case 0xfe03: // CgtUn
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T1, MipsReg.T0, MipsReg.T0, 0, MipsFunct.Sltu));
                break;
            case 0xfe04: // Clt
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.T0, 0, MipsFunct.Slt));
                break;
            case 0xfe05: // CltUn
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.T0, 0, MipsFunct.Sltu));
                break;
            default:
                Runtime.Fail("JIT arithmetic template");
                break;
        }

        RegisterCache.StoreInt(left, MipsReg.T0);
    }

    /// <summary>
    /// Emits <c>Div</c>, <c>DivUn</c>, <c>Rem</c> or <c>RemUn</c> of <c>$t0</c> by <c>$t1</c>, with the result in
    /// <c>$t0</c>. A division by a constant power of two is matched to C's strength reduction (fixed-point /4096
    /// and power-of-two remainder) and preserves CLI truncation toward zero for negative operands.
    /// </summary>
    private static void EmitDivision(int code, int immediate, int hasImmediate)
    {
        if (hasImmediate != 0 && immediate > 0 && immediate <= MaxPowerOfTwoImmediate && (immediate & (immediate - 1)) == 0)
        {
            EmitDivisionByPowerOfTwo(code, immediate);
        }
        else
        {
            EmitDivisionGeneral(code);
        }
    }

    /// <summary>Division or remainder by the constant power of two <c>immediate</c>, as shifts and masks.</summary>
    private static void EmitDivisionByPowerOfTwo(int code, int immediate)
    {
        int shift = 0;
        for (int value = immediate; value > 1; value >>= 1)
        {
            shift++;
        }

        if (code == Il.Div || code == Il.Rem)
        {
            MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.Zero, MipsReg.T0, MipsReg.T2, 31, MipsFunct.Sra));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Andi, MipsReg.T2, MipsReg.T2, immediate - 1));
            MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T2, MipsReg.T0, 0, MipsFunct.Addu));
            if (code == Il.Div)
            {
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.Zero, MipsReg.T0, MipsReg.T0, shift, MipsFunct.Sra));
            }
            else
            {
                MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Andi, MipsReg.T0, MipsReg.T0, immediate - 1));
                MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T2, MipsReg.T0, 0, MipsFunct.Subu));
            }
        }
        else if (code == Il.DivUn)
        {
            MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.Zero, MipsReg.T0, MipsReg.T0, shift, MipsFunct.Srl));
        }
        else
        {
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Andi, MipsReg.T0, MipsReg.T0, immediate - 1));
        }
    }

    /// <summary>
    /// The hardware divide, guarded by the traps the CLI requires: divide by zero, and <c>int.MinValue / -1</c>.
    /// </summary>
    private static void EmitDivisionGeneral(int code)
    {
        int nonzero = MipsAssembler.Label();
        MipsAssembler.Branch(MipsOp.Bne, MipsReg.T1, MipsReg.Zero, nonzero);
        EmitTrap(JitAbi.TrapDivideByZero);
        MipsAssembler.Bind(nonzero);
        if (code == Il.Div)
        {
            int safe = MipsAssembler.Label();
            MipsAssembler.Li(MipsReg.T8, -1);
            MipsAssembler.Branch(MipsOp.Bne, MipsReg.T1, MipsReg.T8, safe);
            MipsAssembler.Li(MipsReg.T8, -2147483647 - 1);
            MipsAssembler.Branch(MipsOp.Bne, MipsReg.T0, MipsReg.T8, safe);
            EmitTrap(JitAbi.TrapDivisionOverflow);
            MipsAssembler.Bind(safe);
        }

        int done = MipsAssembler.Label();
        if (code == Il.Rem)
        {
            int divide = MipsAssembler.Label();
            MipsAssembler.Li(MipsReg.T8, -1);
            MipsAssembler.Branch(MipsOp.Bne, MipsReg.T1, MipsReg.T8, divide);
            MipsAssembler.Li(MipsReg.T0, 0);
            MipsAssembler.Jump(done);
            MipsAssembler.Bind(divide);
        }

        MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.Zero, 0, code == Il.DivUn || code == Il.RemUn ? MipsFunct.Divu : MipsFunct.Div));
        MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.Zero, MipsReg.Zero, MipsReg.T0, 0, code == Il.Rem || code == Il.RemUn ? MipsFunct.Mfhi : MipsFunct.Mflo));
        MipsAssembler.Bind(done);
    }

    /// <summary>
    /// Emits <c>ret</c>. In an inlined callee (<c>endLabel &gt;= 0</c>) it copies the result to the slot
    /// <c>destination</c>, flushes the cache and jumps to <c>endLabel</c>. In a method of its own it flushes the
    /// cache, calls the Leave helper (keeping the result in a temporary on the native stack across it), stores the
    /// result through <c>$s1</c>, restores the saved registers and returns. An integer return type is checked to be
    /// an <c>Int32</c> first.
    /// </summary>
    private static void EmitReturn(int methodId, int origin, int stackPointer, int endLabel, int destination)
    {
        ref MethodBody body = ref Loader.Bodies[methodId];
        if (Arithmetic.IsIntegerType(body.ReturnType))
        {
            EmitKindCheck(stackPointer - 1, ValueKind.Int32);
        }

        if (endLabel >= 0)
        {
            if (body.ReturnType != ElementType.Void)
            {
                RegisterCache.Copy(stackPointer - 1, destination);
            }

            RegisterCache.FlushCache();
            MipsAssembler.Jump(endLabel);
            return;
        }

        RegisterCache.FlushCache();
        if (body.ReturnType != ElementType.Void)
        {
            RegisterCache.Lw(MipsReg.T0, stackPointer - 1, JitFrame.KindOffset);
            RegisterCache.Lw(MipsReg.T1, stackPointer - 1, JitFrame.DataOffset);
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.Sp, MipsReg.T0, JitFrame.KindOffset));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.Sp, MipsReg.T1, JitFrame.DataOffset));
        }

        MipsAssembler.Li(MipsReg.A0, methodId);
        MipsAssembler.Move(MipsReg.A1, MipsReg.S0);
        EmitHelperCall(JitAbi.HelperLeave);
        if (body.ReturnType != ElementType.Void)
        {
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.Sp, MipsReg.T0, JitFrame.KindOffset));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.Sp, MipsReg.T1, JitFrame.DataOffset));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.S1, MipsReg.T0, JitFrame.KindOffset));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.S1, MipsReg.T1, JitFrame.DataOffset));
        }

        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.Sp, MipsReg.Ra, JitFrame.SaveRa));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.Sp, MipsReg.S0, JitFrame.SaveS0));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.Sp, MipsReg.S1, JitFrame.SaveS1));
        for (int i = 0; i < RegisterCache.Size; i++)
        {
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.Sp, MipsReg.S2 + i, JitFrame.SaveCache - i * JitFrame.SaveStride));
        }

        MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.Ra, MipsReg.Zero, MipsReg.Zero, 0, MipsFunct.Jr));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Addiu, MipsReg.Sp, MipsReg.Sp, JitFrame.Size));
    }

    /// <summary>
    /// True for the opcodes <c>EmitArithmetic</c> handles: <c>Il.Add</c> to <c>Il.ShrUn</c>, and <c>Il.Cgt</c> to
    /// <c>Il.CltUn</c>.
    /// </summary>
    private static bool IsArithmeticCode(int code)
    {
        return (code >= Il.Add && code <= Il.ShrUn) || (code >= Il.Cgt && code <= Il.CltUn);
    }

    /// <summary>
    /// Emits <c>ldelem</c> (<c>isStore</c> 0) or <c>stelem</c> (<c>isStore</c> 1) through the array element helper,
    /// after checking that the array and the index have the right kinds. The helper validates the index and returns
    /// the address of the element in <c>$v0</c>; the value is then copied from or to that address.
    /// </summary>
    private static void EmitArrayAccess(int stackPointer, int isStore)
    {
        int array = stackPointer - (isStore != 0 ? 3 : 2);
        int index = array + 1;
        EmitKindCheck(array, ValueKind.Array);
        EmitKindCheck(index, ValueKind.Int32);
        RegisterCache.Lw(MipsReg.A0, array, JitFrame.DataOffset);
        RegisterCache.Lw(MipsReg.A1, index, JitFrame.DataOffset);
        EmitHelperCall(JitAbi.HelperArrayElement);
        if (isStore == 0)
        {
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.V0, MipsReg.T0, JitFrame.KindOffset));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.V0, MipsReg.T1, JitFrame.DataOffset));
            RegisterCache.Sw(MipsReg.T0, array, JitFrame.KindOffset);
            RegisterCache.Sw(MipsReg.T1, array, JitFrame.DataOffset);
        }
        else
        {
            RegisterCache.Lw(MipsReg.T0, stackPointer - 1, JitFrame.KindOffset);
            RegisterCache.Lw(MipsReg.T1, stackPointer - 1, JitFrame.DataOffset);
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.V0, MipsReg.T0, JitFrame.KindOffset));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.V0, MipsReg.T1, JitFrame.DataOffset));
        }
    }

    /// <summary>
    /// Emits one method's instructions, either as the method itself (<c>inlineDepth</c> 0) or as a leaf callee
    /// inlined into its caller, whose arguments then start at stack slot <c>origin</c> and whose return jumps to
    /// <c>endLabel</c> after copying the result to slot <c>destination</c>.
    /// </summary>
    /// <remarks>
    /// <para><c>opLabels[pc]</c> marks the first word of instruction <c>pc</c>. <c>knownInt[d]</c> is 1 while the
    ///   evaluation stack entry at depth <c>d</c> is known to hold an <c>Int32</c>, so the instructions that use it
    ///   skip their kind check.</para>
    /// <para>At the start of a basic block (<c>Planner.PlanBlockStart</c>) the register cache is flushed, what is
    ///   known about the stack is forgotten, and the block's instruction budget is charged. The dispatch then picks
    ///   the first group an instruction belongs to, and each group has one emitter. An emitter that can leave a
    ///   known <c>Int32</c> on the stack returns 1 for it, which becomes the new <c>knownInt</c> entry.</para>
    /// </remarks>
    private static void EmitBody(int methodId, int origin, int endLabel, int destination, int inlineDepth)
    {
        ref MethodBody body = ref Loader.Bodies[methodId];
        RegisterCache.FlushCache();
        int[] opLabels = new int[body.OpCount + 1];
        int[] knownInt = new int[body.MaxStack + 1];
        for (int i = 0; i <= body.OpCount; i++)
        {
            opLabels[i] = MipsAssembler.Label();
        }

        if (inlineDepth != 0)
        {
            EmitInlineEntry(ref body, origin);
        }

        for (int pc = 0; pc < body.OpCount; pc++)
        {
            ref PlanOp op = ref body.Plan[pc];
            ColdOp coldOp = body.Cold[op.ColdIndex];
            int code = op.Code, operand = op.Operand;
            int stackPointer = origin + op.StackSlot;
            if ((op.Flags & Planner.PlanBlockStart) != 0)
            {
                RegisterCache.FlushCache();
                for (int i = 0; i <= body.MaxStack; i++)
                {
                    knownInt[i] = 0;
                }
            }

            MipsAssembler.Bind(opLabels[pc]);
            if (op.StackSlot < 0)
            {
                continue;
            }

            int depth = op.StackSlot - body.ArgCount - body.LocalCount, resultKnownInt = 0;
            int leftKnownInt = depth >= 2 ? knownInt[depth - 2] : 0, rightKnownInt = depth >= 1 ? knownInt[depth - 1] : 0;
            if ((op.Flags & Planner.PlanBlockStart) != 0)
            {
                EmitBudgetCheck(BlockLength(ref body, pc));
            }

            if (IsConstant(op))
            {
                if (FusesWithNextArithmetic(ref body, pc))
                {
                    EmitArithmetic(body.Plan[pc + 1].Code, stackPointer, ConstantValue(op), 1, rightKnownInt, 1);
                    knownInt[depth - 1] = 1;
                    MipsAssembler.Bind(opLabels[++pc]);
                    continue;
                }
                else
                {
                    EmitConstant(ConstantValue(op), stackPointer);
                    resultKnownInt = 1;
                }
            }
            else if ((code >= Il.Ldarg_0 && code <= Il.Ldarg_3) || code == Il.LdargS || code == Il.Ldarg)
            {
                resultKnownInt = EmitLoadArgument(ref body, origin, code, operand, stackPointer);
            }
            else if ((code >= Il.Ldloc_0 && code <= Il.Ldloc_3) || code == Il.LdlocS || code == Il.Ldloc)
            {
                resultKnownInt = EmitLoadLocal(ref body, origin, code, operand, stackPointer);
            }
            else if ((code >= Il.Stloc_0 && code <= Il.Stloc_3) || code == Il.StlocS || code == Il.Stloc)
            {
                EmitStoreLocal(ref body, origin, code, operand, stackPointer, rightKnownInt);
            }
            else if (code == Il.StargS || code == Il.Starg)
            {
                EmitStoreArgument(ref body, origin, operand, stackPointer, rightKnownInt);
            }
            else if (code == Il.Ldnull)
            {
                EmitLoadNull(stackPointer);
            }
            else if (code == Il.Dup)
            {
                EmitDup(stackPointer, rightKnownInt);
                resultKnownInt = rightKnownInt;
            }
            else if (code == Il.Nop || code == Il.Pop)
            {
            }
            else if (IsArithmeticCode(code))
            {
                EmitArithmetic(code, stackPointer, 0, 0, leftKnownInt, rightKnownInt);
                resultKnownInt = 1;
            }
            else if (code == Il.Ceq)
            {
                EmitCompareEqual(methodId, pc, origin, stackPointer, leftKnownInt, rightKnownInt);
                resultKnownInt = 1;
            }
            else if (code == Il.Ret)
            {
                EmitReturn(methodId, origin, stackPointer, endLabel, destination);
            }
            else if (code == Il.BrS || code == Il.Br)
            {
                EmitBranch(opLabels[operand]);
            }
            else if (code == Il.BrfalseS || code == Il.Brfalse || code == Il.BrtrueS || code == Il.Brtrue)
            {
                EmitConditionalBranch(code, stackPointer, opLabels[operand]);
            }
            else if (code == Il.Call && coldOp.MethodIndex >= 0)
            {
                resultKnownInt = EmitCall(coldOp.MethodIndex, coldOp.ResultType, stackPointer, inlineDepth);
            }
            else if (code == Il.Ldstr)
            {
                RegisterCache.StoreValue(stackPointer, coldOp.Literal);
            }
            else if (code == Il.LdelemI4 || code == Il.LdelemU4 || code == Il.LdelemRef || code == Il.Ldelem)
            {
                EmitArrayAccess(stackPointer, 0);
            }
            else if (code == Il.StelemI4 || code == Il.StelemRef || code == Il.Stelem)
            {
                EmitArrayAccess(stackPointer, 1);
            }
            else if (code == Il.Ldsfld || code == Il.Stsfld)
            {
                resultKnownInt = EmitStaticField(code, operand, stackPointer, rightKnownInt);
            }
            else if (code == Il.Ldlen)
            {
                EmitArrayLength(stackPointer);
                resultKnownInt = 1;
            }
            else if (code == Il.Ldfld)
            {
                resultKnownInt = EmitInstanceField(methodId, pc, origin, operand, stackPointer);
            }
            else if (code == Il.ConvI4 || code == Il.ConvU4 || code == Il.ConvI || code == Il.ConvU)
            {
                EmitConvertToInt(stackPointer, rightKnownInt);
                resultKnownInt = 1;
            }
            else
            {
                resultKnownInt = EmitFallback(methodId, pc, origin, code, operand, coldOp.ResultType, opLabels);
            }

            int popCount = op.Flags & Planner.PlanPopMask, pushCount = (op.Flags >> Planner.PlanPushShift) & Planner.PlanPushMask;
            if (pushCount > 0)
            {
                knownInt[depth - popCount + pushCount - 1] = resultKnownInt;
            }
        }

        RegisterCache.FlushCache();
        MipsAssembler.Bind(opLabels[body.OpCount]);
        Host.ReleaseInts(opLabels);
        Host.ReleaseInts(knownInt);
    }

    /// <summary>
    /// The code an inlined callee starts with. Its arguments are already in place at <c>origin</c>, so this checks
    /// that each integer argument is an <c>Int32</c> and stores every local's default value.
    /// </summary>
    private static void EmitInlineEntry(ref MethodBody body, int origin)
    {
        for (int i = 0; i < body.ArgCount; i++)
        {
            if (Arithmetic.IsIntegerType(body.ArgTypes[i]))
            {
                EmitKindCheck(origin + i, ValueKind.Int32);
            }
        }

        for (int i = 0; i < body.LocalCount; i++)
        {
            RegisterCache.StoreValue(origin + body.ArgCount + i, body.LocalDefaults[i]);
        }
    }

    /// <summary>
    /// How many instructions the basic block that begins at <c>pc</c> has: it ends before the next block start, or
    /// after a <c>ret</c>.
    /// </summary>
    private static int BlockLength(ref MethodBody body, int pc)
    {
        int blockEnd = pc + 1;
        while (blockEnd < body.OpCount && (body.Plan[blockEnd].Flags & Planner.PlanBlockStart) == 0 && body.Plan[blockEnd - 1].Code != Il.Ret)
        {
            blockEnd++;
        }

        return blockEnd - pc;
    }

    /// <summary>
    /// True when the instruction at <c>pc</c> is followed, in the same block, by an arithmetic or
    /// ordered-comparison instruction (see <c>IsArithmeticCode</c>). A constant load then fuses with it, and the
    /// constant becomes its immediate.
    /// </summary>
    private static bool FusesWithNextArithmetic(ref MethodBody body, int pc)
    {
        return pc + 1 < body.OpCount && (body.Plan[pc + 1].Flags & Planner.PlanBlockStart) == 0 && IsArithmeticCode(body.Plan[pc + 1].Code);
    }

    /// <summary><c>ldc.i4</c> and its short forms: push a constant.</summary>
    private static void EmitConstant(int value, int stackPointer)
    {
        MipsAssembler.Li(MipsReg.T0, value);
        RegisterCache.StoreInt(stackPointer, MipsReg.T0);
    }

    /// <summary>
    /// <c>ldarg</c>: push argument <c>arg</c>. Returns 1 when it is an integer argument, so the pushed value is a
    /// known <c>Int32</c>.
    /// </summary>
    private static int EmitLoadArgument(ref MethodBody body, int origin, int code, int operand, int stackPointer)
    {
        int arg = code >= Il.Ldarg_0 && code <= Il.Ldarg_3 ? code - Il.Ldarg_0 : operand;
        if (Arithmetic.IsIntegerType(body.ArgTypes[arg]))
        {
            RegisterCache.CopyInt(origin + arg, stackPointer);
            return 1;
        }

        RegisterCache.Copy(origin + arg, stackPointer);
        return 0;
    }

    /// <summary>
    /// <c>ldloc</c>: push a local. Returns 1 when it is an integer local, so the pushed value is a known
    /// <c>Int32</c>.
    /// </summary>
    private static int EmitLoadLocal(ref MethodBody body, int origin, int code, int operand, int stackPointer)
    {
        int local = code >= Il.Ldloc_0 && code <= Il.Ldloc_3 ? code - Il.Ldloc_0 : operand;
        if (Arithmetic.IsIntegerType(body.LocalTypes[local]))
        {
            RegisterCache.CopyInt(origin + body.ArgCount + local, stackPointer);
            return 1;
        }

        RegisterCache.Copy(origin + body.ArgCount + local, stackPointer);
        return 0;
    }

    /// <summary>
    /// <c>stloc</c>: pop into a local. An integer local first checks that the value is an <c>Int32</c>, unless that
    /// is already known.
    /// </summary>
    private static void EmitStoreLocal(ref MethodBody body, int origin, int code, int operand, int stackPointer, int rightKnownInt)
    {
        int local = code >= Il.Stloc_0 && code <= Il.Stloc_3 ? code - Il.Stloc_0 : operand;
        if (Arithmetic.IsIntegerType(body.LocalTypes[local]))
        {
            if (rightKnownInt == 0)
            {
                EmitKindCheck(stackPointer - 1, ValueKind.Int32);
            }

            RegisterCache.CopyInt(stackPointer - 1, origin + body.ArgCount + local);
        }
        else
        {
            RegisterCache.Copy(stackPointer - 1, origin + body.ArgCount + local);
        }
    }

    /// <summary>
    /// <c>starg</c>: pop into an argument, with the same <c>Int32</c> check as <c>EmitStoreLocal</c>.
    /// </summary>
    private static void EmitStoreArgument(ref MethodBody body, int origin, int operand, int stackPointer, int rightKnownInt)
    {
        if (Arithmetic.IsIntegerType(body.ArgTypes[operand]))
        {
            if (rightKnownInt == 0)
            {
                EmitKindCheck(stackPointer - 1, ValueKind.Int32);
            }

            RegisterCache.CopyInt(stackPointer - 1, origin + operand);
        }
        else
        {
            RegisterCache.Copy(stackPointer - 1, origin + operand);
        }
    }

    /// <summary><c>ldnull</c>: push a zero <c>Value</c>.</summary>
    private static void EmitLoadNull(int stackPointer)
    {
        RegisterCache.Sw(MipsReg.Zero, stackPointer, JitFrame.KindOffset);
        RegisterCache.Sw(MipsReg.Zero, stackPointer, JitFrame.DataOffset);
    }

    /// <summary><c>dup</c>: copy the top of the stack. A known <c>Int32</c> stays in the register cache.</summary>
    private static void EmitDup(int stackPointer, int rightKnownInt)
    {
        if (rightKnownInt != 0)
        {
            RegisterCache.CopyInt(stackPointer - 1, stackPointer);
        }
        else
        {
            RegisterCache.Copy(stackPointer - 1, stackPointer);
        }
    }

    /// <summary>
    /// <c>ceq</c>: both operands <c>Int32</c> compare in registers; anything else (references, other kinds) takes
    /// the slow helper.
    /// </summary>
    private static void EmitCompareEqual(int methodId, int pc, int origin, int stackPointer, int leftKnownInt, int rightKnownInt)
    {
        RegisterCache.FlushCache();
        int fallback = MipsAssembler.Label(), done = MipsAssembler.Label();
        if (leftKnownInt == 0)
        {
            RegisterCache.Lw(MipsReg.T8, stackPointer - 2, JitFrame.KindOffset);
            MipsAssembler.Li(MipsReg.T9, ValueKind.Int32);
            MipsAssembler.Branch(MipsOp.Bne, MipsReg.T8, MipsReg.T9, fallback);
        }

        if (rightKnownInt == 0)
        {
            RegisterCache.Lw(MipsReg.T8, stackPointer - 1, JitFrame.KindOffset);
            MipsAssembler.Li(MipsReg.T9, ValueKind.Int32);
            MipsAssembler.Branch(MipsOp.Bne, MipsReg.T8, MipsReg.T9, fallback);
        }

        RegisterCache.Lw(MipsReg.T0, stackPointer - 2, JitFrame.DataOffset);
        RegisterCache.Lw(MipsReg.T1, stackPointer - 1, JitFrame.DataOffset);
        MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T0, MipsReg.T1, MipsReg.T0, 0, MipsFunct.Xor));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sltiu, MipsReg.T0, MipsReg.T0, 1));
        RegisterCache.StoreInt(stackPointer - 2, MipsReg.T0);
        RegisterCache.FlushCache();
        MipsAssembler.Jump(done);
        MipsAssembler.Bind(fallback);
        EmitSlowCall(methodId, pc, origin);
        MipsAssembler.Bind(done);
    }

    /// <summary><c>br</c> and <c>br.s</c>: jump to the label of the target instruction.</summary>
    private static void EmitBranch(int target)
    {
        RegisterCache.FlushCache();
        MipsAssembler.Jump(target);
    }

    /// <summary>
    /// <c>brfalse</c> and <c>brtrue</c> (and their short forms): an <c>Int32</c> is tested by its value, any other
    /// kind by its kind.
    /// </summary>
    private static void EmitConditionalBranch(int code, int stackPointer, int target)
    {
        RegisterCache.FlushCache();
        RegisterCache.Lw(MipsReg.T0, stackPointer - 1, JitFrame.KindOffset);
        RegisterCache.Lw(MipsReg.T1, stackPointer - 1, JitFrame.DataOffset);
        MipsAssembler.Li(MipsReg.T8, ValueKind.Int32);
        int isIntLabel = MipsAssembler.Label();
        MipsAssembler.Branch(MipsOp.Beq, MipsReg.T0, MipsReg.T8, isIntLabel);
        MipsAssembler.Move(MipsReg.T1, MipsReg.T0);
        MipsAssembler.Bind(isIntLabel);
        MipsAssembler.Branch(code == Il.BrfalseS || code == Il.Brfalse ? MipsOp.Beq : MipsOp.Bne, MipsReg.T1, MipsReg.Zero, target);
    }

    /// <summary>
    /// <c>call</c> of a method in this program: inlined when it is a leaf and fewer than <c>MaxInlineDepth</c>
    /// inlinings enclose it, otherwise a native call through the callee's code slot. Returns 1 when the callee
    /// returns an integer.
    /// </summary>
    private static int EmitCall(int calleeId, int resultType, int stackPointer, int inlineDepth)
    {
        RegisterCache.FlushCache();
        int resultKnownInt = Arithmetic.IsIntegerType(resultType) ? 1 : 0;
        ref MethodBody callee = ref Loader.Bodies[calleeId];
        if (inlineDepth < MaxInlineDepth && IsLeaf(calleeId, 0))
        {
            int done = MipsAssembler.Label();
            EmitBody(calleeId, stackPointer - callee.ArgCount, done, stackPointer - callee.ArgCount, inlineDepth + 1);
            MipsAssembler.Bind(done);
        }
        else
        {
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Addiu, MipsReg.S0, MipsReg.A0, (stackPointer - callee.ArgCount) * JitFrame.SlotSize));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Addiu, MipsReg.S0, MipsReg.A1, (stackPointer - callee.ArgCount) * JitFrame.SlotSize));
            MipsAssembler.Li(MipsReg.T9, Host.MethodSlot(calleeId));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.T9, MipsReg.T9, 0));
            MipsAssembler.Emit(MipsAssembler.EncodeR(MipsReg.T9, MipsReg.Zero, MipsReg.Ra, 0, MipsFunct.Jalr));
            MipsAssembler.Emit(MipsAssembler.Nop);
        }

        return resultKnownInt;
    }

    /// <summary>
    /// <c>ldsfld</c> and <c>stsfld</c>: read or write the static field's slot in the globals area directly. Returns
    /// 1 for a load of an integer field.
    /// </summary>
    private static int EmitStaticField(int code, int operand, int stackPointer, int rightKnownInt)
    {
        int resultKnownInt = 0;
        RegisterCache.FlushCache();
        if (code == Il.Stsfld && Arithmetic.IsIntegerType(TypeSystem.FieldTypes[operand]) && rightKnownInt == 0)
        {
            EmitKindCheck(stackPointer - 1, ValueKind.Int32);
        }
        MipsAssembler.Li(MipsReg.T9, Host.GlobalSlot(Interpreter.GlobalsBase + operand));
        if (code == Il.Ldsfld)
        {
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.T9, MipsReg.T0, JitFrame.KindOffset));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.T9, MipsReg.T1, JitFrame.DataOffset));
            RegisterCache.Sw(MipsReg.T0, stackPointer, JitFrame.KindOffset);
            RegisterCache.Sw(MipsReg.T1, stackPointer, JitFrame.DataOffset);
        }
        else
        {
            RegisterCache.Lw(MipsReg.T0, stackPointer - 1, JitFrame.KindOffset);
            RegisterCache.Lw(MipsReg.T1, stackPointer - 1, JitFrame.DataOffset);
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.T9, MipsReg.T0, JitFrame.KindOffset));
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.T9, MipsReg.T1, JitFrame.DataOffset));
        }
        if (code == Il.Ldsfld && Arithmetic.IsIntegerType(TypeSystem.FieldTypes[operand]))
        {
            resultKnownInt = 1;
        }

        return resultKnownInt;
    }

    /// <summary><c>ldlen</c>: replace the array on the top of the stack with its length.</summary>
    private static void EmitArrayLength(int stackPointer)
    {
        EmitKindCheck(stackPointer - 1, ValueKind.Array);
        RegisterCache.Lw(MipsReg.A0, stackPointer - 1, JitFrame.DataOffset);
        EmitHelperCall(JitAbi.HelperArrayLength);
        RegisterCache.StoreInt(stackPointer - 1, MipsReg.V0);
    }

    /// <summary>
    /// <c>ldfld</c>: a record reads its field through the helper; any other kind takes the slow helper. Returns 1
    /// for an integer field, after checking that the value read is an <c>Int32</c>.
    /// </summary>
    private static int EmitInstanceField(int methodId, int pc, int origin, int operand, int stackPointer)
    {
        int resultKnownInt = 0;
        RegisterCache.FlushCache();
        int slowLabel = MipsAssembler.Label(), done = MipsAssembler.Label();
        RegisterCache.Lw(MipsReg.T8, stackPointer - 1, JitFrame.KindOffset);
        MipsAssembler.Li(MipsReg.T9, ValueKind.Record);
        MipsAssembler.Branch(MipsOp.Bne, MipsReg.T8, MipsReg.T9, slowLabel);
        RegisterCache.Lw(MipsReg.A0, stackPointer - 1, JitFrame.DataOffset);
        MipsAssembler.Li(MipsReg.A1, operand);
        EmitHelperCall(JitAbi.HelperFieldElement);
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.V0, MipsReg.T0, JitFrame.KindOffset));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Lw, MipsReg.V0, MipsReg.T1, JitFrame.DataOffset));
        RegisterCache.Sw(MipsReg.T0, stackPointer - 1, JitFrame.KindOffset);
        RegisterCache.Sw(MipsReg.T1, stackPointer - 1, JitFrame.DataOffset);
        MipsAssembler.Jump(done);
        MipsAssembler.Bind(slowLabel);
        EmitSlowCall(methodId, pc, origin);
        MipsAssembler.Bind(done);
        if (Arithmetic.IsIntegerType(TypeSystem.FieldTypes[operand]))
        {
            EmitKindCheck(stackPointer - 1, ValueKind.Int32);
            resultKnownInt = 1;
        }

        return resultKnownInt;
    }

    /// <summary>
    /// <c>conv.i4</c>, <c>conv.u4</c>, <c>conv.i</c> and <c>conv.u</c>: a no-op on an <c>Int32</c>, so only the
    /// kind check remains.
    /// </summary>
    private static void EmitConvertToInt(int stackPointer, int rightKnownInt)
    {
        if (rightKnownInt == 0)
        {
            EmitKindCheck(stackPointer - 1, ValueKind.Int32);
        }
    }

    /// <summary>
    /// Any instruction without a native template runs through the interpreter's Slow helper. A compare-and-branch
    /// then branches on the helper's result in <c>$v0</c>. Returns 1 when the instruction is known to leave an
    /// <c>Int32</c>.
    /// </summary>
    private static int EmitFallback(int methodId, int pc, int origin, int code, int operand, int resultType, int[] opLabels)
    {
        int resultKnownInt = 0;
        EmitSlowCall(methodId, pc, origin);
        if ((code >= Il.BeqS && code <= Il.BltUnS) || (code >= Il.Beq && code <= Il.BltUn))
        {
            MipsAssembler.Branch(MipsOp.Bne, MipsReg.V0, MipsReg.Zero, opLabels[operand]);
        }

        if ((code >= Il.Div && code <= Il.RemUn) || (code >= Il.Neg && code <= Il.ConvI4) || code == Il.ConvU2 || code == Il.ConvU1 || code == Il.Ceq || ((code == Il.Call || code == Il.Callvirt) && Arithmetic.IsIntegerType(resultType)))
        {
            resultKnownInt = 1;
        }

        return resultKnownInt;
    }

    /// <summary>
    /// Compiles every method to MIPS, handing each one's words to the host, and then turns the JIT on
    /// (<c>Enabled</c>). Constant leaf calls are folded and inline scratch is reserved first, because both change
    /// what the later passes emit.
    /// </summary>
    internal static void Compile()
    {
        RegisterCache.Initialize();
        FoldConstantCalls();
        ReserveInlineScratch();
        for (int methodId = 0; methodId < Loader.MethodCount; methodId++)
        {
            CompileMethod(methodId);
        }

        Enabled = 1;
    }

    /// <summary>
    /// Replaces each call of a method that only returns a constant (a leaf with no arguments, whose body is a
    /// constant load and a <c>ret</c>) by that constant. The original IL remains in the assembly and is listed
    /// separately. Branch stack effects are unchanged by these replacements.
    /// </summary>
    private static void FoldConstantCalls()
    {
        for (int methodId = 0; methodId < Loader.MethodCount; methodId++)
        {
            for (int pc = 0; pc < Loader.Bodies[methodId].OpCount; pc++)
            {
                ref PlanOp op = ref Loader.Bodies[methodId].Plan[pc];
                ColdOp coldOp = Loader.Bodies[methodId].Cold[op.ColdIndex];
                if (op.Code == Il.Call && coldOp.MethodIndex >= 0)
                {
                    ref MethodBody callee = ref Loader.Bodies[coldOp.MethodIndex];
                    if (callee.ArgCount == 0 && callee.OpCount == 2 && IsConstant(callee.Plan[0]) && callee.Plan[1].Code == Il.Ret)
                    {
                        FoldConstantCall(ref op, ConstantValue(callee.Plan[0]));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Raises each method's <c>MaxStack</c> to make room, above its own evaluation stack at the call, for the
    /// locals and stack of every leaf callee that may be inlined there. <c>Interpreter.Leave</c> clears the frame
    /// on return, so the scratch is cleared too. It makes <c>ScratchPasses</c> passes, because a callee's
    /// <c>MaxStack</c> can itself grow in a pass and its callers must then reserve for the new value.
    /// </summary>
    private static void ReserveInlineScratch()
    {
        for (int pass = 0; pass < ScratchPasses; pass++)
        {
            for (int methodId = 0; methodId < Loader.MethodCount; methodId++)
            {
                ref MethodBody body = ref Loader.Bodies[methodId];
                int maxStack = body.MaxStack;
                for (int pc = 0; pc < body.OpCount; pc++)
                {
                    ref PlanOp op = ref body.Plan[pc];
                    ColdOp coldOp = body.Cold[op.ColdIndex];
                    if (op.Code == Il.Call && coldOp.MethodIndex >= 0 && IsLeaf(coldOp.MethodIndex, 0))
                    {
                        ref MethodBody callee = ref Loader.Bodies[coldOp.MethodIndex];
                        int required = op.StackSlot - body.ArgCount - body.LocalCount + callee.LocalCount + callee.MaxStack;
                        if (required > maxStack)
                        {
                            maxStack = required;
                        }
                    }
                }

                body.MaxStack = maxStack;
            }
        }
    }

    /// <summary>
    /// Compiles one method into the assembler's buffer: the prologue, the body, the branch offsets, and then the
    /// words are handed to the host and the buffers released.
    /// </summary>
    private static void CompileMethod(int methodId)
    {
        MipsAssembler.Begin(Loader.Bodies[methodId].OpCount * WordsPerOp + BaseWords);
        RegisterCache.Clear();
        EmitPrologue(methodId);
        EmitBody(methodId, 0, -1, 0, 0);
        MipsAssembler.ResolveFixups();
        Host.CodeReady(methodId, Loader.Bodies[methodId].NameOffset, MipsAssembler.Words, MipsAssembler.WordCount);
        MipsAssembler.Release();
    }

    /// <summary>
    /// Opens the native stack frame and saves <c>$ra</c>, <c>$s0</c>, <c>$s1</c> and the cache registers, takes the
    /// frame and result pointers from <c>$a0</c> and <c>$a1</c> into <c>$s0</c> and <c>$s1</c>, and calls the Enter
    /// helper.
    /// </summary>
    private static void EmitPrologue(int methodId)
    {
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Addiu, MipsReg.Sp, MipsReg.Sp, -JitFrame.Size));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.Sp, MipsReg.Ra, JitFrame.SaveRa));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.Sp, MipsReg.S0, JitFrame.SaveS0));
        MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.Sp, MipsReg.S1, JitFrame.SaveS1));
        for (int i = 0; i < RegisterCache.Size; i++)
        {
            MipsAssembler.Emit(MipsAssembler.EncodeI(MipsOp.Sw, MipsReg.Sp, MipsReg.S2 + i, JitFrame.SaveCache - i * JitFrame.SaveStride));
        }

        MipsAssembler.Move(MipsReg.S0, MipsReg.A0);
        MipsAssembler.Move(MipsReg.S1, MipsReg.A1);
        MipsAssembler.Li(MipsReg.A0, methodId);
        MipsAssembler.Move(MipsReg.A1, MipsReg.S0);
        EmitHelperCall(JitAbi.HelperEnter);
    }
}
