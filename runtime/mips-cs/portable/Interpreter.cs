namespace ProLang.Portable;

/// <summary>
/// The value stack, the IL interpreter, and the entry points that JIT-compiled code calls back into.
/// </summary>
/// <remarks>
/// <para><c>Memory</c> holds the whole guest state: slots 0 up to <c>Limits.ValueStackSlots</c> are the value
///   stack, and the globals area (one slot per field, of which the static ones are used) begins at
///   <c>GlobalsBase</c>. A call's frame is a run of <c>ArgCount + LocalCount + MaxStack</c> slots from
///   <c>frame</c>: the arguments, then the locals, then the evaluation stack. The caller's outgoing arguments are
///   the top entries of its evaluation stack and become the start of the callee's frame, and the result is written
///   back to the slot where the first argument was.</para>
/// <para><c>Steps</c> counts the instructions charged, and <c>Calls</c> the calls entered, since the last display
///   swap, when <c>Natives.Call</c> reports both to the host and zeroes them. JIT code charges the same
///   <c>Steps</c> variable through <c>Host.Counter</c>.</para>
/// </remarks>
internal static class Interpreter
{
    // The most nested calls the interpreter (or the JIT through Enter) allows.
    internal const int MaxCallDepth = 128;

    /// <summary>
    /// The value stack followed by the globals area; the C glue reads this array as <c>Interpreter_Memory</c>.
    /// </summary>
    internal static Value[] Memory = null!;
    /// <summary>The index in <c>Memory</c> of the first global, equal to <c>Limits.ValueStackSlots</c>.</summary>
    internal static int GlobalsBase;
    private static int _callDepth;
    internal static int Steps, Calls;

    /// <summary>Zeroes the call depth and the counters.</summary>
    internal static void Reset()
    {
        _callDepth = Steps = Calls = 0;
    }

    /// <summary>
    /// Runs one instruction on behalf of JIT code: the instruction at index <c>pc</c> of method <c>methodId</c>,
    /// with its frame at <c>frame</c>. It collects first if one is due, which is safe because the JIT has already
    /// flushed its register cache. Returns the branch decision of a compare-and-branch, and 0 for every other
    /// instruction. This is the Slow helper (<c>pvm_slow</c> in <c>psp/entry.h</c>).
    /// </summary>
    internal static int Slow(int methodId, int pc, int frame)
    {
        Heap.CollectIfNeeded();
        ref MethodBody body = ref Loader.Bodies[methodId];
        ref PlanOp op = ref body.Plan[pc];
        return ExecuteResolved(ref body, ref op, frame);
    }

