namespace ProLang.Portable;

public static partial class VM
{
    static Val Ref(int kind, int handle)
    {
        Val value = default;
        value.kind = kind;
        value.data = handle;
        return value;
    }

    static int Object(Val value, int kind)
    {
        if (value.kind != kind || value.data <= 0 || value.data >= objects.Length || objects[value.data].kind != kind)
        {
            Bad("invalid managed reference");
        }
        return value.data;
    }

    static int NewObject(int type, int count, int permanent)
    {
        if (count < 0 || count > 262144)
        {
            Bad("object size limit");
        }
        int handle = AllocateObject(4, permanent);
        objects[handle].type = type;
        objects[handle].count = count;
        objects[handle].values = new Val[count];
        allocationBytes += count * 8 + 32;
        return handle;
    }

    static int NewText(int count, int permanent)
    {
        if (count < 0 || count > 32768)
        {
            Bad("string length limit");
        }
        int handle = AllocateObject(2, permanent);
        objects[handle].type = 14;
        objects[handle].count = count;
        objects[handle].chars = new ushort[count + 1];
        allocationBytes += (count + 1) * 2 + 32;
        return handle;
    }

    static Val Zero(int type, int nesting)
    {
        if (nesting > 32)
        {
            Bad("recursive struct");
        }

        if ((type & 0xfff00000) == 0x100000)
        {
            int row = type & 0xfffff;
            if (row < 1 || row > rows[2])
            {
                Bad("struct type");
            }

            if (zeros[row].kind == 4)
            {
                return zeros[row];
            }

            int count = 0;
            for (int i = 0; i < fieldCount; i++)
            {
                if (fieldOwners[i] == row && fieldStatic[i] == 0)
                {
                    count++;
                }
            }

            int o = NewObject(type, count, 1);
            for (int i = 0; i < fieldCount; i++)
            {
                if (fieldOwners[i] == row && fieldStatic[i] == 0)
                {
                    objects[o].values[fieldSlots[i]] = Zero(fieldTypes[i], nesting + 1);
                }
            }

            zeros[row] = Ref(4, o);
            return zeros[row];
        }

        if (type >= 2 && type <= 9)
        {
            return Integer(0);
        }

        return default(Val);
    }

    static Val NewArray(int type, int count)
    {
        if (count < 0 || count > 262144 - totalElements)
        {
            Bad("array heap limit exceeded");
        }

        totalElements += count;
        int array = NewObject(type, count, 0);
        Val value = Zero(type, 0);
        for (int i = 0; i < count; i++)
        {
            objects[array].values[i] = value;
        }

        objects[array].kind = 3;
        return Ref(3, array);
    }

    static int Element(Val array, int index)
    {
        if (array.kind != 3)
        {
            Bad("expected non-null array");
        }
        int handle = Object(array, 3);
        if (index < 0 || index >= objects[handle].count)
        {
            Bad("array index out of bounds");
        }
        return handle;
    }

    // The native JIT uses this same check before its platform bridge obtains a slot.
    public static int ValidateArray(int handle, int index)
    {
        if (handle <= 0 || handle >= objects.Length || objects[handle].kind != 3)
        {
            return Bad("expected non-null array");
        }
        if (index < 0 || index >= objects[handle].count)
        {
            return Bad("array index out of bounds");
        }
        return handle;
    }

    public static int ArrayLength(int handle)
    {
        if (handle <= 0 || handle >= objects.Length || objects[handle].kind != 3)
        {
            return Bad("expected non-null array");
        }
        return objects[handle].count;
    }

    public static int ValidateField(int handle, int field)
    {
        if (handle <= 0 || handle >= objects.Length || objects[handle].kind != 4)
        {
            return Bad("invalid managed reference");
        }
        if (field < 0 || field >= fieldCount || fieldStatic[field] != 0 ||
            objects[handle].type != (0x100000 | fieldOwners[field]))
        {
            return Bad("invalid field owner");
        }
        int slot = fieldSlots[field];
        if (slot < 0 || slot >= objects[handle].count)
        {
            return Bad("field index");
        }
        return slot;
    }

    static Val SlotAddress(int slot)
    {
        int handle = AllocateObject(5, 0);
        objects[handle].root = slot;
        return Ref(5, handle);
    }

