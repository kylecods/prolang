namespace ProLang.Portable;
// On-device runtime core. This deliberately uses a checked, unmanaged C# subset so the
// identical source can be built by Roslyn and by cs-to-c for PSP. Host is the platform ABI.
public struct Val
{
    public int kind;
    public int data;
}

public struct Obj
{
    public int type, count, kind, mark, permanent, parent, root, array;
    public Val[] values;
    public ushort[] chars;
}

public struct Reader
{
    public int start, length, pos;
}

public struct Section
{
    public uint rva, raw, size;
}

public struct Signature
{
    public int count, returns, instance;
    public int[] types;
}

public struct Op
{
    public int code, operand, offset, depth, method, native, count, returns, type, resultType;
    public Val literal;
}

// The verifier's hot execution record; flags contain stack effects and block boundaries.
public struct PlanOp
{
    public int code, operand, stack, offset, flags, cold;
}

public struct ColdOp
{
    public int method, native, count, returns, type, resultType;
    public Val literal;
}

public struct Body
{
    public int name;
    public int rva, sig, nargs, returns, nlocals, maxstack, count;
    public int[] locals;
    public int[] argtypes;
    public Val[] init;
    public Op[] ops;
    public PlanOp[] plan;
    public ColdOp[] cold;
    public int machine;
}

public static partial class VM
{
    static byte[] image = null!;
    static int strings, blobs, users, tables;
    static int imageLength, stringsLength, blobsLength, usersLength, tablesLength;
    static int sw, bw, gw, entry, methodCount, fieldCount, sectionCount;
    static int[] rows = null!, offsets = null!, widths = null!, fieldTypes = null!, fieldOwners = null!, fieldSlots = null!, fieldStatic = null!;
    static int[] fieldNames = null!;
    static Section[] sections = null!;
    static Body[] bodies = null!;
    static Val[] memory = null!;
    static int globals;
    static Val[] zeros = null!;
    static int typeName;
    static int typeNs;
    static int depth, steps, calls, totalElements, jit;
    static int output;
    static int Bad(string message)
    {
        Host.Fail(message);
        return 0;
    }

    static Reader Read(int start, int length)
    {
        Reader reader = default;
        reader.start = start;
        reader.length = length;
        return reader;
    }

    static int Take(ref Reader reader, int count)
    {
        if (count < 0 || reader.length < 0 || reader.pos < 0 || reader.pos > reader.length - count ||
            reader.start < 0 || reader.start > image.Length - reader.length)
        {
            Bad("binary read outside image");
        }
        int at = reader.start + reader.pos;
        reader.pos += count;
        return at;
    }

    static int U8(ref Reader reader)
    {
        int at = Take(ref reader, 1);
        return image[at];
    }

    static int U16(ref Reader reader)
    {
        int at = Take(ref reader, 2);
        return image[at] | (image[at + 1] << 8);
    }

    static uint U32(ref Reader reader)
    {
        int at = Take(ref reader, 4);
        return (uint)(image[at] | (image[at + 1] << 8) | (image[at + 2] << 16) | (image[at + 3] << 24));
    }

    static int Index(ref Reader reader, int size)
    {
        return size == 2 ? U16(ref reader) : (int)U32(ref reader);
    }

    static int Compressed(ref Reader reader)
    {
        int b = U8(ref reader);
        if ((b & 128) == 0)
        {
            return b;
        }
        if ((b & 192) == 128)
        {
            return ((b & 63) << 8) | U8(ref reader);
        }
        if ((b & 224) == 192)
        {
            int b1 = U8(ref reader), b2 = U8(ref reader), b3 = U8(ref reader);
            return ((b & 31) << 24) | (b1 << 16) | (b2 << 8) | b3;
        }
        return Bad("invalid compressed integer");
    }

    static int Z(ref Reader reader)
    {
        int at = Take(ref reader, 0);
        while (U8(ref reader) != 0)
        {
        }
        return at;
    }