    /// <summary>
    /// Executes one instruction of a verified method, whose frame starts at <c>frame</c>. Returns 1 if a
    /// compare-and-branch is taken, so that the caller jumps to <c>op.Operand</c>, and 0 otherwise.
    /// </summary>
    /// <remarks>
    /// <para>The operands are the top <c>popCount</c> entries of the evaluation stack, which ends at
    ///   <c>stackPointer = frame + op.StackSlot</c>: <c>top</c> is <c>Memory[stackPointer - 1]</c> and <c>below</c>
    ///   the entry under it. Results go back into the same area, and <c>localsBase</c> is where the locals start.
    ///   <c>top</c> and <c>below</c> are read once, up front.</para>
    /// <para>The method runs in three layers, in this order. First an if-chain over the operation category packed
    ///   into <c>op.Flags</c>, which handles loads and stores of arguments and locals, small constants, binary
    ///   arithmetic and compare-and-branch with no cold record; the chain is in the order of
    ///   <c>Planner.PlanLoadArgument</c> to <c>PlanCompareBranch</c>. Second, for every other instruction, the
    ///   instruction's <c>ColdOp</c> is fetched (if it has one). Third, one large switch over the opcode.
    ///   <c>Invoke</c> itself handles <c>ret</c>, <c>br</c> and the <c>brtrue</c> and <c>brfalse</c> forms before
    ///   it gets here.</para>
    /// <para>The case labels of both switches are numeric literals with a trailing comment naming the <c>Il</c>
    ///   constant, because the generated C cannot use a static variable as a case label (<c>Il.Add</c> is <c>static
    ///   int32_t Il_Add = 0x58;</c> there), and the comment is how a label is kept in step with <c>Il</c>. The
    ///   compare-and-branch switch counts from <c>Il.Beq</c> or <c>Il.BeqS</c>.</para>
    /// </remarks>
    private static int ExecuteResolved(ref MethodBody body, ref PlanOp op, int frame)
    {
        int code = op.Code, operand = op.Operand;
        int localsBase = frame + body.ArgCount;
        int stackPointer = frame + op.StackSlot;
        int popCount = op.Flags & Planner.PlanPopMask;
        int category = (op.Flags >> Planner.PlanCategoryShift) & Planner.PlanCategoryMask;
        Value top = popCount > 0 ? Memory[stackPointer - 1] : default(Value), below = popCount > 1 ? Memory[stackPointer - 2] : default(Value);
        if (category == Planner.PlanLoadArgument)
        {
            Memory[stackPointer + 0] = Memory[frame + code - Il.Ldarg_0];
        }
        else if (category == Planner.PlanLoadLocal)
        {
            Memory[stackPointer + 0] = Memory[localsBase + code - Il.Ldloc_0];
        }
        else if (category == Planner.PlanStoreLocal)
        {
            Memory[localsBase + code - Il.Stloc_0] = top;
        }
        else if (category == Planner.PlanSmallConstant)
        {
            Memory[stackPointer + 0] = Arithmetic.MakeInt32(code - Il.LdcI4_0);
        }
        else if (category == Planner.PlanBinary)
        {
            Memory[stackPointer + -2] = Arithmetic.MakeInt32(Arithmetic.Binary(code, Arithmetic.ExpectInt32(below), Arithmetic.ExpectInt32(top)));
        }
        else if (category == Planner.PlanCompareBranch)
        {
            int branchKind = code <= Il.BltUnS ? code - Il.BeqS : code - Il.Beq;
            // Case labels count from Il.Beq (long form) or Il.BeqS (short form); literals, because a C case label cannot be a variable.
            switch (branchKind)
            {
                case 0: // Beq
                    return Objects.ValuesEqual(below, top, 1) ? 1 : 0;
                case 5: // BneUn
                    return Objects.ValuesEqual(below, top, 1) ? 0 : 1;
                case 1: // Bge
                    return Arithmetic.ExpectInt32(below) >= Arithmetic.ExpectInt32(top) ? 1 : 0;
                case 2: // Bgt
                    return Arithmetic.ExpectInt32(below) > Arithmetic.ExpectInt32(top) ? 1 : 0;
                case 3: // Ble
                    return Arithmetic.ExpectInt32(below) <= Arithmetic.ExpectInt32(top) ? 1 : 0;
                case 4: // Blt
                    return Arithmetic.ExpectInt32(below) < Arithmetic.ExpectInt32(top) ? 1 : 0;
                case 6: // BgeUn
                    return (uint)Arithmetic.ExpectInt32(below) >= (uint)Arithmetic.ExpectInt32(top) ? 1 : 0;
                case 7: // BgtUn
                    return (uint)Arithmetic.ExpectInt32(below) > (uint)Arithmetic.ExpectInt32(top) ? 1 : 0;
                case 8: // BleUn
                    return (uint)Arithmetic.ExpectInt32(below) <= (uint)Arithmetic.ExpectInt32(top) ? 1 : 0;
                case 9: // BltUn
                    return (uint)Arithmetic.ExpectInt32(below) < (uint)Arithmetic.ExpectInt32(top) ? 1 : 0;
            }
        }
        else
        {
            ColdOp coldOp = default;
            if (op.ColdIndex != 0)
            {
                coldOp = body.Cold[op.ColdIndex];
            }
            // Case labels are Il opcodes, kept as literals because a C case label cannot be a variable.
            switch (code)
            {
                case 0: // Nop
                case 0x26: // Pop
                    break;
                case 0x0e: // LdargS
                case 0xfe09: // Ldarg
                    Memory[stackPointer + 0] = Memory[frame + operand];
                    break;
                case 0x0f: // LdargaS
                case 0xfe0a: // Ldarga
                    Memory[stackPointer + 0] = Objects.SlotAddress(frame + operand);
                    break;
                case 0x10: // StargS
                case 0xfe0b: // Starg
                    Memory[frame + operand] = top;
                    break;
                case 0x11: // LdlocS
                case 0xfe0c: // Ldloc
                    Memory[stackPointer + 0] = Memory[localsBase + operand];
                    break;
                case 0x12: // LdlocaS
                case 0xfe0d: // Ldloca
                    Memory[stackPointer + 0] = Objects.SlotAddress(localsBase + operand);
                    break;
                case 0x13: // StlocS
                case 0xfe0e: // Stloc
                    Memory[localsBase + operand] = top;
                    break;
                case 0x14: // Ldnull
                    Memory[stackPointer + 0] = default(Value);
                    break;
                case 0x1f: // LdcI4S
                case 0x20: // LdcI4
                    Memory[stackPointer + 0] = Arithmetic.MakeInt32(operand);
                    break;
                case 0x25: // Dup
                    Memory[stackPointer + 0] = top;
                    break;
                case 0x28: // Call
                case 0x6f: // Callvirt
                    {
                        int firstArg = stackPointer - coldOp.ArgCount;
                        Value result = default;
                        if (coldOp.MethodIndex >= 0)
                        {
                            result = Invoke(coldOp.MethodIndex, firstArg);
                        }
                        else if (coldOp.NativeId == Natives.DelegateInvoke)
                        {
                            if (Memory[firstArg + 0].Kind != ValueKind.Delegate)
                            {
                                Runtime.Fail("Invoke on invalid delegate");
                            }

                            int target = (int)Memory[firstArg + 0].Data;
                            if (target < 0 || target >= Loader.MethodCount || Loader.Bodies[target].ArgCount != coldOp.ArgCount - 1)
                            {
                                Runtime.Fail("delegate signature mismatch");
                            }

                            result = Invoke(target, firstArg + 1);
                        }
                        else
                        {
                            result = Natives.Call(coldOp, firstArg);
                        }

                        if (coldOp.ReturnsValue != 0)
                        {
                            Memory[firstArg + 0] = result;
                        }

                        break;
                    }

                case 0x65: // Neg
                    Memory[stackPointer + -1] = Arithmetic.MakeInt32(unchecked(-Arithmetic.ExpectInt32(top)));
                    break;
                case 0x66: // Not
                    Memory[stackPointer + -1] = Arithmetic.MakeInt32(~Arithmetic.ExpectInt32(top));
                    break;
                case 0x67: // ConvI1
                    Memory[stackPointer + -1] = Arithmetic.MakeInt32((sbyte)Arithmetic.ExpectInt32(top));
                    break;
                case 0x68: // ConvI2
                    Memory[stackPointer + -1] = Arithmetic.MakeInt32((short)Arithmetic.ExpectInt32(top));
                    break;
                case 0x69: // ConvI4
                case 0x6d: // ConvU4
                case 0xd3: // ConvI
                case 0xe0: // ConvU
                    Arithmetic.ExpectInt32(top);
                    break;
                case 0xd1: // ConvU2
                    Memory[stackPointer + -1] = Arithmetic.MakeInt32((ushort)Arithmetic.ExpectInt32(top));
                    break;
                case 0xd2: // ConvU1
                    Memory[stackPointer + -1] = Arithmetic.MakeInt32((byte)Arithmetic.ExpectInt32(top));
                    break;
                case 0x71: // Ldobj
                    if (top.Kind != ValueKind.Address)
                    {
                        Runtime.Fail("expected managed address");
                    }

                    Memory[stackPointer + -1] = Objects.ReadAddress(top.Data, 0);
                    break;
                case 0x72: // Ldstr
                    Memory[stackPointer + 0] = coldOp.Literal;
                    break;
                case 0x73: // Newobj
                    if (coldOp.NativeId != Natives.DelegateCtor || top.Kind != ValueKind.FunctionPointer || below.Kind != ValueKind.Null)
                    {
                        Runtime.Fail("unsupported constructor");
                    }

                    top.Kind = ValueKind.Delegate;
                    Memory[stackPointer + -2] = top;
                    break;
                case 0x7b: // Ldfld
                case 0x7c: // Ldflda
                case 0x7d: // Stfld
                    {
                        Value parent = code == Il.Stfld ? below : top;
                        int record = Objects.RequireHandle(Objects.Deref(parent), ValueKind.Record);
                        if (Heap.HandleTable[record].Type != (TypeEncoding.Struct | TypeSystem.FieldOwners[operand]) || TypeSystem.FieldIsStatic[operand] != 0)
                        {
                            Runtime.Fail("invalid field owner");
                        }

                        int field = TypeSystem.FieldSlots[operand];
                        if (code == Il.Ldfld)
                        {
                            Memory[stackPointer + -1] = Heap.HandleTable[record].Values[field];
                        }
                        else
                        {
                            Value address = Objects.FieldAddress(parent, field);
                            if (code == Il.Ldflda)
                            {
                                Memory[stackPointer + -1] = address;
                            }
                            else
                            {
                                if (Arithmetic.IsIntegerType(TypeSystem.FieldTypes[operand]))
                                {
                                    Arithmetic.ExpectInt32(top);
                                }

                                Objects.WriteAddress(address.Data, top, 0);
                            }
                        }

                        break;
                    }

                case 0x7e: // Ldsfld
                    Memory[stackPointer + 0] = Memory[GlobalsBase + operand];
                    break;
                case 0x7f: // Ldsflda
                    Memory[stackPointer + 0] = Objects.SlotAddress(GlobalsBase + operand);
                    break;
                case 0x80: // Stsfld
                    if (Arithmetic.IsIntegerType(TypeSystem.FieldTypes[operand]))
                    {
                        Arithmetic.ExpectInt32(top);
                    }

                    Memory[GlobalsBase + operand] = top;
                    break;
                case 0x81: // Stobj
                    if (below.Kind != ValueKind.Address)
                    {
                        Runtime.Fail("expected managed address");
                    }

                    Objects.WriteAddress(below.Data, top, 0);
                    break;
                case 0x8c: // Box
                    {
                        int boxHandle = Objects.NewObject(coldOp.Type, 1, 0);
                        Heap.HandleTable[boxHandle].Kind = ValueKind.Box;
                        Heap.HandleTable[boxHandle].Values[0] = top;
                        Memory[stackPointer + -1] = Objects.MakeReference(ValueKind.Box, boxHandle);
                        break;
                    }

                case 0x8d: // Newarr
                    Memory[stackPointer + -1] = Objects.NewArray(coldOp.Type, Arithmetic.ExpectInt32(top));
                    break;
                case 0x8e: // Ldlen
                    Memory[stackPointer + -1] = Arithmetic.MakeInt32(Heap.HandleTable[Objects.RequireHandle(top, ValueKind.Array)].Count);
                    break;
                case 0x8f: // Ldelema
                    Memory[stackPointer + -2] = Objects.ArrayAddress(below, Arithmetic.ExpectInt32(top));
                    break;
                case 0x90: // LdelemI1
                case 0x91: // LdelemU1
                case 0x92: // LdelemI2
                case 0x93: // LdelemU2
                case 0x94: // LdelemI4
                case 0x95: // LdelemU4
                case 0x9a: // LdelemRef
                case 0xa3: // Ldelem
                    {
                        int index = Arithmetic.ExpectInt32(top);
                        int array = Objects.RequireArrayHandleInBounds(below, index);
                        Value value = Heap.HandleTable[array].Values[index];
                        if (code == Il.LdelemI1)
                        {
                            value = Arithmetic.MakeInt32((sbyte)Arithmetic.ExpectInt32(value));
                        }

                        if (code == Il.LdelemU1)
                        {
                            value = Arithmetic.MakeInt32((byte)Arithmetic.ExpectInt32(value));
                        }

                        if (code == Il.LdelemI2)
                        {
                            value = Arithmetic.MakeInt32((short)Arithmetic.ExpectInt32(value));
                        }

                        if (code == Il.LdelemU2)
                        {
                            value = Arithmetic.MakeInt32((ushort)Arithmetic.ExpectInt32(value));
                        }

                        Memory[stackPointer + -2] = value;
                        break;
                    }

                case 0x9b: // StelemI
                case 0x9c: // StelemI1
                case 0x9d: // StelemI2
                case 0x9e: // StelemI4
                case 0xa2: // StelemRef
                case 0xa4: // Stelem
                    if (code == Il.StelemI1)
                    {
                        top = Arithmetic.MakeInt32((byte)Arithmetic.ExpectInt32(top));
                    }

                    if (code == Il.StelemI2)
                    {
                        top = Arithmetic.MakeInt32((ushort)Arithmetic.ExpectInt32(top));
                    }

                    int storeIndex = Arithmetic.ExpectInt32(below);
                    int storeArray = Objects.RequireArrayHandleInBounds(Memory[stackPointer + -3], storeIndex);
                    Heap.HandleTable[storeArray].Values[storeIndex] = top;
                    break;
                case 0xa5: // UnboxAny
                    {
                        int boxHandle = Objects.RequireHandle(top, ValueKind.Box);
                        if (Heap.HandleTable[boxHandle].Type != coldOp.Type)
                        {
                            Runtime.Fail("unbox.any type mismatch");
                        }

                        Memory[stackPointer + -1] = Heap.HandleTable[boxHandle].Values[0];
                        break;
                    }

                case 0xfe01: // Ceq
                    Memory[stackPointer + -2] = Arithmetic.MakeInt32(Objects.ValuesEqual(below, top, 1) ? 1 : 0);
                    break;
                case 0xfe06: // Ldftn
                    {
                        Value pointer = default;
                        pointer.Kind = ValueKind.FunctionPointer;
                        pointer.Data = coldOp.MethodIndex;
                        Memory[stackPointer + 0] = pointer;
                        break;
                    }

                case 0xfe15: // Initobj
                    if (top.Kind != ValueKind.Address)
                    {
                        Runtime.Fail("expected managed address");
                    }

                    Objects.WriteAddress(top.Data, Objects.ZeroValue(coldOp.Type, 0), 0);
                    break;
                default:
                    Runtime.Fail("unhandled IL operation");
                    break;
            }
        }

        return 0;
    }