    static Val ArrayAddress(Val array, int index)
    {
        int owner = Element(array, index);
        int handle = AllocateObject(5, 0);
        objects[handle].array = owner;
        objects[handle].root = index;
        return Ref(5, handle);
    }

    static Val FieldAddress(Val parent, int field)
    {
        Object(parent, 5);
        int handle = AllocateObject(5, 0);
        objects[handle].parent = parent.data;
        objects[handle].root = field;
        return Ref(5, handle);
    }

    static Val ReadAddress(int handle, int nesting)
    {
        if (nesting > 32 || handle <= 0 || handle >= objects.Length || objects[handle].kind != 5)
        {
            Bad("invalid managed address");
        }
        int parent = objects[handle].parent;
        int index = objects[handle].root;
        if (parent == 0)
        {
            int array = objects[handle].array;
            return array == 0 ? memory[index] : objects[array].values[index];
        }
        int owner = Object(ReadAddress(parent, nesting + 1), 4);
        if (index < 0 || index >= objects[owner].count)
        {
            Bad("field index");
        }
        return objects[owner].values[index];
    }

    static void WriteAddress(int handle, Val value, int nesting)
    {
        if (nesting > 32 || handle <= 0 || handle >= objects.Length || objects[handle].kind != 5)
        {
            Bad("invalid managed address");
        }
        int parent = objects[handle].parent;
        int index = objects[handle].root;
        if (parent == 0)
        {
            int array = objects[handle].array;
            if (array == 0)
            {
                memory[index] = value;
            }
            else
            {
                objects[array].values[index] = value;
            }
            return;
        }
        int old = Object(ReadAddress(parent, nesting + 1), 4);
        if (index < 0 || index >= objects[old].count)
        {
            Bad("field index");
        }
        int copy = NewObject(objects[old].type, objects[old].count, 0);
        for (int i = 0; i < objects[old].count; i++)
        {
            objects[copy].values[i] = objects[old].values[i];
        }
        objects[copy].values[index] = value;
        WriteAddress(parent, Ref(4, copy), nesting + 1);
    }

    static Val Deref(Val value)
    {
        return value.kind == 5 ? ReadAddress(value.data, 0) : value;
    }

    static bool Equal(Val a, Val b, int identity)
    {
        if (a.kind != b.kind)
        {
            return false;
        }

        if (a.kind == 2)
        {
            if (identity != 0)
            {
                Bad("string reference identity comparison is unsupported");
            }

            int x = Object(a, 2);
            int y = Object(b, 2);
            if (objects[x].count != objects[y].count)
            {
                return false;
            }

            for (int i = 0; i < objects[x].count; i++)
            {
                if (objects[x].chars[i] != objects[y].chars[i])
                {
                    return false;
                }
            }

            return true;
        }

        return a.data == b.data;
    }

    static int Concat(int a, int b)
    {
        int text = NewText(objects[a].count + objects[b].count, 0);
        for (int i = 0; i < objects[a].count; i++)
        {
            objects[text].chars[i] = objects[a].chars[i];
        }

        for (int i = 0; i < objects[b].count; i++)
        {
            objects[text].chars[objects[a].count + i] = objects[b].chars[i];
        }

        return text;
    }

    static int Ascii(string s)
    {
        int n = Host.Length(s);
        int text = NewText(n, 0);
        Host.Ascii(s, objects[text].chars, n);
        return text;
    }

    static int Format(Val v, int declared)
    {
        if (v.kind == 8)
        {
            int box = Object(v, 8);
            return Format(objects[box].values[0], objects[box].type);
        }

        if (v.kind == 0)
        {
            return NewText(0, 0);
        }

        if (v.kind == 2)
        {
            return Object(v, 2);
        }

        int n = Int(v);
        if (declared == 2)
        {
            return Ascii(n == 0 ? "False" : "True");
        }

        if (declared == 3)
        {
            int ch = NewText(1, 0);
            objects[ch].chars[0] = (ushort)n;
            return ch;
        }

        int text = NewText(16, 0);
        int length = Host.Number(n, declared == 9, objects[text].chars);
        objects[text].count = length;
        return text;
    }

    static void Flush()
    {
        if (output != 0 && objects[output].count > 0)
        {
            Host.Write(objects[output].chars, objects[output].count);
        }

        output = 0;
    }

