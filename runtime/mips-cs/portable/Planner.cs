namespace ProLang.Portable;

/// <summary>
/// Builds each method's execution plan from its verified decoded instructions: a <c>PlanOp</c> per instruction, and
/// a sparse table of <c>ColdOp</c> records for the instructions that carry call, type or literal data.
/// </summary>
/// <remarks>
/// <para><c>PlanOp.Flags</c> packs the verifier's pop and push counts, the JIT's basic block boundaries and an
///   operation category. The constants below give the layout: the pop count is bits 0 to 6, the push count bits 7
///   and 8, <c>PlanBlockStart</c> is bit 9 and the category is bits 10 to 12. The categories are the instructions
///   <c>Interpreter.ExecuteResolved</c> handles before it looks at a cold record.</para>
/// </remarks>
internal static class Planner
{
    internal const int PlanPopMask = 127;
    internal const int PlanPushShift = 7;
    internal const int PlanPushMask = 3;
    internal const int PlanBlockStart = 512;
    internal const int PlanCategoryShift = 10;
    internal const int PlanCategoryMask = 7;
    internal const int PlanOther = 0;
    internal const int PlanLoadArgument = 1;
    internal const int PlanLoadLocal = 2;
    internal const int PlanStoreLocal = 3;
    internal const int PlanSmallConstant = 4;
    internal const int PlanBinary = 5;
    internal const int PlanCompareBranch = 6;

    /// <summary>
    /// The <c>Plan*</c> category of an IL opcode (a load or store of an argument or local, a small constant, an
    /// arithmetic or comparison operation, or a compare-and-branch), or <c>PlanOther</c>. An opcode with cold
    /// metadata must be <c>PlanOther</c>, because the interpreter's category fast paths never read a cold record;
    /// <c>PlanTests</c> checks that.
    /// </summary>
    internal static int PlanCategory(int code)
    {
        if (code >= Il.Ldarg_0 && code <= Il.Ldarg_3)
        {
            return PlanLoadArgument;
        }
        if (code >= Il.Ldloc_0 && code <= Il.Ldloc_3)
        {
            return PlanLoadLocal;
        }
        if (code >= Il.Stloc_0 && code <= Il.Stloc_3)
        {
            return PlanStoreLocal;
        }
        if (code >= Il.LdcI4_M1 && code <= Il.LdcI4_8)
        {
            return PlanSmallConstant;
        }
        if ((code >= Il.Add && code <= Il.ShrUn) || (code >= Il.Cgt && code <= Il.CltUn))
        {
            return PlanBinary;
        }
        if ((code >= Il.BeqS && code <= Il.BltUnS) || (code >= Il.Beq && code <= Il.BltUn))
        {
            return PlanCompareBranch;
        }
        return PlanOther;
    }

    /// <summary>
    /// True for the opcodes whose <c>DecodedOp</c> carries call, type or literal data that the plan keeps in a
    /// <c>ColdOp</c>: call, callvirt, newobj, ldftn, ldstr, ldobj, stobj, box, newarr, ldelema, ldelem, stelem,
    /// unbox.any and initobj.
    /// </summary>
    internal static bool HasColdMetadata(int code)
    {
        return code == Il.Call || code == Il.Callvirt || code == Il.Newobj || code == Il.Ldftn ||
            code == Il.Ldstr || code == Il.Ldobj || code == Il.Stobj || code == Il.Box || code == Il.Newarr ||
            code == Il.Ldelema || code == Il.Ldelem || code == Il.Stelem || code == Il.UnboxAny || code == Il.Initobj;
    }

    /// <summary>
    /// Replaces <c>body.Ops</c> with an exact-length <c>body.Plan</c> and a <c>body.Cold</c> table, then releases
    /// the decoded records.
    /// </summary>
    /// <remarks>
    /// <para><c>PlanOp.StackSlot</c> is made absolute here (<c>ArgCount + LocalCount + StackDepth</c>). Entry 0 of
    ///   <c>Cold</c> is an empty record that every instruction without cold data points at. The first instruction,
    ///   every branch target and every instruction after a branch is marked <c>PlanBlockStart</c>.</para>
    /// </remarks>
    internal static void BuildPlan(int methodId)
    {
        ref MethodBody body = ref Loader.Bodies[methodId];
        int coldCount = 1; // Index zero is the empty entry.
        for (int i = 0; i < body.OpCount; i++)
        {
            if (HasColdMetadata(body.Ops[i].Code))
            {
                coldCount++;
            }
        }

        body.Plan = new PlanOp[body.OpCount];
        body.Cold = new ColdOp[coldCount];
        int nextCold = 1;
        for (int i = 0; i < body.OpCount; i++)
        {
            DecodedOp source = body.Ops[i];
            ref PlanOp target = ref body.Plan[i];
            target.Code = source.Code;
            target.Operand = source.Operand;
            target.StackSlot = source.StackDepth < 0 ? -1 : body.ArgCount + body.LocalCount + source.StackDepth;
            target.Offset = source.Offset;
            target.Flags = Verifier.PopCount(source, body.ReturnType) | (Verifier.PushCount(source) << PlanPushShift) |
                (PlanCategory(source.Code) << PlanCategoryShift);
            if (HasColdMetadata(source.Code))
            {
                target.ColdIndex = nextCold++;
                ref ColdOp coldOp = ref body.Cold[target.ColdIndex];
                coldOp.MethodIndex = source.MethodIndex;
                coldOp.NativeId = source.NativeId;
                coldOp.ArgCount = source.ArgCount;
                coldOp.ReturnsValue = source.ReturnsValue;
                coldOp.Type = source.Type;
                coldOp.ResultType = source.ResultType;
                coldOp.Literal = source.Literal;
            }
        }

        body.Plan[0].Flags |= PlanBlockStart;
        for (int i = 0; i < body.OpCount; i++)
        {
            int code = body.Plan[i].Code;
            if (code >= Il.BrS && code <= Il.BltUn)
            {
                body.Plan[body.Plan[i].Operand].Flags |= PlanBlockStart;
                if (i + 1 < body.OpCount)
                {
                    body.Plan[i + 1].Flags |= PlanBlockStart;
                }
            }
        }

        Host.ReleaseOps(body.Ops);
        body.Ops = null!;
    }
}
