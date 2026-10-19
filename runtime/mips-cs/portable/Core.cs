namespace ProLang.Portable;

// On-device runtime core. This deliberately uses a checked, unmanaged C# subset so the
// identical source can be built by Roslyn and by cs-to-c for PSP. Host is the platform ABI.
public unsafe struct Val { public int kind; public nint data; }
public unsafe struct Obj { public int type, count; public Val* values; public ushort* chars; }
public unsafe struct Address { public Val* root; public Address* parent; public int index; }
public unsafe struct Reader { public byte* bytes; public int length, pos; }
public unsafe struct Section { public uint rva, raw, size; }
public unsafe struct Signature { public int count, returns, instance; public int* types; }
public unsafe struct Op
{
    public int code, operand, offset, depth, method, native, count, returns, type, resultType;
    public Val literal;
}
public unsafe struct Body
{
    public byte* name;
    public int rva, sig, nargs, returns, nlocals, maxstack, count;
    public int* locals;
    public int* argtypes;
    public Val* init;
    public Op* ops;
    public uint* machine;
}

public static unsafe partial class VM
{
    static byte* image, strings, blobs, users, tables;
    static int imageLength, stringsLength, blobsLength, usersLength, tablesLength;
    static int sw, bw, gw, entry, methodCount, fieldCount, sectionCount;
    static int* rows, offsets, widths, fieldTypes, fieldOwners, fieldSlots, fieldStatic;
    static byte** fieldNames;
    static Section* sections;
    static Body* bodies;
    static Val* memory;
    static Val* globals;
    static Val* zeros;
    static byte* typeName;
    static byte* typeNs;
    static int depth, steps, calls, totalElements, jit;
    static Obj* output;
    static int Bad(string message) { Host.Fail(message); return 0; }
    static void* Perm(int n) { if (n < 0 || n > 16 * 1024 * 1024) Bad("permanent allocation limit"); return Host.Perm(n); }
    static void* Alloc(int n) { if (n < 0 || n > 8 * 1024 * 1024) Bad("managed allocation limit"); return Host.Alloc(n); }
    static Reader Read(byte* p, int n) { Reader r = default; r.bytes = p; r.length = n; return r; }
    static byte* Take(Reader* r, int n)
    {
        if (n < 0 || r->pos < 0 || r->pos > r->length - n) Bad("binary read outside image");
        byte* p = r->bytes + r->pos; r->pos += n; return p;
    }
    static int U8(Reader* r) { return *Take(r, 1); }
    static int U16(Reader* r) { byte* p = Take(r, 2); return p[0] | (p[1] << 8); }
    static uint U32(Reader* r) { byte* p = Take(r, 4); return (uint)(p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24)); }
    static int Index(Reader* r, int size) { return size == 2 ? U16(r) : (int)U32(r); }
    static int Compressed(Reader* r)
    {
        int b = U8(r);
        if ((b & 128) == 0) return b;
        if ((b & 192) == 128) return ((b & 63) << 8) | U8(r);
        if ((b & 224) == 192) return ((b & 31) << 24) | (U8(r) << 16) | (U8(r) << 8) | U8(r);
        return Bad("invalid compressed integer");
    }
    static byte* Z(Reader* r)
    {
        byte* p = r->bytes + r->pos;
        while (U8(r) != 0) { }
        return p;
    }
    static int Coded(int bits, ulong mask)
    {
        for (int t = 0; t < 45; t++) if ((mask & (1UL << t)) != 0 && rows[t] >= (1 << (16 - bits))) return 4;
        return 2;
    }
    static int Td() { return Coded(2, (1UL << 2) | (1UL << 1) | (1UL << 27)); }
    static int Mr() { return Coded(3, (1UL << 2) | (1UL << 1) | (1UL << 26) | (1UL << 6) | (1UL << 27)); }
    static int RowSize(int t)
    {
        switch (t)
        {
            case 0: return 2 + sw + 3 * gw;
            case 1: return Coded(2, 1UL | (1UL << 26) | (1UL << 35) | 2UL) + 2 * sw;
            case 2: return 8 + 2 * sw + Td();
            case 3: case 5: case 7: case 19: case 22: return 2;
            case 4: return 2 + sw + bw;
            case 6: return 10 + sw + bw;
            case 8: return 4 + sw;
            case 9: return 2 + Td();
            case 10: return Mr() + sw + bw;
            case 11: return 2 + Coded(2, (1UL << 4) | (1UL << 8) | (1UL << 23)) + bw;
            case 12: return Coded(5, (1UL << 45) - 1) + Coded(3, (1UL << 6) | (1UL << 10)) + bw;
            case 13: return Coded(1, (1UL << 4) | (1UL << 8)) + bw;
            case 14: return 2 + Coded(2, (1UL << 2) | (1UL << 6) | (1UL << 32)) + bw;
            case 15: return 8;
            case 16: return 6;
            case 17: case 27: return bw;
            case 18: case 21: case 41: return 4;
            case 20: return 2 + sw + Td();
            case 23: return 2 + sw + bw;
            case 24: return 4 + Coded(1, (1UL << 20) | (1UL << 23));
            case 25: return 2 + 2 * Coded(1, (1UL << 6) | (1UL << 10));
            case 26: return sw;
            case 28: return 4 + sw + Coded(1, (1UL << 4) | (1UL << 6));
            case 29: return 6;
            case 30: return 8;
            case 31: case 33: return 4;
            case 32: return 16 + bw + 2 * sw;
            case 34: case 36: return 12;
            case 35: return 12 + 2 * bw + 2 * sw;
            case 37: return 14;
            case 38: return 4 + sw + bw;
            case 39: case 40: return 8 + 2 * sw + Coded(2, (1UL << 38) | (1UL << 35) | (1UL << 39));
            case 42: return 4 + Coded(1, (1UL << 2) | (1UL << 6)) + sw;
            case 43: return Coded(1, (1UL << 6) | (1UL << 10)) + bw;
            case 44: return 2 + Td();
        }
        return Bad("unsupported metadata table");
    }
    static Reader Row(int t, int n)
    {
        if (t < 0 || t >= 45 || n < 1 || n > rows[t]) Bad("invalid metadata row");
        return Read(tables + offsets[t] + (n - 1) * widths[t], widths[t]);
    }
    static Reader Blob(int n)
    {
        Reader r = Read(blobs, blobsLength); r.pos = n; int size = Compressed(&r); byte* b = Take(&r, size); return Read(b, size);
    }
    static byte* Text(int n) { Reader r = Read(strings, stringsLength); r.pos = n; return Z(&r); }
    static int Rva(uint address, int length)
    {
        for (int i = 0; i < sectionCount; i++)
        {
            Section s = sections[i];
            if (address >= s.rva && (ulong)address + (uint)length <= (ulong)s.rva + s.size)
            {
                uint at = s.raw + address - s.rva;
                if ((ulong)at + (uint)length <= (uint)imageLength) return (int)at;
            }
        }
        return Bad("RVA outside image");
    }
    static uint TypeToken(int coded) { int t = (coded & 3) == 0 ? 2 : ((coded & 3) == 1 ? 1 : 27); return ((uint)t << 24) | (uint)(coded >> 2); }
    static void TypeName(uint token, int nesting)
    {
        if (nesting > 32) Bad("type nesting limit");
        int table = (int)(token >> 24); Reader r = Row(table, (int)(token & 0xffffff));
        if (table == 27)
        {
            Reader s = Blob(Index(&r, bw));
            if (U8(&s) != 0x15 || U8(&s) != 0x12) Bad("unsupported TypeSpec");
            TypeName(TypeToken(Compressed(&s)), nesting + 1); return;
        }
        if (table == 1) Index(&r, Coded(2, 1UL | (1UL << 26) | (1UL << 35) | 2UL));
        else if (table == 2) U32(&r); else Bad("unsupported type token");
        typeName = Text(Index(&r, sw)); typeNs = Text(Index(&r, sw));
    }
    static int ReadType(Reader* r, int nesting)
    {
        if (nesting > 32) Bad("type nesting limit");
        int t = U8(r);
        if (t == 1 || t == 2 || t == 3 || t == 4 || t == 5 || t == 6 || t == 7 || t == 8 || t == 9 || t == 14 || t == 28 || t == 24) return t;
        if (t == 17) { int n = Compressed(r); if ((n & 3) != 0) Bad("external value type"); return 0x100000 | (n >> 2); }
        if (t == 18) { Compressed(r); return 18; }
        if (t == 19) return 0x1000 | Compressed(r);
        if (t == 29)
        {
            int element = ReadType(r, nesting + 1);
            if (element == 1 || (element & 0x40000080) != 0) Bad("unsupported array type");
            return element < 128 ? 128 | element : 0x40000000 | element;
        }
        if (t == 21)
        {
            if (U8(r) != 18) Bad("generic value type");
            Compressed(r); int n = Compressed(r); if (n > 16) Bad("generic arity limit");
            for (int i = 0; i < n; i++) ReadType(r, nesting + 1);
            return 18;
        }
        return Bad("unsupported signature type");
    }
    static Signature Sig(int index)
    {
        Reader r = Blob(index); Signature s = default;
        int flags = U8(&r); if ((flags & 31) != 0) Bad("generic method signature");
        s.instance = (flags & 32) != 0 ? 1 : 0;
        s.count = Compressed(&r); if (s.count > 64) Bad("parameter limit");
        s.returns = ReadType(&r, 0); s.types = (int*)Perm(4 * s.count);
        for (int i = 0; i < s.count; i++) s.types[i] = ReadType(&r, 0);
        return s;
    }
    static int FieldIndex(uint token)
    {
        int row = (int)(token & 0xffffff);
        if ((token >> 24) == 4) { if (row < 1 || row > fieldCount) Bad("field token"); return row - 1; }
        if ((token >> 24) != 10) return Bad("field token");
        Reader r = Row(10, row); int parent = Index(&r, Mr());
        if ((parent & 7) != 0) Bad("external field");
        byte* name = Text(Index(&r, sw)); Reader sig = Blob(Index(&r, bw));
        for (int i = 0; i < fieldCount; i++) if (fieldOwners[i] == (parent >> 3) && Host.Same(fieldNames[i], name))
        {
            Reader fd = Row(4, i + 1); U16(&fd); Index(&fd, sw); Reader fs = Blob(Index(&fd, bw));
            if (sig.length == fs.length && Host.BytesEqual(sig.bytes, fs.bytes, sig.length)) return i;
        }
        return Bad("unresolved field");
    }
    static int TypeFromToken(uint token)
    {
        if ((token >> 24) == 2) return 0x100000 | (int)(token & 0xffffff);
        TypeName(token, 0);
        if (!Host.Equal(typeNs, "System")) return Bad("external type");
        if (Host.Equal(typeName, "Int32")) return 8;
        if (Host.Equal(typeName, "UInt32")) return 9;
        if (Host.Equal(typeName, "Boolean")) return 2;
        if (Host.Equal(typeName, "Char")) return 3;
        if (Host.Equal(typeName, "Byte")) return 5;
        if (Host.Equal(typeName, "String")) return 14;
        if (Host.Equal(typeName, "Object")) return 28;
        return Bad("unsupported token type");
    }
    static void Load(byte* bytes, int length)
    {
        if (length < 128 || length > 8 * 1024 * 1024) Bad("image size limit");
        image = bytes; imageLength = length;
        rows = (int*)Perm(64 * 4); offsets = (int*)Perm(64 * 4); widths = (int*)Perm(64 * 4);
        Reader r = Read(bytes, length); if (U16(&r) != 0x5a4d) Bad("expected MZ"); r.pos = 0x3c; int pe = (int)U32(&r); r.pos = pe;
        if (U32(&r) != 0x4550) Bad("expected PE"); U16(&r); sectionCount = U16(&r); if (sectionCount < 1 || sectionCount > 96) Bad("section count");
        Take(&r, 12); int optional = U16(&r); U16(&r); Reader opt = Read(Take(&r, optional), optional);
        int magic = U16(&opt); if (magic != 0x10b && magic != 0x20b) Bad("optional header");
        opt.pos = (magic == 0x10b ? 96 : 112) + 14 * 8; uint cliRva = U32(&opt); if (U32(&opt) < 72) Bad("CLI header");
        sections = (Section*)Perm(sectionCount * sizeof(Section));
        for (int i = 0; i < sectionCount; i++) { Take(&r, 12); sections[i].rva = U32(&r); sections[i].size = U32(&r); sections[i].raw = U32(&r); Take(&r, 16); }
        Reader cli = Read(bytes + Rva(cliRva, 72), 72); Take(&cli, 8); uint mdRva = U32(&cli); int mdSize = (int)U32(&cli); uint flags = U32(&cli); uint token = U32(&cli);
        if ((flags & 17) != 1 || (token >> 24) != 6) Bad("managed entry point required"); entry = (int)(token & 0xffffff) - 1;
        byte* metadata = bytes + Rva(mdRva, mdSize); Reader md = Read(metadata, mdSize);
        if (U32(&md) != 0x424a5342) Bad("metadata signature"); Take(&md, 8); Take(&md, (int)U32(&md)); U16(&md); int streams = U16(&md); if (streams > 16) Bad("stream count");
        for (int i = 0; i < streams; i++)
        {
            int off = (int)U32(&md), size = (int)U32(&md); byte* name = Z(&md); md.pos = (md.pos + 3) & ~3;
            if (off < 0 || size < 0 || off > mdSize - size) Bad("stream bounds");
            if (Host.Equal(name, "#Strings")) { strings = metadata + off; stringsLength = size; }
            if (Host.Equal(name, "#Blob")) { blobs = metadata + off; blobsLength = size; }
            if (Host.Equal(name, "#US")) { users = metadata + off; usersLength = size; }
            if (Host.Equal(name, "#~")) { tables = metadata + off; tablesLength = size; }
        }
        if (tables == null || strings == null || blobs == null) Bad("missing metadata streams");
        Reader tab = Read(tables, tablesLength); Take(&tab, 6); int hf = U8(&tab); U8(&tab); uint low = U32(&tab), high = U32(&tab); Take(&tab, 8);
        sw = (hf & 1) != 0 ? 4 : 2; gw = (hf & 2) != 0 ? 4 : 2; bw = (hf & 4) != 0 ? 4 : 2;
        for (int i = 0; i < 64; i++) if (((i < 32 ? low : high) & (1U << (i & 31))) != 0) { int count = (int)U32(&tab); if (count < 0 || count > 65535 || (i >= 45 && count > 0)) Bad("metadata table limit"); rows[i] = count; }
        for (int i = 0; i < 45; i++) if (rows[i] > 0) { widths[i] = RowSize(i); offsets[i] = tab.pos; Take(&tab, widths[i] * rows[i]); }
        methodCount = rows[6]; fieldCount = rows[4]; if (entry < 0 || entry >= methodCount) Bad("entry token");
        bodies = (Body*)Perm(methodCount * sizeof(Body));
        fieldTypes = (int*)Perm(fieldCount * 4); fieldOwners = (int*)Perm(fieldCount * 4); fieldSlots = (int*)Perm(fieldCount * 4); fieldStatic = (int*)Perm(fieldCount * 4); fieldNames = (byte**)Perm(fieldCount * sizeof(byte*));
        zeros = (Val*)Perm((rows[2] + 1) * sizeof(Val));
        for (int i = 0; i < fieldCount; i++)
        {
            Reader f = Row(4, i + 1); fieldStatic[i] = (U16(&f) & 16) != 0 ? 1 : 0; fieldNames[i] = Text(Index(&f, sw)); Reader sig = Blob(Index(&f, bw));
            if (U8(&sig) != 6) Bad("field signature"); fieldTypes[i] = ReadType(&sig, 0);
        }
        for (int i = 1; i <= rows[2]; i++)
        {
            Reader t = Row(2, i); Take(&t, 4 + 2 * sw + Td()); int first = U16(&t), last = fieldCount + 1;
            if (i < rows[2]) { Reader next = Row(2, i + 1); Take(&next, 4 + 2 * sw + Td()); last = U16(&next); }
            int slot = 0;
            for (int f = first - 1; f < last - 1; f++) { fieldOwners[f] = i; fieldSlots[f] = fieldStatic[f] == 0 ? slot++ : -1; }
        }
        memory = (Val*)Perm((16384 + fieldCount) * sizeof(Val)); globals = memory + 16384; Host.Roots(memory, (16384 + fieldCount) * sizeof(Val));
        for (int i = 0; i < methodCount; i++)
        {
            Reader m = Row(6, i + 1); Body* b = bodies + i; b->rva = (int)U32(&m); if ((U16(&m) & 3) != 0 || (U16(&m) & 16) == 0) Bad("static IL methods required");
            b->name = Text(Index(&m, sw)); b->sig = Index(&m, bw); Signature sig = Sig(b->sig); b->nargs = sig.count; b->returns = sig.returns; b->argtypes = sig.types;
        }
        for (int i = 0; i < methodCount; i++) Decode(i);
        for (int i = 0; i < methodCount; i++) Verify(i);
        for (int i = 0; i < fieldCount; i++) globals[i] = Zero(fieldTypes[i], 0);
    }
}