    static Val NativeCall(Op op, int args)
    {
        int id = op.native;
        switch (id)
        {
            case 1:
                Host.Init();
                break;
            case 2:
                Host.Clear(Int(memory[args + 0]));
                break;
            case 3:
                Host.Rect(Int(memory[args + 0]), Int(memory[args + 1]), Int(memory[args + 2]), Int(memory[args + 3]), Int(memory[args + 4]));
                break;
            case 4:
                Host.Line(Int(memory[args + 0]), Int(memory[args + 1]), Int(memory[args + 2]), Int(memory[args + 3]), Int(memory[args + 4]));
                break;
            case 5:
                {
                    int text = Object(memory[args + 2], 2);
                    Host.Text(Int(memory[args + 0]), Int(memory[args + 1]), objects[text].chars, objects[text].count, Int(memory[args + 3]));
                    break;
                }

            case 6:
                Host.Present(steps, calls);
                steps = 0;
                calls = 0;
                Host.CollectStats();
                break;
            case 7:
                Host.Vsync();
                break;
            case 8:
                return Integer(Host.Buttons());
            case 9:
                return Integer((Host.Buttons() & Int(memory[args + 0])) != 0 ? 1 : 0);
            case 10:
                output = 0;
                break;
            case 11:
                Flush();
                break;
            case 12:
                {
                    int formatted = Format(memory[args + 0], op.type);
                    int newline = NewText(objects[formatted].count + 1, 0);
                    for (int i = 0; i < objects[formatted].count; i++)
                    {
                        objects[newline].chars[i] = objects[formatted].chars[i];
                    }

                    objects[newline].chars[objects[formatted].count] = 10;
                    output = output == 0 ? newline : Concat(output, newline);
                    break;
                }

            case 13:
                return Ref(2, Format(memory[args + 0], op.type));
            case 14:
                return Ref(2, Concat(Format(memory[args + 0], 28), Format(memory[args + 1], 28)));
            case 15:
                return Integer(Equal(memory[args + 0], memory[args + 1], 0) ? 1 : 0);
            case 16:
                return Integer(Equal(memory[args + 0], memory[args + 1], 0) ? 0 : 1);
            case 17:
                return Integer(objects[Object(memory[args + 0], 2)].count);
            case 18:
                {
                    int text = Object(memory[args + 0], 2);
                    int index = Int(memory[args + 1]);
                    if (index < 0 || index >= objects[text].count)
                    {
                        Bad("string index out of bounds");
                    }

                    return Integer(objects[text].chars[index]);
                }

            case 19:
                {
                    int text = Object(memory[args + 0], 2);
                    int start = Int(memory[args + 1]), end = Int(memory[args + 2]);
                    if (start < 0 || end < start || end > objects[text].count)
                    {
                        Bad("substring range");
                    }

                    int result = NewText(end - start, 0);
                    for (int i = 0; i < end - start; i++)
                    {
                        objects[result].chars[i] = objects[text].chars[start + i];
                    }

                    return Ref(2, result);
                }

            case 20:
                {
                    int text = Object(memory[args + 0], 2);
                    int needle = Object(memory[args + 1], 2);
                    for (int i = 0; i <= objects[text].count - objects[needle].count; i++)
                    {
                        int same = 1;
                        for (int j = 0; j < objects[needle].count; j++)
                        {
                            if (objects[text].chars[i + j] != objects[needle].chars[j])
                            {
                                same = 0;
                            }
                        }

                        if (same != 0)
                        {
                            return Integer(i);
                        }
                    }

                    return Integer(-1);
                }

            case 21:
                {
                    int a = Int(memory[args + 0]), b = Int(memory[args + 1]);
                    return Integer(a < b ? a : b);
                }

            case 22:
                {
                    int a = Int(memory[args + 0]), b = Int(memory[args + 1]);
                    return Integer(a > b ? a : b);
                }

            case 23:
                if (Int(memory[args + 0]) == 0)
                {
                    Flush();
                    int message = Object(memory[args + 1], 2);
                    Host.Write(objects[message].chars, objects[message].count);
                    Bad("assertion failed");
                }

                break;
            case 24:
                {
                    Flush();
                    int text = Object(memory[args + 0], 2);
                    Host.Write(objects[text].chars, objects[text].count);
                    break;
                }

            case 25:
                {
                    int n = Int(memory[args + 0]);
                    if (n < 0 || n > 60000)
                    {
                        Bad("sleep range");
                    }

                    Host.Sleep(n);
                    break;
                }

            case 26:
                return Integer(Host.Millis());
            default:
                Bad("unsupported native call");
                break;
        }

        return default(Val);
    }

