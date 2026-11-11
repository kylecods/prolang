namespace ProLang.Portable;

public static partial class VM
{
    static uint[] machine = null!;
    static int machineCount, machineCapacity, labelCount, fixCount;
    static int[] labels = null!;
    static int[] fixAt = null!;
    static int[] fixTo = null!;
    // Six dirty integer values in callee-saved $s2..$s7. Their tags are implicit.
    // Frame-observing helpers and control-flow joins require the cache to be flushed.
    static int[] cachedSlots = null!;
    static int cacheVictim;
    static void Emit(uint word)
    {
        if (machineCount >= machineCapacity)
        {
            Bad("JIT code buffer limit");
        }

        machine[machineCount++] = word;
    }

    static void Li(int reg, int value)
    {
        if (value >= -32768 && value <= 32767)
        {
            Emit(I(9, 0, reg, value));
        }
        else
        {
            Emit(I(15, 0, reg, (int)((uint)value >> 16)));
            if ((value & 65535) != 0)
            {
                Emit(I(13, reg, reg, value));
            }
        }
    }

    static int Label()
    {
        if (labelCount >= machineCapacity)
        {
            Bad("JIT label limit");
        }

        labels[labelCount] = -1;
        return labelCount++;
    }

    static void Bind(int label)
    {
        labels[label] = machineCount;
    }

    static void Branch(int op, int rs, int rt, int target)
    {
        if (fixCount >= machineCapacity)
        {
            Bad("JIT fixup limit");
        }

        fixAt[fixCount] = machineCount;
        fixTo[fixCount++] = target;
        Emit(I(op, rs, rt, 0));
        Emit(0);
    }

    static void Jump(int target)
    {
        Branch(4, 0, 0, target);
    }

    static void FlushCache()
    {
        for (int i = 0; i < 6; i++)
        {
            if (cachedSlots[i] >= 0)
            {
                FlushOne(i);
            }
        }
    }

    static void FlushOne(int i)
    {
        Li(24, 1);
        Emit(I(43, 16, 24, cachedSlots[i] * 8));
        Emit(I(43, 16, 18 + i, cachedSlots[i] * 8 + 4));
        cachedSlots[i] = -1;
    }

    static void Lw(int reg, int slot, int part)
    {
        for (int i = 0; i < 6; i++)
        {
            if (slot == cachedSlots[i])
            {
                if (part == 0)
                {
                    Li(reg, 1);
                }
                else
                {
                    Move(reg, 18 + i);
                }

                return;
            }
        }

        Emit(I(35, 16, reg, slot * 8 + part));
    }

    static void Sw(int reg, int slot, int part)
    {
        for (int i = 0; i < 6; i++)
        {
            if (slot == cachedSlots[i])
            {
                FlushOne(i);
            }
        }

        Emit(I(43, 16, reg, slot * 8 + part));
    }

    static void Move(int dest, int src)
    {
        Emit(R(src, 0, dest, 0, 33));
    }

    const int ReadsFrame = 1;
    const int WritesFrame = 2;
    const int Allocates = 4;
    const int Collects = 8;
    const int NoReturn = 16;
    // A numeric initializer keeps this constant valid in the translated C output.
    const int NeedsPublishedFrame = 15;

    // Unknown helpers are conservative. The PSP ABI preserves $s2..$s7 across
    // every native helper call. A non-returning trap does not rejoin the fast path.
    static int HelperEffects(int number)
    {
        if (number == 1)
        {
            return ReadsFrame | WritesFrame;
        }
        if (number == 2)
        {
            return WritesFrame;
        }
        if (number == 3)
        {
            return ReadsFrame | WritesFrame | Allocates | Collects;
        }
        if (number == 4)
        {
            return NoReturn;
        }
        if (number == 5 || number == 6 || number == 7)
        {
            return 0;
        }
        return NeedsPublishedFrame;
    }

    static void CallHelper(int number)
    {
        if ((HelperEffects(number) & NeedsPublishedFrame) != 0)
        {
            FlushCache();
        }
        Li(25, Host.Helper(number));
        Emit(R(25, 0, 31, 0, 9));
        Emit(0);
    }

    static void Trap(int reason)
    {
        Li(4, reason);
        CallHelper(4);
    }

    static void Check(int slot, int kind)
    {
        int ok = Label();
        Lw(24, slot, 0);
        Li(25, kind);
        Branch(4, 24, 25, ok);
        Trap(kind);
        Bind(ok);
    }

