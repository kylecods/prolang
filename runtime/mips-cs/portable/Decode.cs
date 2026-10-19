namespace ProLang.Portable;

public static unsafe partial class VM
{
    static bool Owner(string ns, string name) { return Host.Equal(typeNs, ns) && Host.Equal(typeName, name); }
    static bool Shape(Signature s, int instance, int ret, int n, int a, int b, int c, int d, int e)
    {
        if (s.instance != instance || s.returns != ret || s.count != n) return false;
        return (n < 1 || s.types[0] == a) && (n < 2 || s.types[1] == b) && (n < 3 || s.types[2] == c) && (n < 4 || s.types[3] == d) && (n < 5 || s.types[4] == e);
    }
    static int Native(byte* name, Signature s)
    {
        if (Host.Equal(typeNs, "System") && (Host.Starts(typeName, "Func`") || Host.Starts(typeName, "Action")))
        {
            if (Host.Equal(name, "Invoke") && s.instance == 1) return 100;
            if (Host.Equal(name, ".ctor") && Shape(s, 1, 1, 2, 28, 24, 0, 0, 0)) return 101;
        }
        if (Owner("ProLang.Runtime", "PspOps"))
        {
            if (Host.Equal(name, "Init") && Shape(s, 0, 1, 0, 0, 0, 0, 0, 0)) return 1;
            if (Host.Equal(name, "Clear") && Shape(s, 0, 1, 1, 8, 0, 0, 0, 0)) return 2;
            if (Host.Equal(name, "FillRect") && Shape(s, 0, 1, 5, 8, 8, 8, 8, 8)) return 3;
            if (Host.Equal(name, "DrawLine") && Shape(s, 0, 1, 5, 8, 8, 8, 8, 8)) return 4;
            if (Host.Equal(name, "DrawText") && Shape(s, 0, 1, 4, 8, 8, 14, 8, 0)) return 5;
            if (Host.Equal(name, "SwapBuffers") && Shape(s, 0, 1, 0, 0, 0, 0, 0, 0)) return 6;
            if (Host.Equal(name, "Vsync") && Shape(s, 0, 1, 0, 0, 0, 0, 0, 0)) return 7;
            if (Host.Equal(name, "ButtonsHeld") && Shape(s, 0, 8, 0, 0, 0, 0, 0, 0)) return 8;
            if (Host.Equal(name, "ButtonPressed") && Shape(s, 0, 2, 1, 8, 0, 0, 0, 0)) return 9;
        }
        int first = s.count > 0 ? s.types[0] : 0;
        if (Owner("ProLang.Runtime", "Output"))
        {
            if (Host.Equal(name, "Initialize") && Shape(s, 0, 1, 0, 0, 0, 0, 0, 0)) return 10;
            if (Host.Equal(name, "Flush") && Shape(s, 0, 1, 0, 0, 0, 0, 0, 0)) return 11;
            if (Host.Equal(name, "Write") && s.instance == 0 && s.returns == 1 && s.count == 1 && (first == 2 || first == 3 || first == 8 || first == 9 || first == 14 || first == 28)) return 12;
        }
        if (Owner("ProLang.Runtime", "StringOps"))
        {
            if (Host.Equal(name, "From") && s.instance == 0 && s.returns == 14 && s.count == 1 && (first == 2 || first == 3 || first == 8 || first == 9 || first == 28)) return 13;
            if ((Host.Equal(name, "CharAt") && Shape(s, 0, 3, 2, 14, 8, 0, 0, 0)) || (Host.Equal(name, "CharCode") && Shape(s, 0, 8, 2, 14, 8, 0, 0, 0))) return 18;
            if (Host.Equal(name, "Substring") && Shape(s, 0, 14, 3, 14, 8, 8, 0, 0)) return 19;
        }
        if (Owner("System", "String"))
        {
            if (Host.Equal(name, "Concat") && (Shape(s, 0, 14, 2, 14, 14, 0, 0, 0) || Shape(s, 0, 14, 2, 28, 28, 0, 0, 0))) return 14;
            if (Host.Equal(name, "op_Equality") && Shape(s, 0, 2, 2, 14, 14, 0, 0, 0)) return 15;
            if (Host.Equal(name, "op_Inequality") && Shape(s, 0, 2, 2, 14, 14, 0, 0, 0)) return 16;
            if (Host.Equal(name, "get_Length") && Shape(s, 1, 8, 0, 0, 0, 0, 0, 0)) return 17;
            if (Host.Equal(name, "IndexOf") && Shape(s, 1, 8, 1, 14, 0, 0, 0, 0)) return 20;
        }
        if (Owner("System", "Math") && Shape(s, 0, 8, 2, 8, 8, 0, 0, 0))
        { if (Host.Equal(name, "Min")) return 21; if (Host.Equal(name, "Max")) return 22; }
        if (Owner("ProLang.Runtime", "TestOps") && Host.Equal(name, "Assert") && Shape(s, 0, 1, 2, 2, 14, 0, 0, 0)) return 23;
        if (Owner("ProLang.Runtime", "ConsoleOps") && Host.Equal(name, "Write") && Shape(s, 0, 1, 1, 14, 0, 0, 0, 0)) return 24;
        if (Owner("System.Threading", "Thread") && Host.Equal(name, "Sleep") && Shape(s, 0, 1, 1, 8, 0, 0, 0, 0)) return 25;
        if (Owner("ProLang.Runtime", "TimeOps") && Host.Equal(name, "Millis") && Shape(s, 0, 8, 0, 0, 0, 0, 0, 0)) return 26;
        return Bad("unsupported native signature");
    }
    static void Site(Op* op, uint token)
    {
        int row = (int)(token & 0xffffff);
        if ((token >> 24) == 6)
        {
            if (row < 1 || row > methodCount) Bad("method token"); Body* b = bodies + row - 1;
            op->method = row - 1; op->count = b->nargs; op->returns = b->returns == 1 ? 0 : 1; op->resultType = b->returns; return;
        }
        if ((token >> 24) != 10) Bad("call token");
        Reader r = Row(10, row); int parent = Index(&r, Mr()); int tag = parent & 7;
        int table = tag == 0 ? 2 : (tag == 1 ? 1 : (tag == 4 ? 27 : Bad("member parent")));
        TypeName(((uint)table << 24) | (uint)(parent >> 3), 0); byte* name = Text(Index(&r, sw)); Signature sig = Sig(Index(&r, bw));
        if (table == 27)
        {
            Reader tr = Row(27, parent >> 3); Reader ts = Blob(Index(&tr, bw)); U8(&ts); U8(&ts); Compressed(&ts); int count = Compressed(&ts); if (count > 16) Bad("generic arity");
            int* args = (int*)Perm(count * 4); for (int i = 0; i < count; i++) args[i] = ReadType(&ts, 0);
            for (int i = 0; i < sig.count; i++) if ((sig.types[i] & 0xfffff000) == 0x1000) { int n = sig.types[i] & 4095; if (n >= count) Bad("generic parameter"); sig.types[i] = args[n]; }
            if ((sig.returns & 0xfffff000) == 0x1000) { int n = sig.returns & 4095; if (n >= count) Bad("generic return"); sig.returns = args[n]; }
        }
        op->native = Native(name, sig); op->count = sig.count + sig.instance; op->returns = sig.returns == 1 ? 0 : 1; op->resultType = sig.returns; op->type = sig.count > 0 ? sig.types[0] : 0;
    }
    static int Find(Body* b, int offset)
    {
        int low = 0, high = b->count;
        while (low < high) { int m = (low + high) / 2; if (b->ops[m].offset < offset) low = m + 1; else high = m; }
        if (low >= b->count || b->ops[low].offset != offset) Bad("invalid branch target"); return low;
    }
    static void Decode(int method)
    {
        Body* b = bodies + method; int at = Rva((uint)b->rva, 1); Reader r = Read(image, imageLength); r.pos = at;
        int first = U8(&r), length = 0, localToken = 0; b->maxstack = 8;
        if ((first & 3) == 2) length = first >> 2;
        else if ((first & 3) == 3)
        {
            int flags = first | (U8(&r) << 8); if ((flags & 8) != 0 || (flags >> 12) < 3) Bad("unsupported method header or exception sections");
            b->maxstack = U16(&r); length = (int)U32(&r); localToken = (int)U32(&r); Take(&r, (flags >> 12) * 4 - 12);
        }
        else Bad("invalid method header");
        if (b->maxstack > 1024 || length < 1) Bad("method limits");
        Rva((uint)b->rva, r.pos - at + length);
        if (localToken != 0)
        {
            if (((uint)localToken >> 24) != 17) Bad("local signature token");
            Reader row = Row(17, localToken & 0xffffff); Reader ls = Blob(Index(&row, bw)); if (U8(&ls) != 7) Bad("locals signature");
            b->nlocals = Compressed(&ls); if (b->nlocals > 1024) Bad("local count limit"); b->locals = (int*)Perm(b->nlocals * 4); b->init = (Val*)Perm(b->nlocals * sizeof(Val));
            for (int i = 0; i < b->nlocals; i++) { b->locals[i] = ReadType(&ls, 0); b->init[i] = Zero(b->locals[i], 0); }
        }
        Reader il = Read(Take(&r, length), length); b->ops = (Op*)Perm(length * sizeof(Op));
        while (il.pos < length)
        {
            Op* op = b->ops + b->count++; op->offset = il.pos; op->method = -1; op->depth = -1;
            int code = U8(&il); if (code == 254) code = 0xfe00 | U8(&il); op->code = code;
            if ((code >= 0x0e && code <= 0x13) || code == 0x1f || (code >= 0x2b && code <= 0x37)) op->operand = code >= 0x1f ? (sbyte)U8(&il) : U8(&il);
            else if (code == 0x20 || code == 0x28 || (code >= 0x38 && code <= 0x44) || code == 0x6f || (code >= 0x71 && code <= 0x73) || (code >= 0x7b && code <= 0x81) || code == 0x8c || code == 0x8d || code == 0x8f || code == 0xa3 || code == 0xa4 || code == 0xa5 || code == 0xfe06 || code == 0xfe15) op->operand = (int)U32(&il);
            else if (code >= 0xfe09 && code <= 0xfe0e) op->operand = U16(&il);
            else if (!(code == 0 || (code >= 2 && code <= 13) || (code >= 0x14 && code <= 0x1e) || code == 0x25 || code == 0x26 || code == 0x2a || (code >= 0x58 && code <= 0x69) || code == 0x6d || code == 0x8e || (code >= 0x90 && code <= 0x95) || (code >= 0x9a && code <= 0x9e) || code == 0xa2 || code == 0xd1 || code == 0xd2 || code == 0xd3 || code == 0xe0 || (code >= 0xfe01 && code <= 0xfe05))) Bad("unsupported IL opcode");
            if (code >= 0x2b && code <= 0x44) op->operand += il.pos;
            if (code == 0x28 || code == 0x6f || code == 0x73 || code == 0xfe06) Site(op, (uint)op->operand);
            if (code >= 0x7b && code <= 0x80) op->operand = FieldIndex((uint)op->operand);
            if (code == 0x8c || code == 0x8d || code == 0xa5 || code == 0xfe15 || code == 0x71 || code == 0x81 || code == 0xa3 || code == 0xa4 || code == 0x8f) op->type = TypeFromToken((uint)op->operand);
            if (code == 0x72)
            {
                if (((uint)op->operand >> 24) != 0x70) Bad("literal token"); Reader us = Read(users, usersLength); us.pos = op->operand & 0xffffff;
                int bytes = Compressed(&us); if (bytes < 1 || (bytes & 1) == 0) Bad("literal length"); byte* p = Take(&us, bytes);
                Obj* text = NewText((bytes - 1) / 2, 1); for (int i = 0; i < text->count; i++) text->chars[i] = (ushort)(p[i * 2] | (p[i * 2 + 1] << 8)); op->literal = Ref(2, text);
            }
        }
        for (int i = 0; i < b->count; i++) if (b->ops[i].code >= 0x2b && b->ops[i].code <= 0x44) b->ops[i].operand = Find(b, b->ops[i].operand);
    }
    static int Pop(Op* o, int returns)
    {
        int c = o->code;
        if (c == 0 || (c >= 2 && c <= 9) || c == 0x0e || c == 0x0f || c == 0x11 || c == 0x12 || (c >= 0x14 && c <= 0x20) || c == 0x2b || c == 0x38 || c == 0x72 || c == 0x7e || c == 0x7f || c == 0xfe06 || c == 0xfe09 || c == 0xfe0a || c == 0xfe0c || c == 0xfe0d) return 0;
        if (c == 0x28 || c == 0x6f) return o->count;
        if (c == 0x73) return o->count - 1;
        if (c == 0x2a) return returns == 1 ? 0 : 1;
        if (c == 0x9b || c == 0x9c || c == 0x9d || c == 0x9e || c == 0xa2 || c == 0xa4) return 3;
        if ((c >= 0x2e && c <= 0x37) || (c >= 0x3b && c <= 0x44) || (c >= 0x58 && c <= 0x64) || c == 0x7d || c == 0x81 || c == 0x8f || (c >= 0x90 && c <= 0x95) || c == 0x9a || c == 0xa3 || (c >= 0xfe01 && c <= 0xfe05)) return 2;
        return 1;
    }
    static int Push(Op* o)
    {
        int c = o->code;
        if (c == 0 || (c >= 0x0a && c <= 0x0d) || c == 0x10 || c == 0x13 || c == 0x26 || (c >= 0x2a && c <= 0x44) || c == 0x7d || c == 0x80 || c == 0x81 || (c >= 0x9b && c <= 0x9e) || c == 0xa2 || c == 0xa4 || c == 0xfe0b || c == 0xfe0e || c == 0xfe15) return 0;
        if (c == 0x28 || c == 0x6f) return o->returns;
        if (c == 0x25) return 2;
        return 1;
    }
    static void Visit(Body* b, int pc, int d, int* queue, int* tail)
    {
        if (pc < 0 || pc >= b->count) Bad("control falls outside method");
        if (b->ops[pc].depth == -1) { b->ops[pc].depth = d; queue[(*tail)++] = pc; }
        else if (b->ops[pc].depth != d) Bad("inconsistent stack at branch");
    }
    static void Verify(int method)
    {
        Body* b = bodies + method; int* queue = (int*)Perm(b->count * 4); int head = 0, tail = 0;
        Visit(b, 0, 0, queue, &tail);
        for (int i = 0; i < b->count; i++)
        {
            Op* o = b->ops + i; int c = o->code, slot = o->operand;
            if (c >= 6 && c <= 13) slot = (c - 6) & 3;
            if (((c >= 6 && c <= 13) || c == 0x11 || c == 0x12 || c == 0x13 || c == 0xfe0c || c == 0xfe0d || c == 0xfe0e) && (slot < 0 || slot >= b->nlocals)) Bad("local index");
            slot = c >= 2 && c <= 5 ? c - 2 : o->operand;
            if (((c >= 2 && c <= 5) || c == 0x0e || c == 0x0f || c == 0x10 || c == 0xfe09 || c == 0xfe0a || c == 0xfe0b) && (slot < 0 || slot >= b->nargs)) Bad("argument index");
        }
        while (head < tail)
        {
            int pc = queue[head++]; Op* o = b->ops + pc; int pop = Pop(o, b->returns), next = o->depth - pop + Push(o);
            if (o->depth < pop || next > b->maxstack) Bad("invalid evaluation stack");
            if (o->code == 0x2a) { if (next != 0) Bad("invalid return stack"); continue; }
            if (o->code >= 0x2b && o->code <= 0x44) Visit(b, o->operand, next, queue, &tail);
            if (o->code != 0x2b && o->code != 0x38) Visit(b, pc + 1, next, queue, &tail);
        }
    }
}
