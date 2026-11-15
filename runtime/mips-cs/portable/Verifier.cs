namespace ProLang.Portable;

/// <summary>Checks a decoded method and works out its stack shape, then hands it to <c>Planner</c>.</summary>
/// <remarks>
/// <para>Verification is the structural check the engines rely on: every reachable instruction gets one fixed
///   evaluation stack depth, so the planner and the JIT can address operands as frame slots. It does not check
///   types; the engines check kinds at run time.</para>
/// </remarks>
internal static class Verifier
{
    /// <summary>
    /// How many evaluation stack entries the instruction pops. <c>returnType</c> is the method's return type code,
    /// which decides whether <c>ret</c> pops a value.
    /// </summary>
    internal static int PopCount(DecodedOp op, int returnType)
    {
        int code = op.Code;
        if (code == Il.Nop || (code >= Il.Ldarg_0 && code <= Il.Ldloc_3) || code == Il.LdargS || code == Il.LdargaS || code == Il.LdlocS || code == Il.LdlocaS || (code >= Il.Ldnull && code <= Il.LdcI4) || code == Il.BrS || code == Il.Br || code == Il.Ldstr || code == Il.Ldsfld || code == Il.Ldsflda || code == Il.Ldftn || code == Il.Ldarg || code == Il.Ldarga || code == Il.Ldloc || code == Il.Ldloca)
        {
            return 0;
        }

        if (code == Il.Call || code == Il.Callvirt)
        {
            return op.ArgCount;
        }

        if (code == Il.Newobj)
        {
            return op.ArgCount - 1;
        }

        if (code == Il.Ret)
        {
            return returnType == ElementType.Void ? 0 : 1;
        }

        if (code == Il.StelemI || code == Il.StelemI1 || code == Il.StelemI2 || code == Il.StelemI4 || code == Il.StelemRef || code == Il.Stelem)
        {
            return 3;
        }

        if ((code >= Il.BeqS && code <= Il.BltUnS) || (code >= Il.Beq && code <= Il.BltUn) || (code >= Il.Add && code <= Il.ShrUn) || code == Il.Stfld || code == Il.Stobj || code == Il.Ldelema || (code >= Il.LdelemI1 && code <= Il.LdelemU4) || code == Il.LdelemRef || code == Il.Ldelem || (code >= Il.Ceq && code <= Il.CltUn))
        {
            return 2;
        }

        return 1;
    }

    /// <summary>How many entries the instruction pushes. <c>dup</c> pushes 2, having popped 1.</summary>
    internal static int PushCount(DecodedOp op)
    {
        int code = op.Code;
        if (code == Il.Nop || (code >= Il.Stloc_0 && code <= Il.Stloc_3) || code == Il.StargS || code == Il.StlocS || code == Il.Pop || (code >= Il.Ret && code <= Il.BltUn) || code == Il.Stfld || code == Il.Stsfld || code == Il.Stobj || (code >= Il.StelemI && code <= Il.StelemI4) || code == Il.StelemRef || code == Il.Stelem || code == Il.Starg || code == Il.Stloc || code == Il.Initobj)
        {
            return 0;
        }

        if (code == Il.Call || code == Il.Callvirt)
        {
            return op.ReturnsValue;
        }

        if (code == Il.Dup)
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// Records that control reaches instruction <c>pc</c> with the stack at <c>depth</c>. The first arrival fixes
    /// the depth and queues the instruction; a later arrival at a different depth fails with 'inconsistent stack at
    /// branch'. Falling off either end of the method fails.
    /// </summary>
    private static void Visit(MethodBody body, int pc, int depth, int[] queue, ref int tail)
    {
        if (pc < 0 || pc >= body.OpCount)
        {
            Runtime.Fail("control falls outside method");
        }

        if (body.Ops[pc].StackDepth == -1)
        {
            body.Ops[pc].StackDepth = depth;
            queue[tail++] = pc;
        }
        else if (body.Ops[pc].StackDepth != depth)
        {
            Runtime.Fail("inconsistent stack at branch");
        }
    }

    /// <summary>Verifies one decoded method and builds its plan.</summary>
    /// <remarks>
    /// <para>First every local and argument index is range-checked. Then a worklist walk from instruction 0 gives
    ///   each reachable instruction its entry stack depth (<c>DecodedOp.StackDepth</c>), failing on underflow, on a
    ///   depth above <c>MaxStack</c>, on a depth that differs between two paths, and on a <c>ret</c> that leaves
    ///   entries behind. Finally <c>Planner.BuildPlan</c> replaces the decoded records, and the temporary queue is
    ///   released.</para>
    /// </remarks>
    internal static void Verify(int methodId)
    {
        ref MethodBody body = ref Loader.Bodies[methodId];
        int[] queue = new int[body.OpCount];
        int head = 0, tail = 0;
        Visit(body, 0, 0, queue, ref tail);
        for (int i = 0; i < body.OpCount; i++)
        {
            ref DecodedOp op = ref body.Ops[i];
            int code = op.Code, slot = op.Operand;
            if (code >= Il.Ldloc_0 && code <= Il.Stloc_3)
            {
                slot = (code - Il.Ldloc_0) & 3;
            }

            if (((code >= Il.Ldloc_0 && code <= Il.Stloc_3) || code == Il.LdlocS || code == Il.LdlocaS || code == Il.StlocS || code == Il.Ldloc || code == Il.Ldloca || code == Il.Stloc) && (slot < 0 || slot >= body.LocalCount))
            {
                Runtime.Fail("local index");
            }

            slot = code >= Il.Ldarg_0 && code <= Il.Ldarg_3 ? code - Il.Ldarg_0 : op.Operand;
            if (((code >= Il.Ldarg_0 && code <= Il.Ldarg_3) || code == Il.LdargS || code == Il.LdargaS || code == Il.StargS || code == Il.Ldarg || code == Il.Ldarga || code == Il.Starg) && (slot < 0 || slot >= body.ArgCount))
            {
                Runtime.Fail("argument index");
            }
        }

        while (head < tail)
        {
            int pc = queue[head++];
            ref DecodedOp op = ref body.Ops[pc];
            int popCount = PopCount(op, body.ReturnType), nextDepth = op.StackDepth - popCount + PushCount(op);
            if (op.StackDepth < popCount || nextDepth > body.MaxStack)
            {
                Runtime.Fail("invalid evaluation stack");
            }

            if (op.Code == Il.Ret)
            {
                if (nextDepth != 0)
                {
                    Runtime.Fail("invalid return stack");
                }

                continue;
            }

            if (op.Code >= Il.BrS && op.Code <= Il.BltUn)
            {
                Visit(body, op.Operand, nextDepth, queue, ref tail);
            }

            if (op.Code != Il.BrS && op.Code != Il.Br)
            {
                Visit(body, pc + 1, nextDepth, queue, ref tail);
            }
        }
        Host.ReleaseInts(queue);
        Planner.BuildPlan(methodId);
    }
}
