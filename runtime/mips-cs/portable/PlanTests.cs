using System;

namespace ProLang.Portable;

// Desktop-only tests of verifier-to-plan conversion.
public static partial class VM
{
    static void TestPlan()
    {
        for (int code = 0; code <= 0xffff; code++)
        {
            if (HasColdMetadata(code) && PlanCategory(code) != PlanOther)
            {
                throw new Exception("cold metadata overlaps a hot plan category");
            }
        }
        bodies = new Body[1];
        bodies[0].nargs = 1;
        bodies[0].nlocals = 1;
        bodies[0].returns = 8;
        bodies[0].maxstack = 1;
        bodies[0].count = 7;
        bodies[0].ops = new Op[7];
        int[] codes = [0x02, 0x2c, 0x17, 0x2a, 0x72, 0x2a, 0x17];
        int[] offsets = [0, 1, 3, 4, 5, 10, 11];
        for (int i = 0; i < codes.Length; i++)
        {
            bodies[0].ops[i].code = codes[i];
            bodies[0].ops[i].offset = offsets[i];
            bodies[0].ops[i].depth = -1;
            bodies[0].ops[i].method = -1;
        }
        bodies[0].ops[1].operand = 4;
        bodies[0].ops[4].literal = Integer(42);
        Verify(0);
        Body body = bodies[0];
        if (body.ops != null || body.plan.Length != 7 || body.cold.Length != 2 ||
            body.plan[1].operand != 4 || body.plan[4].offset != 5 || body.plan[6].stack != -1 ||
            body.plan[0].stack != 2 || body.plan[1].stack != 3 || body.plan[4].stack != 2 ||
            (body.plan[0].flags & PlanBlockStart) == 0 || (body.plan[2].flags & PlanBlockStart) == 0 ||
            (body.plan[4].flags & PlanBlockStart) == 0 || (body.plan[1].flags & PlanBlockStart) != 0 ||
            (body.plan[1].flags & PlanPopMask) != 1 ||
            ((body.plan[0].flags >> PlanPushShift) & PlanPushMask) != 1 ||
            body.cold[body.plan[4].cold].literal.data != 42 ||
            ((body.plan[0].flags >> PlanCategoryShift) & PlanCategoryMask) != PlanLoadArgument ||
            HelperEffects(4) != NoReturn || (HelperEffects(99) & NeedsPublishedFrame) != NeedsPublishedFrame)
        {
            throw new Exception("verified execution plan failed");
        }
        bodies = new Body[1];
        bodies[0].returns = 8;
        bodies[0].maxstack = 1;
        bodies[0].count = 2;
        bodies[0].ops = new Op[2];
        bodies[0].ops[0].code = 0x28;
        bodies[0].ops[0].returns = 1;
        bodies[0].ops[0].depth = -1;
        bodies[0].ops[1].code = 0x2a;
        bodies[0].ops[1].depth = -1;
        Verify(0);
        int flags = bodies[0].plan[0].flags;
        FoldConstantCall(ref bodies[0].plan[0], 42);
        if (bodies[0].plan[0].flags != flags || (flags & PlanPopMask) != 0 ||
            ((flags >> PlanPushShift) & PlanPushMask) != 1 ||
            ((flags >> PlanCategoryShift) & PlanCategoryMask) != PlanOther ||
            PlanCategory(0x20) != PlanOther || bodies[0].plan[0].operand != 42)
        {
            throw new Exception("constant call folding changed plan effects");
        }

        bodies = new Body[1];
        bodies[0].returns = 8;
        bodies[0].maxstack = 1;
        bodies[0].count = 1;
        bodies[0].ops = new Op[1];
        bodies[0].ops[0].code = 0x2a;
        bodies[0].ops[0].depth = -1;
        try
        {
            Verify(0);
            throw new Exception("invalid stack was accepted");
        }
        catch (InvalidOperationException e) when (e.Message == "invalid evaluation stack")
        {
        }

        steps = 0;
        Charge(10000000);
        try
        {
            Charge(1);
            throw new Exception("instruction budget was exceeded without an error");
        }
        catch (InvalidOperationException e) when (e.Message == "instruction budget exceeded")
        {
        }
        steps = 0;
        Console.WriteLine("PASS verified execution plan");
    }
}