    static void Copy(int source, int dest)
    {
        Lw(8, source, 0);
        Lw(9, source, 4);
        Sw(8, dest, 0);
        Sw(9, dest, 4);
    }

    static void StoreInt(int slot, int reg)
    {
        int free = -1;
        for (int i = 0; i < 6; i++)
        {
            if (cachedSlots[i] == slot)
            {
                Move(18 + i, reg);
                return;
            }

            if (cachedSlots[i] < 0)
            {
                free = i;
            }
        }

        if (free < 0)
        {
            free = cacheVictim++ % 6;
            FlushOne(free);
        }

        Move(18 + free, reg);
        cachedSlots[free] = slot;
    }

    static void CopyInt(int source, int dest)
    {
        Lw(8, source, 4);
        StoreInt(dest, 8);
    }

    static void StoreVal(int slot, Val value)
    {
        Li(8, value.kind);
        Sw(8, slot, 0);
        Li(9, (int)value.data);
        Sw(9, slot, 4);
    }

    static void Budget(int count)
    {
        int ok = Label();
        Li(25, Host.Counter());
        Emit(I(35, 25, 24, 0));
        Emit(I(9, 24, 24, count));
        Emit(I(43, 25, 24, 0));
        Li(25, 10000000);
        Emit(R(25, 24, 24, 0, 42));
        Branch(4, 24, 0, ok);
        Trap(10);
        Bind(ok);
    }

    static bool Constant(PlanOp op)
    {
        return (op.code >= 0x15 && op.code <= 0x20);
    }

    static int ConstantValue(PlanOp op)
    {
        return op.code < 0x1f ? op.code - 0x16 : op.operand;
    }

    static void FoldConstantCall(ref PlanOp op, int value)
    {
        op.code = 0x20;
        op.operand = value;
    }

    static bool Leaf(int mid, int nesting)
    {
        ref Body b = ref bodies[mid];
        if (b.count > 80 || nesting > 1)
        {
            return false;
        }

        for (int i = 0; i < b.count; i++)
        {
            ref PlanOp o = ref b.plan[i];
            ColdOp extra = b.cold[o.cold];
            if (o.code == 0x6f || o.code == 0x73)
            {
                return false;
            }

            if (o.code == 0x28 && (extra.method < 0 || !Leaf(extra.method, nesting + 1)))
            {
                return false;
            }
        }

        return true;
    }

    static void SlowCall(int mid, int pc, int origin)
    {
        Li(4, mid);
        Li(5, pc);
        Emit(I(9, 16, 6, origin * 8));
        CallHelper(3);
    }

