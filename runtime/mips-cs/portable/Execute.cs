namespace ProLang.Portable;

public static unsafe partial class VM
{
    static Val Integer(int n) { Val v = default; v.kind = 1; v.data = n; return v; }
    static Val Ref(int kind, void* p) { Val v = default; v.kind = kind; v.data = (nint)p; return v; }
    static int Int(Val v) { if (v.kind != 1) Bad("expected an int32 value"); return (int)v.data; }
    static bool IntType(int type) { return type >= 2 && type <= 9; }
    static Obj* Object(Val v, int kind) { if (v.kind != kind || v.data == 0) Bad("invalid managed reference"); return (Obj*)v.data; }
    static Obj* NewObject(int type, int count, int permanent)
    {
        if (count < 0 || count > 262144) Bad("object size limit"); int bytes = sizeof(Obj) + count * sizeof(Val);
        Obj* o = (Obj*)(permanent != 0 ? Perm(bytes) : Alloc(bytes)); o->type = type; o->count = count; o->values = (Val*)(o + 1); return o;
    }
    static Obj* NewText(int count, int permanent)
    {
        if (count < 0 || count > 32768) Bad("string length limit"); int bytes = sizeof(Obj) + (count + 1) * 2;
        Obj* o = (Obj*)(permanent != 0 ? Perm(bytes) : Alloc(bytes)); o->type = 14; o->count = count; o->chars = (ushort*)(o + 1); return o;
    }
    static Val Zero(int type, int nesting)
    {
        if (nesting > 32) Bad("recursive struct");
        if ((type & 0xfff00000) == 0x100000)
        {
            int row = type & 0xfffff; if (row < 1 || row > rows[2]) Bad("struct type");
            if (zeros[row].kind == 4) return zeros[row];
            int count = 0; for (int i = 0; i < fieldCount; i++) if (fieldOwners[i] == row && fieldStatic[i] == 0) count++;
            Obj* o = NewObject(type, count, 1);
            for (int i = 0; i < fieldCount; i++) if (fieldOwners[i] == row && fieldStatic[i] == 0) o->values[fieldSlots[i]] = Zero(fieldTypes[i], nesting + 1);
            zeros[row] = Ref(4, o); return zeros[row];
        }
        if (type >= 2 && type <= 9) return Integer(0);
        return default(Val);
    }
    static Val NewArray(int type, int count)
    {
        if (count < 0 || count > 262144 - totalElements) Bad("array heap limit exceeded"); totalElements += count;
        Obj* array = NewObject(type, count, 0); Val value = Zero(type, 0);
        for (int i = 0; i < count; i++) array->values[i] = value;
        return Ref(3, array);
    }
    static Val* Element(Val array, int index)
    {
        if (array.kind != 3) Bad("expected non-null array"); Obj* obj = Object(array, 3);
        if (index < 0 || index >= obj->count) Bad("array index out of bounds"); return obj->values + index;
    }
    static Val SlotAddress(Val* slot) { Address* a = (Address*)Alloc(sizeof(Address)); a->root = slot; return Ref(5, a); }
    static Val FieldAddress(Val parent, int field)
    {
        if (parent.kind != 5) Bad("field store needs a managed address");
        Address* a = (Address*)Alloc(sizeof(Address)); a->parent = (Address*)parent.data; a->index = field; return Ref(5, a);
    }
    static Val ReadAddress(Address* a, int nesting)
    {
        if (nesting > 32 || a == null) Bad("invalid managed address");
        if (a->parent == null) return *a->root;
        Obj* parent = Object(ReadAddress(a->parent, nesting + 1), 4); if (a->index < 0 || a->index >= parent->count) Bad("field index"); return parent->values[a->index];
    }
    static void WriteAddress(Address* a, Val value, int nesting)
    {
        if (nesting > 32 || a == null) Bad("invalid managed address");
        if (a->parent == null) { *a->root = value; return; }
        Obj* old = Object(ReadAddress(a->parent, nesting + 1), 4); if (a->index < 0 || a->index >= old->count) Bad("field index");
        Obj* copy = NewObject(old->type, old->count, 0); for (int i = 0; i < old->count; i++) copy->values[i] = old->values[i]; copy->values[a->index] = value;
        WriteAddress(a->parent, Ref(4, copy), nesting + 1);
    }
    static Val Deref(Val v) { if (v.kind == 5) return ReadAddress((Address*)v.data, 0); return v; }
    static bool Equal(Val a, Val b, int identity)
    {
        if (a.kind != b.kind) return false;
        if (a.kind == 2)
        {
            if (identity != 0) Bad("string reference identity comparison is unsupported"); Obj* x = Object(a, 2); Obj* y = Object(b, 2);
            if (x->count != y->count) return false; for (int i = 0; i < x->count; i++) if (x->chars[i] != y->chars[i]) return false; return true;
        }
        return a.data == b.data;
    }
    static bool Truth(Val v) { return v.kind != 0 && (v.kind != 1 || v.data != 0); }
    static Obj* Concat(Obj* a, Obj* b)
    {
        Obj* text = NewText(a->count + b->count, 0); for (int i = 0; i < a->count; i++) text->chars[i] = a->chars[i]; for (int i = 0; i < b->count; i++) text->chars[a->count + i] = b->chars[i]; return text;
    }
    static Obj* Ascii(string s) { int n = Host.Length(s); Obj* text = NewText(n, 0); Host.Ascii(s, text->chars, n); return text; }
    static Obj* Format(Val v, int declared)
    {
        if (v.kind == 8) { Obj* box = Object(v, 8); return Format(box->values[0], box->type); }
        if (v.kind == 0) return NewText(0, 0);
        if (v.kind == 2) return Object(v, 2);
        int n = Int(v);
        if (declared == 2) return Ascii(n == 0 ? "False" : "True");
        if (declared == 3) { Obj* ch = NewText(1, 0); ch->chars[0] = (ushort)n; return ch; }
        Obj* text = NewText(16, 0); int length = Host.Number(n, declared == 9, text->chars); text->count = length; return text;
    }
    static void Flush()
    {
        if (output != null && output->count > 0) Host.Write(output->chars, output->count);
        output = null;
    }
    static Val NativeCall(Op* op, Val* args)
    {
        int id = op->native;
        switch (id)
        {
            case 1: Host.Init(); break;
            case 2: Host.Clear(Int(args[0])); break;
            case 3: Host.Rect(Int(args[0]), Int(args[1]), Int(args[2]), Int(args[3]), Int(args[4])); break;
            case 4: Host.Line(Int(args[0]), Int(args[1]), Int(args[2]), Int(args[3]), Int(args[4])); break;
            case 5: { Obj* text = Object(args[2], 2); Host.Text(Int(args[0]), Int(args[1]), text->chars, text->count, Int(args[3])); break; }
            case 6: Host.Present(steps, calls); steps = 0; calls = 0; Host.CollectStats(); break;
            case 7: Host.Vsync(); break;
            case 8: return Integer(Host.Buttons());
            case 9: return Integer((Host.Buttons() & Int(args[0])) != 0 ? 1 : 0);
            case 10: output = null; break;
            case 11: Flush(); break;
            case 12:
            {
                Obj* formatted = Format(args[0], op->type); Obj* newline = NewText(formatted->count + 1, 0); for (int i = 0; i < formatted->count; i++) newline->chars[i] = formatted->chars[i]; newline->chars[formatted->count] = 10;
                output = output == null ? newline : Concat(output, newline); break;
            }
            case 13: return Ref(2, Format(args[0], op->type));
            case 14: return Ref(2, Concat(Format(args[0], 28), Format(args[1], 28)));
            case 15: return Integer(Equal(args[0], args[1], 0) ? 1 : 0);
            case 16: return Integer(Equal(args[0], args[1], 0) ? 0 : 1);
            case 17: return Integer(Object(args[0], 2)->count);
            case 18:
            {
                Obj* text = Object(args[0], 2); int index = Int(args[1]); if (index < 0 || index >= text->count) Bad("string index out of bounds"); return Integer(text->chars[index]);
            }
            case 19:
            {
                Obj* text = Object(args[0], 2); int start = Int(args[1]), end = Int(args[2]); if (start < 0 || end < start || end > text->count) Bad("substring range");
                Obj* result = NewText(end - start, 0); for (int i = 0; i < end - start; i++) result->chars[i] = text->chars[start + i]; return Ref(2, result);
            }
            case 20:
            {
                Obj* text = Object(args[0], 2); Obj* needle = Object(args[1], 2);
                for (int i = 0; i <= text->count - needle->count; i++) { int same = 1; for (int j = 0; j < needle->count; j++) if (text->chars[i + j] != needle->chars[j]) same = 0; if (same != 0) return Integer(i); } return Integer(-1);
            }
            case 21: { int a = Int(args[0]), b = Int(args[1]); return Integer(a < b ? a : b); }
            case 22: { int a = Int(args[0]), b = Int(args[1]); return Integer(a > b ? a : b); }
            case 23: if (Int(args[0]) == 0) { Flush(); Obj* message = Object(args[1], 2); Host.Write(message->chars, message->count); Bad("assertion failed"); } break;
            case 24: { Flush(); Obj* text = Object(args[0], 2); Host.Write(text->chars, text->count); break; }
            case 25: { int n = Int(args[0]); if (n < 0 || n > 60000) Bad("sleep range"); Host.Sleep(n); break; }
            case 26: return Integer(Host.Millis());
            default: Bad("unsupported native call"); break;
        }
        return default(Val);
    }
    static int Binary(int code, int a, int b)
    {
        unchecked
        {
            switch (code)
            {
                case 0x58: return a + b;
                case 0x59: return a - b;
                case 0x5a: return a * b;
                case 0x5b: if (b == 0) Bad("division by zero"); if (a == -2147483647 - 1 && b == -1) Bad("int32 division overflow"); return a / b;
                case 0x5c: if (b == 0) Bad("division by zero"); return (int)((uint)a / (uint)b);
                case 0x5d: if (b == 0) Bad("division by zero"); return b == -1 ? 0 : a % b;
                case 0x5e: if (b == 0) Bad("division by zero"); return (int)((uint)a % (uint)b);
                case 0x5f: return a & b;
                case 0x60: return a | b;
                case 0x61: return a ^ b;
                case 0x62: return a << (b & 31);
                case 0x63: return a >> (b & 31);
                case 0x64: return (int)((uint)a >> (b & 31));
                case 0xfe02: return a > b ? 1 : 0;
                case 0xfe03: return (uint)a > (uint)b ? 1 : 0;
                case 0xfe04: return a < b ? 1 : 0;
                case 0xfe05: return (uint)a < (uint)b ? 1 : 0;
            }
        }
        return Bad("arithmetic operation");
    }
    // Returns a branch decision. Every other operation returns zero.
    public static int Slow(int mid, int pc, Val* frame)
    {
        Body* b = bodies + mid; Op* o = b->ops + pc; int c = o->code, a = o->operand;
        Val* locals = frame + b->nargs; Val* stack = locals + b->nlocals; Val* sp = stack + o->depth;
        Val x = o->depth > 0 ? sp[-1] : default(Val), y = o->depth > 1 ? sp[-2] : default(Val);
        if (c >= 2 && c <= 5) sp[0] = frame[c - 2];
        else if (c >= 6 && c <= 9) sp[0] = locals[c - 6];
        else if (c >= 10 && c <= 13) locals[c - 10] = x;
        else if (c >= 0x15 && c <= 0x1e) sp[0] = Integer(c - 0x16);
        else if ((c >= 0x58 && c <= 0x64) || (c >= 0xfe02 && c <= 0xfe05)) sp[-2] = Integer(Binary(c, Int(y), Int(x)));
        else if ((c >= 0x2e && c <= 0x37) || (c >= 0x3b && c <= 0x44))
        {
            int compare = c <= 0x37 ? c - 0x2e : c - 0x3b;
            switch (compare)
            {
                case 0: return Equal(y, x, 1) ? 1 : 0;
                case 5: return Equal(y, x, 1) ? 0 : 1;
                case 1: return Int(y) >= Int(x) ? 1 : 0;
                case 2: return Int(y) > Int(x) ? 1 : 0;
                case 3: return Int(y) <= Int(x) ? 1 : 0;
                case 4: return Int(y) < Int(x) ? 1 : 0;
                case 6: return (uint)Int(y) >= (uint)Int(x) ? 1 : 0;
                case 7: return (uint)Int(y) > (uint)Int(x) ? 1 : 0;
                case 8: return (uint)Int(y) <= (uint)Int(x) ? 1 : 0;
                case 9: return (uint)Int(y) < (uint)Int(x) ? 1 : 0;
            }
        }
        else switch (c)
        {
            case 0: case 0x26: break;
            case 0x0e: case 0xfe09: sp[0] = frame[a]; break;
            case 0x0f: case 0xfe0a: sp[0] = SlotAddress(frame + a); break;
            case 0x10: case 0xfe0b: frame[a] = x; break;
            case 0x11: case 0xfe0c: sp[0] = locals[a]; break;
            case 0x12: case 0xfe0d: sp[0] = SlotAddress(locals + a); break;
            case 0x13: case 0xfe0e: locals[a] = x; break;
            case 0x14: sp[0] = default(Val); break;
            case 0x1f: case 0x20: sp[0] = Integer(a); break;
            case 0x25: sp[0] = x; break;
            case 0x28: case 0x6f:
            {
                Val* args = sp - o->count; Val result = default;
                if (o->method >= 0) result = Invoke(o->method, args);
                else if (o->native == 100)
                {
                    if (args[0].kind != 7) Bad("Invoke on invalid delegate"); int target = (int)args[0].data;
                    if (target < 0 || target >= methodCount || bodies[target].nargs != o->count - 1) Bad("delegate signature mismatch");
                    result = Invoke(target, args + 1);
                }
                else result = NativeCall(o, args);
                if (o->returns != 0) args[0] = result;
                break;
            }
            case 0x65: sp[-1] = Integer(unchecked(-Int(x))); break;
            case 0x66: sp[-1] = Integer(~Int(x)); break;
            case 0x67: sp[-1] = Integer((sbyte)Int(x)); break;
            case 0x68: sp[-1] = Integer((short)Int(x)); break;
            case 0x69: case 0x6d: case 0xd3: case 0xe0: Int(x); break;
            case 0xd1: sp[-1] = Integer((ushort)Int(x)); break;
            case 0xd2: sp[-1] = Integer((byte)Int(x)); break;
            case 0x71: if (x.kind != 5) Bad("expected managed address"); sp[-1] = ReadAddress((Address*)x.data, 0); break;
            case 0x72: sp[0] = o->literal; break;
            case 0x73: if (o->native != 101 || x.kind != 6 || y.kind != 0) Bad("unsupported constructor"); x.kind = 7; sp[-2] = x; break;
            case 0x7b: case 0x7c: case 0x7d:
            {
                Val parent = c == 0x7d ? y : x; Obj* record = Object(Deref(parent), 4);
                if (record->type != (0x100000 | fieldOwners[a]) || fieldStatic[a] != 0) Bad("invalid field owner"); int field = fieldSlots[a];
                if (c == 0x7b) sp[-1] = record->values[field];
                else { Val addr = FieldAddress(parent, field); if (c == 0x7c) sp[-1] = addr; else { if (IntType(fieldTypes[a])) Int(x); WriteAddress((Address*)addr.data, x, 0); } }
                break;
            }
            case 0x7e: sp[0] = globals[a]; break;
            case 0x7f: sp[0] = SlotAddress(globals + a); break;
            case 0x80: if (IntType(fieldTypes[a])) Int(x); globals[a] = x; break;
            case 0x81: if (y.kind != 5) Bad("expected managed address"); WriteAddress((Address*)y.data, x, 0); break;
            case 0x8c: { Obj* box = NewObject(o->type, 1, 0); box->values[0] = x; sp[-1] = Ref(8, box); break; }
            case 0x8d: sp[-1] = NewArray(o->type, Int(x)); break;
            case 0x8e: sp[-1] = Integer(Object(x, 3)->count); break;
            case 0x8f: sp[-2] = SlotAddress(Element(y, Int(x))); break;
            case 0x90: case 0x91: case 0x92: case 0x93: case 0x94: case 0x95: case 0x9a: case 0xa3:
            {
                Val value = *Element(y, Int(x));
                if (c == 0x90) value = Integer((sbyte)Int(value)); if (c == 0x91) value = Integer((byte)Int(value));
                if (c == 0x92) value = Integer((short)Int(value)); if (c == 0x93) value = Integer((ushort)Int(value)); sp[-2] = value; break;
            }
            case 0x9b: case 0x9c: case 0x9d: case 0x9e: case 0xa2: case 0xa4:
                if (c == 0x9c) x = Integer((byte)Int(x)); if (c == 0x9d) x = Integer((ushort)Int(x)); *Element(sp[-3], Int(y)) = x; break;
            case 0xa5: { Obj* box = Object(x, 8); if (box->type != o->type) Bad("unbox.any type mismatch"); sp[-1] = box->values[0]; break; }
            case 0xfe01: sp[-2] = Integer(Equal(y, x, 1) ? 1 : 0); break;
            case 0xfe06: { Val fn = default; fn.kind = 6; fn.data = o->method; sp[0] = fn; break; }
            case 0xfe15: if (x.kind != 5) Bad("expected managed address"); WriteAddress((Address*)x.data, Zero(o->type, 0), 0); break;
            default: Bad("unhandled IL operation"); break;
        }
        return 0;
    }
    public static void Enter(int mid, Val* frame)
    {
        if (++depth > 128) Bad("call depth limit exceeded"); calls++;
        Body* b = bodies + mid; int slots = b->nargs + b->nlocals + b->maxstack;
        if (frame < memory || frame + slots > memory + 16384) Bad("value stack exhausted");
        for (int i = 0; i < b->nargs; i++) if (IntType(b->argtypes[i])) Int(frame[i]);
        for (int i = 0; i < b->nlocals; i++) frame[b->nargs + i] = b->init[i];
    }
    public static void Leave(int mid, Val* frame)
    {
        Body* b = bodies + mid; int slots = b->nargs + b->nlocals + b->maxstack;
        for (int i = 0; i < slots; i++) frame[i] = default(Val); depth--;
    }
    public static void Charge(int count) { steps += count; if (steps > 10000000) Bad("instruction budget exceeded"); }
    static Val Invoke(int mid, Val* frame)
    {
        if (mid < 0 || mid >= methodCount) Bad("method index");
        Body* b = bodies + mid;
        if (jit != 0) { Val result = default; Host.Call(b->machine, frame, &result); return result; }
        Enter(mid, frame); int pc = 0; Val* stack = frame + b->nargs + b->nlocals;
        while (true)
        {
            Op* o = b->ops + pc; int c = o->code; Charge(1);
            if (c == 0x2a) { Val result = b->returns == 1 ? default(Val) : stack[o->depth - 1]; Leave(mid, frame); return result; }
            if (c == 0x2b || c == 0x38) { pc = o->operand; continue; }
            if (c == 0x2c || c == 0x39 || c == 0x2d || c == 0x3a)
            {
                bool truth = Truth(stack[o->depth - 1]); if (truth == (c == 0x2d || c == 0x3a)) { pc = o->operand; continue; }
            }
            else if (Slow(mid, pc, frame) != 0) { pc = o->operand; continue; }
            pc++;
        }
    }
    public static void Run(byte* data, int length, int useJit)
    {
        Load(data, length);
        if (bodies[entry].nargs == 1) memory[0] = NewArray(14, 0);
        else if (bodies[entry].nargs != 0) Bad("entry signature");
        if (useJit != 0) Compile();
        Invoke(entry, memory); Flush();
    }
}