    static bool NameStarts(int at, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if ((uint)(at + i) >= (uint)image.Length || image[at + i] == 0 || image[at + i] != value[i])
            {
                return false;
            }
        }
        return true;
    }

    static bool NameEqual(int at, string value)
    {
        return NameStarts(at, value) && (uint)(at + value.Length) < (uint)image.Length && image[at + value.Length] == 0;
    }

    static bool NamesEqual(int a, int b)
    {
        while (true)
        {
            if ((uint)a >= (uint)image.Length || (uint)b >= (uint)image.Length)
            {
                Bad("binary read outside image");
            }
            if (image[a] != image[b])
            {
                return false;
            }
            if (image[a] == 0)
            {
                return true;
            }
            a++;
            b++;
        }
    }

    static bool BytesEqual(int a, int b, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (image[a + i] != image[b + i])
            {
                return false;
            }
        }
        return true;
    }

    static int Coded(int bits, ulong mask)
    {
        for (int t = 0; t < 45; t++)
        {
            if ((mask & (1UL << t)) != 0 && rows[t] >= (1 << (16 - bits)))
            {
                return 4;
            }
        }

        return 2;
    }

    static int Td()
    {
        return Coded(2, (1UL << 2) | (1UL << 1) | (1UL << 27));
    }

    static int Mr()
    {
        return Coded(3, (1UL << 2) | (1UL << 1) | (1UL << 26) | (1UL << 6) | (1UL << 27));
    }

    static int RowSize(int t)
    {
        switch (t)
        {
            case 0:
                return 2 + sw + 3 * gw;
            case 1:
                return Coded(2, 1UL | (1UL << 26) | (1UL << 35) | 2UL) + 2 * sw;
            case 2:
                return 8 + 2 * sw + Td();
            case 3:
            case 5:
            case 7:
            case 19:
            case 22:
                return 2;
            case 4:
                return 2 + sw + bw;
            case 6:
                return 10 + sw + bw;
            case 8:
                return 4 + sw;
            case 9:
                return 2 + Td();
            case 10:
                return Mr() + sw + bw;
            case 11:
                return 2 + Coded(2, (1UL << 4) | (1UL << 8) | (1UL << 23)) + bw;
            case 12:
                return Coded(5, (1UL << 45) - 1) + Coded(3, (1UL << 6) | (1UL << 10)) + bw;
            case 13:
                return Coded(1, (1UL << 4) | (1UL << 8)) + bw;
            case 14:
                return 2 + Coded(2, (1UL << 2) | (1UL << 6) | (1UL << 32)) + bw;
            case 15:
                return 8;
            case 16:
                return 6;
            case 17:
            case 27:
                return bw;
            case 18:
            case 21:
            case 41:
                return 4;
            case 20:
                return 2 + sw + Td();
            case 23:
                return 2 + sw + bw;
            case 24:
                return 4 + Coded(1, (1UL << 20) | (1UL << 23));
            case 25:
                return 2 + 2 * Coded(1, (1UL << 6) | (1UL << 10));
            case 26:
                return sw;
            case 28:
                return 4 + sw + Coded(1, (1UL << 4) | (1UL << 6));
            case 29:
                return 6;
            case 30:
                return 8;
            case 31:
            case 33:
                return 4;
            case 32:
                return 16 + bw + 2 * sw;
            case 34:
            case 36:
                return 12;
            case 35:
                return 12 + 2 * bw + 2 * sw;
            case 37:
                return 14;
            case 38:
                return 4 + sw + bw;
            case 39:
            case 40:
                return 8 + 2 * sw + Coded(2, (1UL << 38) | (1UL << 35) | (1UL << 39));
            case 42:
                return 4 + Coded(1, (1UL << 2) | (1UL << 6)) + sw;
            case 43:
                return Coded(1, (1UL << 6) | (1UL << 10)) + bw;
            case 44:
                return 2 + Td();
        }

        return Bad("unsupported metadata table");
    }

    static Reader Row(int t, int n)
    {
        if (t < 0 || t >= 45 || n < 1 || n > rows[t])
        {
            Bad("invalid metadata row");
        }

        return Read(tables + offsets[t] + (n - 1) * widths[t], widths[t]);
    }

    static Reader Blob(int n)
    {
        Reader r = Read(blobs, blobsLength);
        r.pos = n;
        int size = Compressed(ref r);
        int b = Take(ref r, size);
        return Read(b, size);
    }

    static int Text(int n)
    {
        Reader r = Read(strings, stringsLength);
        r.pos = n;
        return Z(ref r);
    }

    static int Rva(uint address, int length)
    {
        if (length < 0)
        {
            Bad("RVA outside image");
        }

        for (int i = 0; i < sectionCount; i++)
        {
            Section s = sections[i];
            if (address >= s.rva && (ulong)address + (uint)length <= (ulong)s.rva + s.size)
            {
                ulong at = (ulong)s.raw + address - s.rva;
                if (at + (uint)length <= (uint)imageLength)
                {
                    return (int)at;
                }
            }
        }

        return Bad("RVA outside image");
    }

    static void CheckFieldRange(int first, int last)
    {
        if (first < 1 || first > last || last > fieldCount + 1)
        {
            Bad("invalid field range");
        }
    }

    static uint TypeToken(int coded)
    {
        int t = (coded & 3) == 0 ? 2 : ((coded & 3) == 1 ? 1 : 27);
        return ((uint)t << 24) | (uint)(coded >> 2);
    }

    static void TypeName(uint token, int nesting)
    {
        if (nesting > 32)
        {
            Bad("type nesting limit");
        }

        int table = (int)(token >> 24);
        Reader r = Row(table, (int)(token & 0xffffff));
        if (table == 27)
        {
            Reader s = Blob(Index(ref r, bw));
            if (U8(ref s) != 0x15 || U8(ref s) != 0x12)
            {
                Bad("unsupported TypeSpec");
            }

            TypeName(TypeToken(Compressed(ref s)), nesting + 1);
            return;
        }

        if (table == 1)
        {
            Index(ref r, Coded(2, 1UL | (1UL << 26) | (1UL << 35) | 2UL));
        }
        else if (table == 2)
        {
            U32(ref r);
        }
        else
        {
            Bad("unsupported type token");
        }

        typeName = Text(Index(ref r, sw));
        typeNs = Text(Index(ref r, sw));
    }

    static int ReadType(ref Reader r, int nesting)
    {
        if (nesting > 32)
        {
            Bad("type nesting limit");
        }

        int t = U8(ref r);
        if (t == 1 || t == 2 || t == 3 || t == 4 || t == 5 || t == 6 || t == 7 || t == 8 || t == 9 || t == 14 || t == 28 || t == 24)
        {
            return t;
        }

        if (t == 17)
        {
            int n = Compressed(ref r);
            if ((n & 3) != 0)
            {
                Bad("external value type");
            }

            return 0x100000 | (n >> 2);
        }

        if (t == 18)
        {
            Compressed(ref r);
            return 18;
        }

        if (t == 19)
        {
            return 0x1000 | Compressed(ref r);
        }

        if (t == 29)
        {
            int element = ReadType(ref r, nesting + 1);
            if (element == 1 || (element & 0x40000080) != 0)
            {
                Bad("unsupported array type");
            }

            return element < 128 ? 128 | element : 0x40000000 | element;
        }

        if (t == 21)
        {
            if (U8(ref r) != 18)
            {
                Bad("generic value type");
            }

            Compressed(ref r);
            int n = Compressed(ref r);
            if (n > 16)
            {
                Bad("generic arity limit");
            }

            for (int i = 0; i < n; i++)
            {
                ReadType(ref r, nesting + 1);
            }

            return 18;
        }

        return Bad("unsupported signature type");
    }

    static Signature Sig(int index)
    {
        Reader r = Blob(index);
        Signature s = default;
        int flags = U8(ref r);
        if ((flags & 31) != 0)
        {
            Bad("generic method signature");
        }

        s.instance = (flags & 32) != 0 ? 1 : 0;
        s.count = Compressed(ref r);
        if (s.count > 64)
        {
            Bad("parameter limit");
        }

        s.returns = ReadType(ref r, 0);
        s.types = new int[s.count];
        for (int i = 0; i < s.count; i++)
        {
            s.types[i] = ReadType(ref r, 0);
        }

        return s;
    }

    static int FieldIndex(uint token)
    {
        int row = (int)(token & 0xffffff);
        if ((token >> 24) == 4)
        {
            if (row < 1 || row > fieldCount)
            {
                Bad("field token");
            }

            return row - 1;
        }

        if ((token >> 24) != 10)
        {
            return Bad("field token");
        }

        Reader r = Row(10, row);
        int parent = Index(ref r, Mr());
        if ((parent & 7) != 0)
        {
            Bad("external field");
        }

        int name = Text(Index(ref r, sw));
        Reader sig = Blob(Index(ref r, bw));
        for (int i = 0; i < fieldCount; i++)
        {
            if (fieldOwners[i] == (parent >> 3) && NamesEqual(fieldNames[i], name))
            {
                Reader fd = Row(4, i + 1);
                U16(ref fd);
                Index(ref fd, sw);
                Reader fs = Blob(Index(ref fd, bw));
                if (sig.length == fs.length && BytesEqual(sig.start, fs.start, sig.length))
                {
                    return i;
                }
            }
        }

        return Bad("unresolved field");
    }

    static int TypeFromToken(uint token)
    {
        if ((token >> 24) == 2)
        {
            return 0x100000 | (int)(token & 0xffffff);
        }

        TypeName(token, 0);
        if (!NameEqual(typeNs, "System"))
        {
            return Bad("external type");
        }

        if (NameEqual(typeName, "Int32"))
        {
            return 8;
        }

        if (NameEqual(typeName, "UInt32"))
        {
            return 9;
        }

        if (NameEqual(typeName, "Boolean"))
        {
            return 2;
        }

        if (NameEqual(typeName, "Char"))
        {
            return 3;
        }

        if (NameEqual(typeName, "Byte"))
        {
            return 5;
        }

        if (NameEqual(typeName, "String"))
        {
            return 14;
        }

        if (NameEqual(typeName, "Object"))
        {
            return 28;
        }

        return Bad("unsupported token type");
    }

    static void Load(byte[] bytes, int length)
    {
        if (length < 128 || length > 8 * 1024 * 1024)
        {
            Bad("image size limit");
        }

        image = bytes;
        strings = blobs = users = tables = -1;
        imageLength = length;
        rows = new int[64];
        offsets = new int[64];
        widths = new int[64];
        Reader r = Read(0, length);
        if (U16(ref r) != 0x5a4d)
        {
            Bad("expected MZ");
        }

        r.pos = 0x3c;
        int pe = (int)U32(ref r);
        r.pos = pe;
        if (U32(ref r) != 0x4550)
        {
            Bad("expected PE");
        }

        U16(ref r);
        sectionCount = U16(ref r);
        if (sectionCount < 1 || sectionCount > 96)
        {
            Bad("section count");
        }

        Take(ref r, 12);
        int optional = U16(ref r);
        U16(ref r);
        Reader opt = Read(Take(ref r, optional), optional);
        int magic = U16(ref opt);
        if (magic != 0x10b && magic != 0x20b)
        {
            Bad("optional header");
        }

        opt.pos = (magic == 0x10b ? 96 : 112) + 14 * 8;
        uint cliRva = U32(ref opt);
        if (U32(ref opt) < 72)
        {
            Bad("CLI header");
        }

        sections = new Section[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            Take(ref r, 12);
            sections[i].rva = U32(ref r);
            sections[i].size = U32(ref r);
            sections[i].raw = U32(ref r);
            Take(ref r, 16);
        }

        Reader cli = Read(Rva(cliRva, 72), 72);
        Take(ref cli, 8);
        uint mdRva = U32(ref cli);
        int mdSize = (int)U32(ref cli);
        uint flags = U32(ref cli);
        uint token = U32(ref cli);
        if ((flags & 17) != 1 || (token >> 24) != 6)
        {
            Bad("managed entry point required");
        }

        entry = (int)(token & 0xffffff) - 1;
        int metadata = Rva(mdRva, mdSize);
        Reader md = Read(metadata, mdSize);
        if (U32(ref md) != 0x424a5342)
        {
            Bad("metadata signature");
        }

        Take(ref md, 8);
        Take(ref md, (int)U32(ref md));
        U16(ref md);
        int streams = U16(ref md);
        if (streams > 16)
        {
            Bad("stream count");
        }

        for (int i = 0; i < streams; i++)
        {
            int off = (int)U32(ref md), size = (int)U32(ref md);
            int name = Z(ref md);
            md.pos = (md.pos + 3) & ~3;
            if (off < 0 || size < 0 || off > mdSize - size)
            {
                Bad("stream bounds");
            }

            if (NameEqual(name, "#Strings"))
            {
                strings = metadata + off;
                stringsLength = size;
            }

            if (NameEqual(name, "#Blob"))
            {
                blobs = metadata + off;
                blobsLength = size;
            }

            if (NameEqual(name, "#US"))
            {
                users = metadata + off;
                usersLength = size;
            }

            if (NameEqual(name, "#~"))
            {
                tables = metadata + off;
                tablesLength = size;
            }
        }

        if (tables < 0 || strings < 0 || blobs < 0)
        {
            Bad("missing metadata streams");
        }

        Reader tab = Read(tables, tablesLength);
        Take(ref tab, 6);
        int hf = U8(ref tab);
        U8(ref tab);
        uint low = U32(ref tab), high = U32(ref tab);
        Take(ref tab, 8);
        sw = (hf & 1) != 0 ? 4 : 2;
        gw = (hf & 2) != 0 ? 4 : 2;
        bw = (hf & 4) != 0 ? 4 : 2;
        for (int i = 0; i < 64; i++)
        {
            if (((i < 32 ? low : high) & (1U << (i & 31))) != 0)
            {
                int count = (int)U32(ref tab);
                if (count < 0 || count > 65535 || (i >= 45 && count > 0))
                {
                    Bad("metadata table limit");
                }

                rows[i] = count;
            }
        }

        for (int i = 0; i < 45; i++)
        {
            if (rows[i] > 0)
            {
                widths[i] = RowSize(i);
                offsets[i] = tab.pos;
                Take(ref tab, widths[i] * rows[i]);
            }
        }

        methodCount = rows[6];
        fieldCount = rows[4];
        if (entry < 0 || entry >= methodCount)
        {
            Bad("entry token");
        }

        bodies = new Body[methodCount];
        fieldTypes = new int[fieldCount];
        fieldOwners = new int[fieldCount];
        fieldSlots = new int[fieldCount];
        fieldStatic = new int[fieldCount];
        fieldNames = new int[fieldCount];
        zeros = new Val[rows[2] + 1];
        for (int i = 0; i < fieldCount; i++)
        {
            Reader f = Row(4, i + 1);
            fieldStatic[i] = (U16(ref f) & 16) != 0 ? 1 : 0;
            fieldNames[i] = Text(Index(ref f, sw));
            Reader sig = Blob(Index(ref f, bw));
            if (U8(ref sig) != 6)
            {
                Bad("field signature");
            }

            fieldTypes[i] = ReadType(ref sig, 0);
        }

        for (int i = 1; i <= rows[2]; i++)
        {
            Reader t = Row(2, i);
            Take(ref t, 4 + 2 * sw + Td());
            int first = U16(ref t), last = fieldCount + 1;
            if (i < rows[2])
            {
                Reader next = Row(2, i + 1);
                Take(ref next, 4 + 2 * sw + Td());
                last = U16(ref next);
            }

            CheckFieldRange(first, last);
            int slot = 0;
            for (int f = first - 1; f < last - 1; f++)
            {
                fieldOwners[f] = i;
                fieldSlots[f] = fieldStatic[f] == 0 ? slot++ : -1;
            }
        }

        memory = new Val[16384 + fieldCount];
        globals = 16384;

        for (int i = 0; i < methodCount; i++)
        {
            Reader m = Row(6, i + 1);
            ref Body b = ref bodies[i];
            b.rva = (int)U32(ref m);
            if ((U16(ref m) & 3) != 0 || (U16(ref m) & 16) == 0)
            {
                Bad("static IL methods required");
            }

            b.name = Text(Index(ref m, sw));
            b.sig = Index(ref m, bw);
            Signature sig = Sig(b.sig);
            b.nargs = sig.count;
            b.returns = sig.returns;
            b.argtypes = sig.types;
        }

        for (int i = 0; i < methodCount; i++)
        {
            Decode(i);
        }

        for (int i = 0; i < methodCount; i++)
        {
            Verify(i);
        }

        for (int i = 0; i < fieldCount; i++)
        {
            memory[globals + i] = Zero(fieldTypes[i], 0);
        }
    }
}