    static void Arithmetic(int code, int sp, int immediate, int fused, int leftKnown, int rightKnown)
    {
        int left = fused != 0 ? sp - 1 : sp - 2;
        if (leftKnown == 0)
        {
            Check(left, 1);
        }

        Lw(8, left, 4);
        if (fused != 0)
        {
            Li(9, immediate);
        }
        else
        {
            if (rightKnown == 0)
            {
                Check(sp - 1, 1);
            }

            Lw(9, sp - 1, 4);
        }

        switch (code)
        {
            case 0x58:
                Emit(R(8, 9, 8, 0, 33));
                break;
            case 0x59:
                Emit(R(8, 9, 8, 0, 35));
                break;
            case 0x5a:
                Emit(R(8, 9, 0, 0, 24));
                Emit(R(0, 0, 8, 0, 18));
                break;
            case 0x5b:
            case 0x5c:
            case 0x5d:
            case 0x5e:
                {
                    // Match C's strength reduction for fixed-point /4096 and power-of-two
                    // remainder, preserving CLI truncation toward zero for negative operands.
                    if (fused != 0 && immediate > 0 && immediate <= 32768 && (immediate & (immediate - 1)) == 0)
                    {
                        int shift = 0;
                        for (int n = immediate; n > 1; n >>= 1)
                        {
                            shift++;
                        }

                        if (code == 0x5b || code == 0x5d)
                        {
                            Emit(R(0, 8, 10, 31, 3));
                            Emit(I(12, 10, 10, immediate - 1));
                            Emit(R(8, 10, 8, 0, 33));
                            if (code == 0x5b)
                            {
                                Emit(R(0, 8, 8, shift, 3));
                            }
                            else
                            {
                                Emit(I(12, 8, 8, immediate - 1));
                                Emit(R(8, 10, 8, 0, 35));
                            }
                        }
                        else if (code == 0x5c)
                        {
                            Emit(R(0, 8, 8, shift, 2));
                        }
                        else
                        {
                            Emit(I(12, 8, 8, immediate - 1));
                        }
                    }
                    else
                    {
                        int nonzero = Label();
                        Branch(5, 9, 0, nonzero);
                        Trap(12);
                        Bind(nonzero);
                        if (code == 0x5b)
                        {
                            int safe = Label();
                            Li(24, -1);
                            Branch(5, 9, 24, safe);
                            Li(24, -2147483647 - 1);
                            Branch(5, 8, 24, safe);
                            Trap(13);
                            Bind(safe);
                        }

                        int done = Label();
                        if (code == 0x5d)
                        {
                            int divide = Label();
                            Li(24, -1);
                            Branch(5, 9, 24, divide);
                            Li(8, 0);
                            Jump(done);
                            Bind(divide);
                        }

                        Emit(R(8, 9, 0, 0, code == 0x5c || code == 0x5e ? 27 : 26));
                        Emit(R(0, 0, 8, 0, code == 0x5d || code == 0x5e ? 16 : 18));
                        Bind(done);
                    }

                    break;
                }

            case 0x5f:
                Emit(R(8, 9, 8, 0, 36));
                break;
            case 0x60:
                Emit(R(8, 9, 8, 0, 37));
                break;
            case 0x61:
                Emit(R(8, 9, 8, 0, 38));
                break;
            case 0x62:
                Emit(R(9, 8, 8, 0, 4));
                break;
            case 0x63:
                Emit(R(9, 8, 8, 0, 7));
                break;
            case 0x64:
                Emit(R(9, 8, 8, 0, 6));
                break;
            case 0xfe02:
                Emit(R(9, 8, 8, 0, 42));
                break;
            case 0xfe03:
                Emit(R(9, 8, 8, 0, 43));
                break;
            case 0xfe04:
                Emit(R(8, 9, 8, 0, 42));
                break;
            case 0xfe05:
                Emit(R(8, 9, 8, 0, 43));
                break;
            default:
                Bad("JIT arithmetic template");
                break;
        }

        StoreInt(left, 8);
    }

    static void Return(int mid, int origin, int sp, int end, int destination)
    {
        ref Body b = ref bodies[mid];
        if (IntType(b.returns))
        {
            Check(sp - 1, 1);
        }

        if (end >= 0)
        {
            if (b.returns != 1)
            {
                Copy(sp - 1, destination);
            }

            FlushCache();
            Jump(end);
            return;
        }

        FlushCache();
        if (b.returns != 1)
        {
            Lw(8, sp - 1, 0);
            Lw(9, sp - 1, 4);
            Emit(I(43, 29, 8, 0));
            Emit(I(43, 29, 9, 4));
        }

        Li(4, mid);
        Move(5, 16);
        CallHelper(2);
        if (b.returns != 1)
        {
            Emit(I(35, 29, 8, 0));
            Emit(I(35, 29, 9, 4));
            Emit(I(43, 17, 8, 0));
            Emit(I(43, 17, 9, 4));
        }

        Emit(I(35, 29, 31, 60));
        Emit(I(35, 29, 16, 56));
        Emit(I(35, 29, 17, 52));
        for (int i = 0; i < 6; i++)
        {
            Emit(I(35, 29, 18 + i, 44 - i * 4));
        }

        Emit(R(31, 0, 0, 0, 8));
        Emit(I(9, 29, 29, 64));
    }

    static bool ArithmeticCode(int code)
    {
        return (code >= 0x58 && code <= 0x64) || (code >= 0xfe02 && code <= 0xfe05);
    }

    static void ArrayLoad(int sp, int store)
    {
        int array = sp - (store != 0 ? 3 : 2);
        int index = array + 1;
        Check(array, 3);
        Check(index, 1);
        Lw(4, array, 4);
        Lw(5, index, 4);
        CallHelper(5);
        if (store == 0)
        {
            Emit(I(35, 2, 8, 0));
            Emit(I(35, 2, 9, 4));
            Sw(8, array, 0);
            Sw(9, array, 4);
        }
        else
        {
            Lw(8, sp - 1, 0);
            Lw(9, sp - 1, 4);
            Emit(I(43, 2, 8, 0));
            Emit(I(43, 2, 9, 4));
        }
    }