    /// <summary>
    /// Starts a call: counts it, fails with 'call depth limit exceeded' past <c>MaxCallDepth</c> or with 'value
    /// stack exhausted' if the frame does not fit in the value stack, checks that every integer argument is an
    /// <c>Int32</c>, and sets each local to its default. JIT code reaches this through the Enter helper, from each
    /// method's prologue.
    /// </summary>
    internal static void Enter(int methodId, int frame)
    {
        if (++_callDepth > MaxCallDepth)
        {
            Runtime.Fail("call depth limit exceeded");
        }

        Calls++;
        ref MethodBody body = ref Loader.Bodies[methodId];
        int frameSlots = body.ArgCount + body.LocalCount + body.MaxStack;
        if (frame < 0 || frame + frameSlots > Limits.ValueStackSlots)
        {
            Runtime.Fail("value stack exhausted");
        }

        for (int i = 0; i < body.ArgCount; i++)
        {
            if (Arithmetic.IsIntegerType(body.ArgTypes[i]))
            {
                Arithmetic.ExpectInt32(Memory[frame + i]);
            }
        }

        for (int i = 0; i < body.LocalCount; i++)
        {
            Memory[frame + body.ArgCount + i] = body.LocalDefaults[i];
        }
    }

    /// <summary>
    /// Ends a call: clears every slot of the frame to null, so a finished call keeps no object alive for the
    /// collector, and drops the call depth. JIT code reaches this through the Leave helper.
    /// </summary>
    internal static void Leave(int methodId, int frame)
    {
        ref MethodBody body = ref Loader.Bodies[methodId];
        int frameSlots = body.ArgCount + body.LocalCount + body.MaxStack;
        for (int i = 0; i < frameSlots; i++)
        {
            Memory[frame + i] = default(Value);
        }

        _callDepth--;
    }

