using System;

namespace ProLang.Portable;

/// <summary>Desktop-only tests of verifier-to-plan conversion.</summary>
internal static class PlanTests
{
    /// <summary>
    /// Checks that no opcode is both hot and cold; that verifying a small method gives the expected plan (absolute
    /// stack slots, pop and push counts, categories, block starts and cold records) and the expected helper
    /// effects; that folding a constant call keeps its plan flags; that an invalid evaluation stack is refused; and
    /// that the instruction budget is enforced.
    /// </summary>
    internal static void TestPlan()
    {
        for (int code = 0; code <= 0xffff; code++)
        {
            if (Planner.HasColdMetadata(code) && Planner.PlanCategory(code) != Planner.PlanOther)
            {
                throw new Exception("cold metadata overlaps a hot plan category");
            }
        }
        Loader.Bodies = new MethodBody[1];
        Loader.Bodies[0].ArgCount = 1;
        Loader.Bodies[0].LocalCount = 1;
        Loader.Bodies[0].ReturnType = 8;
        Loader.Bodies[0].MaxStack = 1;
        Loader.Bodies[0].OpCount = 7;
        Loader.Bodies[0].Ops = new DecodedOp[7];
        int[] codes = [0x02, 0x2c, 0x17, 0x2a, 0x72, 0x2a, 0x17];
        int[] offsets = [0, 1, 3, 4, 5, 10, 11];
        for (int i = 0; i < codes.Length; i++)
        {
            Loader.Bodies[0].Ops[i].Code = codes[i];
            Loader.Bodies[0].Ops[i].Offset = offsets[i];
            Loader.Bodies[0].Ops[i].StackDepth = -1;
            Loader.Bodies[0].Ops[i].MethodIndex = -1;
        }
        Loader.Bodies[0].Ops[1].Operand = 4;
        Loader.Bodies[0].Ops[4].Literal = Arithmetic.MakeInt32(42);
        Verifier.Verify(0);
        MethodBody body = Loader.Bodies[0];
        if (body.Ops != null || body.Plan.Length != 7 || body.Cold.Length != 2 ||
            body.Plan[1].Operand != 4 || body.Plan[4].Offset != 5 || body.Plan[6].StackSlot != -1 ||
            body.Plan[0].StackSlot != 2 || body.Plan[1].StackSlot != 3 || body.Plan[4].StackSlot != 2 ||
            (body.Plan[0].Flags & Planner.PlanBlockStart) == 0 || (body.Plan[2].Flags & Planner.PlanBlockStart) == 0 ||
            (body.Plan[4].Flags & Planner.PlanBlockStart) == 0 || (body.Plan[1].Flags & Planner.PlanBlockStart) != 0 ||
            (body.Plan[1].Flags & Planner.PlanPopMask) != 1 ||
            ((body.Plan[0].Flags >> Planner.PlanPushShift) & Planner.PlanPushMask) != 1 ||
            body.Cold[body.Plan[4].ColdIndex].Literal.Data != 42 ||
            ((body.Plan[0].Flags >> Planner.PlanCategoryShift) & Planner.PlanCategoryMask) != Planner.PlanLoadArgument ||
            Jit.HelperEffects(4) != Jit.NoReturn || (Jit.HelperEffects(99) & Jit.NeedsPublishedFrame) != Jit.NeedsPublishedFrame)
        {
            throw new Exception("verified execution plan failed");
        }
        Loader.Bodies = new MethodBody[1];
        Loader.Bodies[0].ReturnType = 8;
        Loader.Bodies[0].MaxStack = 1;
        Loader.Bodies[0].OpCount = 2;
        Loader.Bodies[0].Ops = new DecodedOp[2];
        Loader.Bodies[0].Ops[0].Code = 0x28;
        Loader.Bodies[0].Ops[0].ReturnsValue = 1;
        Loader.Bodies[0].Ops[0].StackDepth = -1;
        Loader.Bodies[0].Ops[1].Code = 0x2a;
        Loader.Bodies[0].Ops[1].StackDepth = -1;
        Verifier.Verify(0);
        int flags = Loader.Bodies[0].Plan[0].Flags;
        Jit.FoldConstantCall(ref Loader.Bodies[0].Plan[0], 42);
        if (Loader.Bodies[0].Plan[0].Flags != flags || (flags & Planner.PlanPopMask) != 0 ||
            ((flags >> Planner.PlanPushShift) & Planner.PlanPushMask) != 1 ||
            ((flags >> Planner.PlanCategoryShift) & Planner.PlanCategoryMask) != Planner.PlanOther ||
            Planner.PlanCategory(0x20) != Planner.PlanOther || Loader.Bodies[0].Plan[0].Operand != 42)
        {
            throw new Exception("constant call folding changed plan effects");
        }

        Loader.Bodies = new MethodBody[1];
        Loader.Bodies[0].ReturnType = 8;
        Loader.Bodies[0].MaxStack = 1;
        Loader.Bodies[0].OpCount = 1;
        Loader.Bodies[0].Ops = new DecodedOp[1];
        Loader.Bodies[0].Ops[0].Code = 0x2a;
        Loader.Bodies[0].Ops[0].StackDepth = -1;
        try
        {
            Verifier.Verify(0);
            throw new Exception("invalid stack was accepted");
        }
        catch (InvalidOperationException e) when (e.Message == "invalid evaluation stack")
        {
        }

        Interpreter.Steps = 0;
        Interpreter.Charge(10000000);
        try
        {
            Interpreter.Charge(1);
            throw new Exception("instruction budget was exceeded without an error");
        }
        catch (InvalidOperationException e) when (e.Message == "instruction budget exceeded")
        {
        }
        Interpreter.Steps = 0;
        Console.WriteLine("PASS verified execution plan");
    }
}