    static void EmitBody(int mid, int origin, int end, int destination, int inlining)
    {
        ref Body b = ref bodies[mid];
        FlushCache();
        int[] map = new int[b.count + 1];
        int[] known = new int[b.maxstack + 1];
        for (int i = 0; i <= b.count; i++)
        {
            map[i] = Label();
        }

        if (inlining != 0)
        {
            for (int i = 0; i < b.nargs; i++)
            {
                if (IntType(b.argtypes[i]))
                {
                    Check(origin + i, 1);
                }
            }
        }

        for (int i = 0; i < b.nlocals && inlining != 0; i++)
        {
            StoreVal(origin + b.nargs + i, b.init[i]);
        }

        for (int pc = 0; pc < b.count; pc++)
        {
            ref PlanOp o = ref b.plan[pc];
            ColdOp extra = b.cold[o.cold];
            int code = o.code, a = o.operand;
            int sp = origin + o.stack;
            if ((o.flags & PlanBlockStart) != 0)
            {
                FlushCache();
                for (int i = 0; i <= b.maxstack; i++)
                {
                    known[i] = 0;
                }
            }

            Bind(map[pc]);
            if (o.stack < 0)
            {
                continue;
            }

            int d = o.stack - b.nargs - b.nlocals, resultKnown = 0;
            int leftKnown = d >= 2 ? known[d - 2] : 0, rightKnown = d >= 1 ? known[d - 1] : 0;
            if ((o.flags & PlanBlockStart) != 0)
            {
                int next = pc + 1;
                while (next < b.count && (b.plan[next].flags & PlanBlockStart) == 0 && b.plan[next - 1].code != 0x2a)
                {
                    next++;
                }

                Budget(next - pc);
            }

            if (Constant(o))
            {
                if (pc + 1 < b.count && (b.plan[pc + 1].flags & PlanBlockStart) == 0 && ArithmeticCode(b.plan[pc + 1].code))
                {
                    Arithmetic(b.plan[pc + 1].code, sp, ConstantValue(o), 1, rightKnown, 1);
                    known[d - 1] = 1;
                    Bind(map[++pc]);
                    continue;
                }
                else
                {
                    Li(8, ConstantValue(o));
                    StoreInt(sp, 8);
                    resultKnown = 1;
                }
            }
            else if ((code >= 2 && code <= 5) || code == 0x0e || code == 0xfe09)
            {
                int arg = code >= 2 && code <= 5 ? code - 2 : a;
                if (IntType(b.argtypes[arg]))
                {
                    CopyInt(origin + arg, sp);
                    resultKnown = 1;
                }
                else
                {
                    Copy(origin + arg, sp);
                }
            }
            else if ((code >= 6 && code <= 9) || code == 0x11 || code == 0xfe0c)
            {
                int local = code >= 6 && code <= 9 ? code - 6 : a;
                if (IntType(b.locals[local]))
                {
                    CopyInt(origin + b.nargs + local, sp);
                    resultKnown = 1;
                }
                else
                {
                    Copy(origin + b.nargs + local, sp);
                }
            }
            else if ((code >= 10 && code <= 13) || code == 0x13 || code == 0xfe0e)
            {
                int local = code >= 10 && code <= 13 ? code - 10 : a;
                if (IntType(b.locals[local]))
                {
                    if (rightKnown == 0)
                    {
                        Check(sp - 1, 1);
                    }

                    CopyInt(sp - 1, origin + b.nargs + local);
                }
                else
                {
                    Copy(sp - 1, origin + b.nargs + local);
                }
            }
            else if (code == 0x10 || code == 0xfe0b)
            {
                if (IntType(b.argtypes[a]))
                {
                    if (rightKnown == 0)
                    {
                        Check(sp - 1, 1);
                    }

                    CopyInt(sp - 1, origin + a);
                }
                else
                {
                    Copy(sp - 1, origin + a);
                }
            }
            else if (code == 0x14)
            {
                Sw(0, sp, 0);
                Sw(0, sp, 4);
            }
            else if (code == 0x25)
            {
                if (rightKnown != 0)
                {
                    CopyInt(sp - 1, sp);
                }
                else
                {
                    Copy(sp - 1, sp);
                }

                resultKnown = rightKnown;
            }
            else if (code == 0 || code == 0x26)
            {
            }
            else if (ArithmeticCode(code))
            {
                Arithmetic(code, sp, 0, 0, leftKnown, rightKnown);
                resultKnown = 1;
            }
            else if (code == 0xfe01)
            {
                FlushCache();
                int fallback = Label(), done = Label();
                if (leftKnown == 0)
                {
                    Lw(24, sp - 2, 0);
                    Li(25, 1);
                    Branch(5, 24, 25, fallback);
                }

                if (rightKnown == 0)
                {
                    Lw(24, sp - 1, 0);
                    Li(25, 1);
                    Branch(5, 24, 25, fallback);
                }

                Lw(8, sp - 2, 4);
                Lw(9, sp - 1, 4);
                Emit(R(8, 9, 8, 0, 38));
                Emit(I(11, 8, 8, 1));
                StoreInt(sp - 2, 8);
                FlushCache();
                Jump(done);
                Bind(fallback);
                SlowCall(mid, pc, origin);
                Bind(done);
                resultKnown = 1;
            }
            else if (code == 0x2a)
            {
                Return(mid, origin, sp, end, destination);
            }
            else if (code == 0x2b || code == 0x38)
            {
                FlushCache();
                Jump(map[a]);
            }
            else if (code == 0x2c || code == 0x39 || code == 0x2d || code == 0x3a)
            {
                FlushCache();
                Lw(8, sp - 1, 0);
                Lw(9, sp - 1, 4);
                Li(24, 1);
                int integer = Label();
                Branch(4, 8, 24, integer);
                Move(9, 8);
                Bind(integer);
                Branch(code == 0x2c || code == 0x39 ? 4 : 5, 9, 0, map[a]);
            }
            else if (code == 0x28 && extra.method >= 0)
            {
                FlushCache();
                resultKnown = IntType(extra.resultType) ? 1 : 0;
                ref Body callee = ref bodies[extra.method];
                if (inlining < 2 && Leaf(extra.method, 0))
                {
                    int done = Label();
                    EmitBody(extra.method, sp - callee.nargs, done, sp - callee.nargs, inlining + 1);
                    Bind(done);
                }
                else
                {
                    Emit(I(9, 16, 4, (sp - callee.nargs) * 8));
                    Emit(I(9, 16, 5, (sp - callee.nargs) * 8));
                    Li(25, Host.MethodSlot(extra.method));
                    Emit(I(35, 25, 25, 0));
                    Emit(R(25, 0, 31, 0, 9));
                    Emit(0);
                }
            }
            else if (code == 0x72)
            {
                StoreVal(sp, extra.literal);
            }
            else if (code == 0x94 || code == 0x95 || code == 0x9a || code == 0xa3)
            {
                ArrayLoad(sp, 0);
            }
            else if (code == 0x9e || code == 0xa2 || code == 0xa4)
            {
                ArrayLoad(sp, 1);
            }
            else if (code == 0x7e || code == 0x80)
            {
                FlushCache();
                if (code == 0x80 && IntType(fieldTypes[a]) && rightKnown == 0)
                {
                    Check(sp - 1, 1);
                }
                Li(25, Host.GlobalSlot(globals + a));
                if (code == 0x7e)
                {
                    Emit(I(35, 25, 8, 0));
                    Emit(I(35, 25, 9, 4));
                    Sw(8, sp, 0);
                    Sw(9, sp, 4);
                }
                else
                {
                    Lw(8, sp - 1, 0);
                    Lw(9, sp - 1, 4);
                    Emit(I(43, 25, 8, 0));
                    Emit(I(43, 25, 9, 4));
                }
                if (code == 0x7e && IntType(fieldTypes[a]))
                {
                    resultKnown = 1;
                }
            }
            else if (code == 0x8e)
            {
                Check(sp - 1, 3);
                Lw(4, sp - 1, 4);
                CallHelper(7);
                StoreInt(sp - 1, 2);
                resultKnown = 1;
            }
            else if (code == 0x7b)
            {
                FlushCache();
                int slow = Label(), done = Label();
                Lw(24, sp - 1, 0);
                Li(25, 4);
                Branch(5, 24, 25, slow);
                Lw(4, sp - 1, 4);
                Li(5, a);
                CallHelper(6);
                Emit(I(35, 2, 8, 0));
                Emit(I(35, 2, 9, 4));
                Sw(8, sp - 1, 0);
                Sw(9, sp - 1, 4);
                Jump(done);
                Bind(slow);
                SlowCall(mid, pc, origin);
                Bind(done);
                if (IntType(fieldTypes[a]))
                {
                    Check(sp - 1, 1);
                    resultKnown = 1;
                }
            }
            else if (code == 0x69 || code == 0x6d || code == 0xd3 || code == 0xe0)
            {
                if (rightKnown == 0)
                {
                    Check(sp - 1, 1);
                }

                resultKnown = 1;
            }
            else
            {
                SlowCall(mid, pc, origin);
                if ((code >= 0x2e && code <= 0x37) || (code >= 0x3b && code <= 0x44))
                {
                    Branch(5, 2, 0, map[a]);
                }

                if ((code >= 0x5b && code <= 0x5e) || (code >= 0x65 && code <= 0x69) || code == 0xd1 || code == 0xd2 || code == 0xfe01 || ((code == 0x28 || code == 0x6f) && IntType(extra.resultType)))
                {
                    resultKnown = 1;
                }
            }

            int pop = o.flags & PlanPopMask, push = (o.flags >> PlanPushShift) & PlanPushMask;
            if (push > 0)
            {
                known[d - pop + push - 1] = resultKnown;
            }
        }

        FlushCache();
        Bind(map[b.count]);
        Host.ReleaseInts(map);
        Host.ReleaseInts(known);
    }