    // Returns a branch decision. Every other operation returns zero.
    public static int Slow(int mid, int pc, int frame)
    {
        CollectIfNeeded();
        ref Body b = ref bodies[mid];
        ref Op o = ref b.ops[pc];
        int c = o.code, a = o.operand;
        int locals = frame + b.nargs;
        int stack = locals + b.nlocals;
        int sp = stack + o.depth;
        Val x = o.depth > 0 ? memory[sp + -1] : default(Val), y = o.depth > 1 ? memory[sp + -2] : default(Val);
        if (c >= 2 && c <= 5)
        {
            memory[sp + 0] = memory[frame + c - 2];
        }
        else if (c >= 6 && c <= 9)
        {
            memory[sp + 0] = memory[locals + c - 6];
        }
        else if (c >= 10 && c <= 13)
        {
            memory[locals + c - 10] = x;
        }
        else if (c >= 0x15 && c <= 0x1e)
        {
            memory[sp + 0] = Integer(c - 0x16);
        }
        else if ((c >= 0x58 && c <= 0x64) || (c >= 0xfe02 && c <= 0xfe05))
        {
            memory[sp + -2] = Integer(Binary(c, Int(y), Int(x)));
        }
        else if ((c >= 0x2e && c <= 0x37) || (c >= 0x3b && c <= 0x44))
        {
            int compare = c <= 0x37 ? c - 0x2e : c - 0x3b;
            switch (compare)
            {
                case 0:
                    return Equal(y, x, 1) ? 1 : 0;
                case 5:
                    return Equal(y, x, 1) ? 0 : 1;
                case 1:
                    return Int(y) >= Int(x) ? 1 : 0;
                case 2:
                    return Int(y) > Int(x) ? 1 : 0;
                case 3:
                    return Int(y) <= Int(x) ? 1 : 0;
                case 4:
                    return Int(y) < Int(x) ? 1 : 0;
                case 6:
                    return (uint)Int(y) >= (uint)Int(x) ? 1 : 0;
                case 7:
                    return (uint)Int(y) > (uint)Int(x) ? 1 : 0;
                case 8:
                    return (uint)Int(y) <= (uint)Int(x) ? 1 : 0;
                case 9:
                    return (uint)Int(y) < (uint)Int(x) ? 1 : 0;
            }
        }
        else
        {
            switch (c)
            {
                case 0:
                case 0x26:
                    break;
                case 0x0e:
                case 0xfe09:
                    memory[sp + 0] = memory[frame + a];
                    break;
                case 0x0f:
                case 0xfe0a:
                    memory[sp + 0] = SlotAddress(frame + a);
                    break;
                case 0x10:
                case 0xfe0b:
                    memory[frame + a] = x;
                    break;
                case 0x11:
                case 0xfe0c:
                    memory[sp + 0] = memory[locals + a];
                    break;
                case 0x12:
                case 0xfe0d:
                    memory[sp + 0] = SlotAddress(locals + a);
                    break;
                case 0x13:
                case 0xfe0e:
                    memory[locals + a] = x;
                    break;
                case 0x14:
                    memory[sp + 0] = default(Val);
                    break;
                case 0x1f:
                case 0x20:
                    memory[sp + 0] = Integer(a);
                    break;
                case 0x25:
                    memory[sp + 0] = x;
                    break;
                case 0x28:
                case 0x6f:
                    {
                        int args = sp - o.count;
                        Val result = default;
                        if (o.method >= 0)
                        {
                            result = Invoke(o.method, args);
                        }
                        else if (o.native == 100)
                        {
                            if (memory[args + 0].kind != 7)
                            {
                                Bad("Invoke on invalid delegate");
                            }

                            int target = (int)memory[args + 0].data;
                            if (target < 0 || target >= methodCount || bodies[target].nargs != o.count - 1)
                            {
                                Bad("delegate signature mismatch");
                            }

                            result = Invoke(target, args + 1);
                        }
                        else
                        {
                            result = NativeCall(o, args);
                        }

                        if (o.returns != 0)
                        {
                            memory[args + 0] = result;
                        }

                        break;
                    }

                case 0x65:
                    memory[sp + -1] = Integer(unchecked(-Int(x)));
                    break;
                case 0x66:
                    memory[sp + -1] = Integer(~Int(x));
                    break;
                case 0x67:
                    memory[sp + -1] = Integer((sbyte)Int(x));
                    break;
                case 0x68:
                    memory[sp + -1] = Integer((short)Int(x));
                    break;
                case 0x69:
                case 0x6d:
                case 0xd3:
                case 0xe0:
                    Int(x);
                    break;
                case 0xd1:
                    memory[sp + -1] = Integer((ushort)Int(x));
                    break;
                case 0xd2:
                    memory[sp + -1] = Integer((byte)Int(x));
                    break;
                case 0x71:
                    if (x.kind != 5)
                    {
                        Bad("expected managed address");
                    }

                    memory[sp + -1] = ReadAddress(x.data, 0);
                    break;
                case 0x72:
                    memory[sp + 0] = o.literal;
                    break;
                case 0x73:
                    if (o.native != 101 || x.kind != 6 || y.kind != 0)
                    {
                        Bad("unsupported constructor");
                    }

                    x.kind = 7;
                    memory[sp + -2] = x;
                    break;
                case 0x7b:
                case 0x7c:
                case 0x7d:
                    {
                        Val parent = c == 0x7d ? y : x;
                        int record = Object(Deref(parent), 4);
                        if (objects[record].type != (0x100000 | fieldOwners[a]) || fieldStatic[a] != 0)
                        {
                            Bad("invalid field owner");
                        }

                        int field = fieldSlots[a];
                        if (c == 0x7b)
                        {
                            memory[sp + -1] = objects[record].values[field];
                        }
                        else
                        {
                            Val addr = FieldAddress(parent, field);
                            if (c == 0x7c)
                            {
                                memory[sp + -1] = addr;
                            }
                            else
                            {
                                if (IntType(fieldTypes[a]))
                                {
                                    Int(x);
                                }

                                WriteAddress(addr.data, x, 0);
                            }
                        }

                        break;
                    }

                case 0x7e:
                    memory[sp + 0] = memory[globals + a];
                    break;
                case 0x7f:
                    memory[sp + 0] = SlotAddress(globals + a);
                    break;
                case 0x80:
                    if (IntType(fieldTypes[a]))
                    {
                        Int(x);
                    }

                    memory[globals + a] = x;
                    break;
                case 0x81:
                    if (y.kind != 5)
                    {
                        Bad("expected managed address");
                    }

                    WriteAddress(y.data, x, 0);
                    break;
                case 0x8c:
                    {
                        int box = NewObject(o.type, 1, 0);
                        objects[box].kind = 8;
                        objects[box].values[0] = x;
                        memory[sp + -1] = Ref(8, box);
                        break;
                    }

                case 0x8d:
                    memory[sp + -1] = NewArray(o.type, Int(x));
                    break;
                case 0x8e:
                    memory[sp + -1] = Integer(objects[Object(x, 3)].count);
                    break;
                case 0x8f:
                    memory[sp + -2] = ArrayAddress(y, Int(x));
                    break;
                case 0x90:
                case 0x91:
                case 0x92:
                case 0x93:
                case 0x94:
                case 0x95:
                case 0x9a:
                case 0xa3:
                    {
                        int index = Int(x);
                        int array = Element(y, index);
                        Val value = objects[array].values[index];
                        if (c == 0x90)
                        {
                            value = Integer((sbyte)Int(value));
                        }

                        if (c == 0x91)
                        {
                            value = Integer((byte)Int(value));
                        }

                        if (c == 0x92)
                        {
                            value = Integer((short)Int(value));
                        }

                        if (c == 0x93)
                        {
                            value = Integer((ushort)Int(value));
                        }

                        memory[sp + -2] = value;
                        break;
                    }

                case 0x9b:
                case 0x9c:
                case 0x9d:
                case 0x9e:
                case 0xa2:
                case 0xa4:
                    if (c == 0x9c)
                    {
                        x = Integer((byte)Int(x));
                    }

                    if (c == 0x9d)
                    {
                        x = Integer((ushort)Int(x));
                    }

                    int storeIndex = Int(y);
                    int storeArray = Element(memory[sp + -3], storeIndex);
                    objects[storeArray].values[storeIndex] = x;
                    break;
                case 0xa5:
                    {
                        int box = Object(x, 8);
                        if (objects[box].type != o.type)
                        {
                            Bad("unbox.any type mismatch");
                        }

                        memory[sp + -1] = objects[box].values[0];
                        break;
                    }

                case 0xfe01:
                    memory[sp + -2] = Integer(Equal(y, x, 1) ? 1 : 0);
                    break;
                case 0xfe06:
                    {
                        Val fn = default;
                        fn.kind = 6;
                        fn.data = o.method;
                        memory[sp + 0] = fn;
                        break;
                    }

                case 0xfe15:
                    if (x.kind != 5)
                    {
                        Bad("expected managed address");
                    }

                    WriteAddress(x.data, Zero(o.type, 0), 0);
                    break;
                default:
                    Bad("unhandled IL operation");
                    break;
            }
        }

        return 0;
    }

