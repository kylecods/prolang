namespace ProLang.Portable;

// Flags pack the verifier's pop/push counts and the JIT's existing block boundaries.
public static partial class VM
{
    const int PlanPopMask = 127;
    const int PlanPushShift = 7;
    const int PlanPushMask = 3;
    const int PlanBlockStart = 512;
    const int PlanCategoryShift = 10;
    const int PlanCategoryMask = 7;
    const int PlanOther = 0;
    const int PlanLoadArgument = 1;
    const int PlanLoadLocal = 2;
    const int PlanStoreLocal = 3;
    const int PlanSmallConstant = 4;
    const int PlanBinary = 5;
    const int PlanCompareBranch = 6;

    static int PlanCategory(int code)
    {
        if (code >= 2 && code <= 5)
        {
            return PlanLoadArgument;
        }
        if (code >= 6 && code <= 9)
        {
            return PlanLoadLocal;
        }
        if (code >= 10 && code <= 13)
        {
            return PlanStoreLocal;
        }
        if (code >= 0x15 && code <= 0x1e)
        {
            return PlanSmallConstant;
        }
        if ((code >= 0x58 && code <= 0x64) || (code >= 0xfe02 && code <= 0xfe05))
        {
            return PlanBinary;
        }
        if ((code >= 0x2e && code <= 0x37) || (code >= 0x3b && code <= 0x44))
        {
            return PlanCompareBranch;
        }
        return PlanOther;
    }

    static bool HasColdMetadata(int code)
    {
        return code == 0x28 || code == 0x6f || code == 0x73 || code == 0xfe06 ||
            code == 0x72 || code == 0x71 || code == 0x81 || code == 0x8c || code == 0x8d ||
            code == 0x8f || code == 0xa3 || code == 0xa4 || code == 0xa5 || code == 0xfe15;
    }

    static void BuildPlan(int method)
    {
        ref Body body = ref bodies[method];
        int coldCount = 1; // Index zero is the empty entry.
        for (int i = 0; i < body.count; i++)
        {
            if (HasColdMetadata(body.ops[i].code))
            {
                coldCount++;
            }
        }

        body.plan = new PlanOp[body.count];
        body.cold = new ColdOp[coldCount];
        int nextCold = 1;
        for (int i = 0; i < body.count; i++)
        {
            Op source = body.ops[i];
            ref PlanOp target = ref body.plan[i];
            target.code = source.code;
            target.operand = source.operand;
            target.stack = source.depth < 0 ? -1 : body.nargs + body.nlocals + source.depth;
            target.offset = source.offset;
            target.flags = Pop(source, body.returns) | (Push(source) << PlanPushShift) |
                (PlanCategory(source.code) << PlanCategoryShift);
            if (HasColdMetadata(source.code))
            {
                target.cold = nextCold++;
                ref ColdOp extra = ref body.cold[target.cold];
                extra.method = source.method;
                extra.native = source.native;
                extra.count = source.count;
                extra.returns = source.returns;
                extra.type = source.type;
                extra.resultType = source.resultType;
                extra.literal = source.literal;
            }
        }

        body.plan[0].flags |= PlanBlockStart;
        for (int i = 0; i < body.count; i++)
        {
            int code = body.plan[i].code;
            if (code >= 0x2b && code <= 0x44)
            {
                body.plan[body.plan[i].operand].flags |= PlanBlockStart;
                if (i + 1 < body.count)
                {
                    body.plan[i + 1].flags |= PlanBlockStart;
                }
            }
        }

        Host.ReleaseOps(body.ops);
        body.ops = null!;
    }
}