    static void Compile()
    {
        cachedSlots = new int[6];
        // Fold only exact constant leaves; the original IL remains in the assembly and is
        // listed separately. Branch stack effects are unchanged by these replacements.
        for (int mid = 0; mid < methodCount; mid++)
        {
            for (int pc = 0; pc < bodies[mid].count; pc++)
            {
                ref PlanOp op = ref bodies[mid].plan[pc];
                ColdOp extra = bodies[mid].cold[op.cold];
                if (op.code == 0x28 && extra.method >= 0)
                {
                    ref Body c = ref bodies[extra.method];
                    if (c.nargs == 0 && c.count == 2 && Constant(c.plan[0]) && c.plan[1].code == 0x2a)
                    {
                        FoldConstantCall(ref op, ConstantValue(c.plan[0]));
                    }
                }
            }
        }

        // Reserve inline scratch beyond the original maximum stack. It is cleared on return.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int mid = 0; mid < methodCount; mid++)
            {
                ref Body b = ref bodies[mid];
                int maximum = b.maxstack;
                for (int pc = 0; pc < b.count; pc++)
                {
                    ref PlanOp op = ref b.plan[pc];
                    ColdOp extra = b.cold[op.cold];
                    if (op.code == 0x28 && extra.method >= 0 && Leaf(extra.method, 0))
                    {
                        ref Body c = ref bodies[extra.method];
                        int n = op.stack - b.nargs - b.nlocals + c.nlocals + c.maxstack;
                        if (n > maximum)
                        {
                            maximum = n;
                        }
                    }
                }

                b.maxstack = maximum;
            }
        }