    public static void Enter(int mid, int frame)
    {
        if (++depth > 128)
        {
            Bad("call depth limit exceeded");
        }

        calls++;
        ref Body b = ref bodies[mid];
        int slots = b.nargs + b.nlocals + b.maxstack;
        if (frame < 0 || frame + slots > 16384)
        {
            Bad("value stack exhausted");
        }

        for (int i = 0; i < b.nargs; i++)
        {
            if (IntType(b.argtypes[i]))
            {
                Int(memory[frame + i]);
            }
        }

        for (int i = 0; i < b.nlocals; i++)
        {
            memory[frame + b.nargs + i] = b.init[i];
        }
    }

    public static void Leave(int mid, int frame)
    {
        ref Body b = ref bodies[mid];
        int slots = b.nargs + b.nlocals + b.maxstack;
        for (int i = 0; i < slots; i++)
        {
            memory[frame + i] = default(Val);
        }

        depth--;
    }

    public static void Charge(int count)
    {
        steps += count;
        if (steps > 10000000)
        {
            Bad("instruction budget exceeded");
        }
    }

    static Val Invoke(int mid, int frame)
    {
        if (mid < 0 || mid >= methodCount)
        {
            Bad("method index");
        }

        ref Body b = ref bodies[mid];
        if (jit != 0)
        {
            Val result = default;
            Host.Call(mid, frame, ref result);
            return result;
        }

        Enter(mid, frame);
        int pc = 0;
        int stack = frame + b.nargs + b.nlocals;
        while (true)
        {
            ref Op o = ref b.ops[pc];
            int c = o.code;
            Charge(1);
            if (c == 0x2a)
            {
                Val result = b.returns == 1 ? default(Val) : memory[stack + o.depth - 1];
                Leave(mid, frame);
                return result;
            }

            if (c == 0x2b || c == 0x38)
            {
                pc = o.operand;
                continue;
            }

            if (c == 0x2c || c == 0x39 || c == 0x2d || c == 0x3a)
            {
                bool truth = Truth(memory[stack + o.depth - 1]);
                if (truth == (c == 0x2d || c == 0x3a))
                {
                    pc = o.operand;
                    continue;
                }
            }
            else if (Slow(mid, pc, frame) != 0)
            {
                pc = o.operand;
                continue;
            }

            pc++;
        }
    }

    public static void Run(byte[] data, int length, int useJit)
    {
        Host.Reset();
        depth = steps = calls = totalElements = jit = output = 0;
        strings = blobs = users = tables = -1;
        stringsLength = blobsLength = usersLength = tablesLength = 0;
        sw = bw = gw = entry = methodCount = fieldCount = sectionCount = 0;
        typeName = typeNs = 0;
        ResetHeap();
        Load(data, length);
        if (bodies[entry].nargs == 1)
        {
            memory[0] = NewArray(14, 0);
        }
        else if (bodies[entry].nargs != 0)
        {
            Bad("entry signature");
        }

        if (useJit != 0)
        {
            Compile();
        }

        Invoke(entry, 0);
        Flush();
    }
}