    /// <summary>
    /// Adds to <c>Steps</c> and fails with 'instruction budget exceeded' beyond <c>Limits.InstructionBudget</c>.
    /// The interpreter charges one per instruction.
    /// </summary>
    internal static void Charge(int instructionCount)
    {
        Steps += instructionCount;
        if (Steps > Limits.InstructionBudget)
        {
            Runtime.Fail("instruction budget exceeded");
        }
    }

    /// <summary>
    /// Runs method <c>methodId</c>, whose arguments are already in <c>Memory</c> at <c>frame</c>, and returns its
    /// result (<c>default(Value)</c> for a void method). With the JIT enabled it hands the call to the compiled
    /// code through <c>Host.Call</c>.
    /// </summary>
    /// <remarks>
    /// <para>Otherwise it interprets: <c>Enter</c>, then a loop over <c>body.Plan</c> that charges each
    ///   instruction, handles <c>ret</c>, <c>br</c> and the <c>brtrue</c>/<c>brfalse</c> forms inline, and for any
    ///   other instruction collects if due and calls <c>ExecuteResolved</c>, which may ask for a branch. <c>ret</c>
    ///   calls <c>Leave</c>.</para>
    /// </remarks>
    internal static Value Invoke(int methodId, int frame)
    {
        if (methodId < 0 || methodId >= Loader.MethodCount)
        {
            Runtime.Fail("method index");
        }

        ref MethodBody body = ref Loader.Bodies[methodId];
        if (Jit.Enabled != 0)
        {
            Value result = default;
            Host.Call(methodId, frame, ref result);
            return result;
        }

        Enter(methodId, frame);
        int pc = 0;
        while (true)
        {
            ref PlanOp op = ref body.Plan[pc];
            int code = op.Code;
            Charge(1);
            if (code == Il.Ret)
            {
                Value result = body.ReturnType == ElementType.Void ? default(Value) : Memory[frame + op.StackSlot - 1];
                Leave(methodId, frame);
                return result;
            }

            if (code == Il.BrS || code == Il.Br)
            {
                pc = op.Operand;
                continue;
            }

            if (code == Il.BrfalseS || code == Il.Brfalse || code == Il.BrtrueS || code == Il.Brtrue)
            {
                bool truth = Arithmetic.IsTruthy(Memory[frame + op.StackSlot - 1]);
                if (truth == (code == Il.BrtrueS || code == Il.Brtrue))
                {
                    pc = op.Operand;
                    continue;
                }
            }
            else
            {
                Heap.CollectIfNeeded();
                if (ExecuteResolved(ref body, ref op, frame) != 0)
                {
                    pc = op.Operand;
                    continue;
                }
            }

            pc++;
        }
    }
}