        for (int mid = 0; mid < methodCount; mid++)
        {
            ref Body b = ref bodies[mid];
            machineCapacity = b.count * 256 + 2048;
            machineCount = 0;
            labelCount = 0;
            fixCount = 0;
            for (int i = 0; i < 6; i++)
            {
                cachedSlots[i] = -1;
            }

            cacheVictim = 0;
            machine = new uint[machineCapacity];
            labels = new int[machineCapacity];
            fixAt = new int[machineCapacity];
            fixTo = new int[machineCapacity];
            Emit(I(9, 29, 29, -64));
            Emit(I(43, 29, 31, 60));
            Emit(I(43, 29, 16, 56));
            Emit(I(43, 29, 17, 52));
            for (int i = 0; i < 6; i++)
            {
                Emit(I(43, 29, 18 + i, 44 - i * 4));
            }

            Move(16, 4);
            Move(17, 5);
            Li(4, mid);
            Move(5, 16);
            CallHelper(1);
            EmitBody(mid, 0, -1, 0, 0);
            for (int i = 0; i < fixCount; i++)
            {
                int target = labels[fixTo[i]], delta = target - fixAt[i] - 1;
                if (target < 0 || delta < -32768 || delta > 32767)
                {
                    Bad("JIT branch range");
                }

                machine[fixAt[i]] = (machine[fixAt[i]] & 0xffff0000U) | ((uint)delta & 65535);
            }

            Host.CodeReady(mid, b.name, machine, machineCount);
            Host.ReleaseWords(machine);
            Host.ReleaseInts(labels);
            Host.ReleaseInts(fixAt);
            Host.ReleaseInts(fixTo);
        }

        jit = 1;
    }
}
