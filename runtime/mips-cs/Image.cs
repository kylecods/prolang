using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ProLang.Mips;

sealed record Ty(string Name, int Row = 0, ImmutableArray<Ty> Args = default)
{
    public static readonly Ty Int = new("Int32"), Void = new("Void");
}

sealed class Types(MetadataReader md) : ISignatureTypeProvider<Ty, ImmutableArray<Ty>>
{
    public Ty GetArrayType(Ty t, ArrayShape shape) => throw new NotSupportedException("multidimensional arrays");
    public Ty GetByReferenceType(Ty t) => new(t.Name + "&");
    public Ty GetFunctionPointerType(MethodSignature<Ty> s) => throw new NotSupportedException("function pointer signature");
    public Ty GetGenericInstantiation(Ty t, ImmutableArray<Ty> args) => t with { Args = args };
    public Ty GetGenericMethodParameter(ImmutableArray<Ty> c, int i) => throw new NotSupportedException("generic methods");
    public Ty GetGenericTypeParameter(ImmutableArray<Ty> c, int i) => !c.IsDefault && i < c.Length ? c[i] : throw new NotSupportedException("unbound generic parameter");
    public Ty GetModifiedType(Ty m, Ty t, bool r) => t;
    public Ty GetPinnedType(Ty t) => throw new NotSupportedException("pinned locals");
    public Ty GetPointerType(Ty t) => throw new NotSupportedException("pointer signature");
    public Ty GetPrimitiveType(PrimitiveTypeCode c) => c switch
    {
        PrimitiveTypeCode.Void or PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Char or
        PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte or PrimitiveTypeCode.Int16 or
        PrimitiveTypeCode.UInt16 or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or
        PrimitiveTypeCode.String or PrimitiveTypeCode.Object or PrimitiveTypeCode.IntPtr => new(c.ToString()),
        _ => throw new NotSupportedException("unsupported signature type " + c)
    };
    public Ty GetSZArrayType(Ty t) => new(t.Name + "[]", Args: [t]);
    public Ty GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => new(r.GetString(r.GetTypeDefinition(h).Name), MetadataTokens.GetRowNumber(h));
    public Ty GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    {
        var t = r.GetTypeReference(h);
        return new(r.GetString(t.Namespace) + "." + r.GetString(t.Name));
    }
    public Ty GetTypeFromSpecification(MetadataReader r, ImmutableArray<Ty> c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
    public Ty Resolve(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(md, (TypeDefinitionHandle)h, 0),
        HandleKind.TypeReference => GetTypeFromReference(md, (TypeReferenceHandle)h, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(md, [], (TypeSpecificationHandle)h, 0),
        _ => throw new NotSupportedException("invalid type token")
    };
}

sealed record Site(string Owner, string Name, MethodSignature<Ty> Signature, int Method = -1)
{
    public bool Instance => Signature.Header.IsInstance;
    public bool Returns => Signature.ReturnType.Name != "Void";
    public int Count => Signature.ParameterTypes.Length + (Instance ? 1 : 0);
    public string Key { get; } = $"{Owner}::{Name}({string.Join(',', Signature.ParameterTypes.Select(t => t.Name))})->{Signature.ReturnType.Name}";
    public override string ToString() => Key;
}

sealed class Ins(int offset, int code, int operand = 0)
{
    public int Offset = offset, Code = code, Operand = operand, Depth = -1;
    public Site? Site;
    public string? Text;
    public Ty? Type;
    public override string ToString() => $"IL_{Offset:x4} {((ILOpCode)Code).ToString().Replace('_', '.').ToLowerInvariant(),-16} {Site?.ToString() ?? Text ?? Type?.Name ?? Operand.ToString()}";
}

sealed class Method(string name, Site site, Ty[] locals, int maxStack, Ins[] code)
{
    public string Name = name;
    public Site Site = site;
    public Ty[] Locals = locals;
    public int MaxStack = maxStack;
    public Ins[] Code = code;
    public uint[] Words = [];
    public string[] Assembly = [];
    public int[] WordIl = [];
    public int[] Starts = [];
    public int FoldedCalls, FusedPairs;
}

sealed record Field(Ty Type, int Owner, int Index, bool Static);

// Read metadata as data. No Assembly.Load, Reflection.Emit or dynamic code is used.
sealed class Image
{
    public Method[] Methods = [];
    public Field[] Fields = [];
    public Dictionary<int, int[]> Structs = [];
    public int Entry;
    public string Sha256 = "";
    public static Image Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("image exceeds 8 MiB");
        using var pe = new PEReader(new MemoryStream(bytes));
        var md = pe.GetMetadataReader();
        var types = new Types(md);
        var im = new Image { Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) };
        if (pe.PEHeaders.CorHeader is not { } cor || (cor.Flags & CorFlags.NativeEntryPoint) != 0)
            throw new InvalidDataException("managed entry point required");
        im.Entry = (cor.EntryPointTokenOrRelativeVirtualAddress & 0xffffff) - 1;
        var fields = new Field[md.FieldDefinitions.Count];
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            int owner = MetadataTokens.GetRowNumber(th), index = 0;
            var ids = new List<int>();
            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                int id = MetadataTokens.GetRowNumber(fh) - 1;
                bool stat = (fd.Attributes & FieldAttributes.Static) != 0;
                fields[id] = new(fd.DecodeSignature(types, []), owner, stat ? -1 : index++, stat);
                if (!stat) ids.Add(id);
            }
            im.Structs[owner] = ids.ToArray();
        }
        im.Fields = fields;
        Site ResolveSite(int token)
        {
            var h = MetadataTokens.EntityHandle(token);
            if (h.Kind == HandleKind.MethodDefinition)
            {
                var d = md.GetMethodDefinition((MethodDefinitionHandle)h);
                return new(types.Resolve(d.GetDeclaringType()).Name, md.GetString(d.Name), d.DecodeSignature(types, []), MetadataTokens.GetRowNumber(h) - 1);
            }
            if (h.Kind != HandleKind.MemberReference) throw new NotSupportedException("unsupported method token");
            var r = md.GetMemberReference((MemberReferenceHandle)h);
            var owner = types.Resolve(r.Parent);
            return new(owner.Name, md.GetString(r.Name), r.DecodeMethodSignature(types, owner.Args));
        }
        var methods = new List<Method>();
        foreach (var mh in md.MethodDefinitions)
        {
            var d = md.GetMethodDefinition(mh);
            var site = ResolveSite(MetadataTokens.GetToken(mh));
            if (site.Instance || d.RelativeVirtualAddress == 0) throw new NotSupportedException("only static method bodies are supported");
            var b = pe.GetMethodBody(d.RelativeVirtualAddress);
            if (b.ExceptionRegions.Length != 0) throw new NotSupportedException("exception regions");
            Ty[] locals = b.LocalSignature.IsNil ? [] : md.GetStandaloneSignature(b.LocalSignature).DecodeLocalSignature(types, []).ToArray();
            if (locals.Length > 1024 || b.MaxStack > 1024) throw new InvalidDataException("method frame limit exceeded");
            var code = Decode(b.GetILBytes() ?? throw new InvalidDataException("missing IL"));
            foreach (var ins in code)
            {
                int op = ins.Code;
                if (op is 0x28 or 0x6f or 0x73 or 0xfe06) ins.Site = ResolveSite(ins.Operand);
                if (op == 0x72) ins.Text = md.GetUserString(MetadataTokens.UserStringHandle(ins.Operand & 0xffffff));
                if (op is 0x71 or 0x81 or 0x8c or 0x8d or 0x8f or 0xa3 or 0xa4 or 0xa5 or 0xfe15) ins.Type = types.Resolve(MetadataTokens.EntityHandle(ins.Operand));
                if (op is >= 0x7b and <= 0x80)
                {
                    var fh = MetadataTokens.EntityHandle(ins.Operand);
                    if (fh.Kind == HandleKind.FieldDefinition) ins.Operand = MetadataTokens.GetRowNumber(fh) - 1;
                    else if (fh.Kind == HandleKind.MemberReference)
                    {
                        // Cecil may encode a field in the current module as a MemberRef.
                        var member = md.GetMemberReference((MemberReferenceHandle)fh);
                        if (member.Parent.Kind != HandleKind.TypeDefinition) throw new NotSupportedException("external field");
                        var candidates = md.GetTypeDefinition((TypeDefinitionHandle)member.Parent).GetFields().Where(f =>
                        {
                            var fd = md.GetFieldDefinition(f);
                            return md.GetString(fd.Name) == md.GetString(member.Name) && md.GetBlobBytes(fd.Signature).AsSpan().SequenceEqual(md.GetBlobBytes(member.Signature));
                        }).ToArray();
                        if (candidates.Length != 1) throw new InvalidDataException("unresolved field member");
                        ins.Operand = MetadataTokens.GetRowNumber(candidates[0]) - 1;
                    }
                    else throw new NotSupportedException("invalid field token");
                }
            }
            var m = new Method(site.Name, site, locals, b.MaxStack, code);
            Verify(m);
            methods.Add(m);
        }
        im.Methods = methods.ToArray();
        if ((uint)im.Entry >= im.Methods.Length || (uint)cor.EntryPointTokenOrRelativeVirtualAddress >> 24 != 6)
            throw new InvalidDataException("invalid entry point");
        return im;
    }

    public static Ins[] Decode(byte[] il)
    {
        var result = new List<Ins>();
        int p = 0;
        int Read(int n)
        {
            if (p + n > il.Length) throw new InvalidDataException("truncated IL operand");
            int v = n switch { 1 => il[p], 2 => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(p)), _ => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(p)) };
            p += n; return v;
        }
        while (p < il.Length)
        {
            int at = p, op = Read(1);
            if (op == 0xfe) op = 0xfe00 | Read(1);
            int arg = 0;
            if (op is >= 0x2b and <= 0x37) { arg = (sbyte)Read(1); arg += p; }
            else if (op is >= 0x38 and <= 0x44) { arg = Read(4); arg += p; }
            else if (op is >= 0x0e and <= 0x13) arg = Read(1);
            else if (op == 0x1f) arg = (sbyte)Read(1);
            else if (op is >= 0xfe09 and <= 0xfe0e) arg = Read(2);
            else if (op is 0x20 or 0x28 or 0x6f or 0x71 or 0x72 or 0x73 or >= 0x7b and <= 0x81 or 0x8c or 0x8d or 0x8f or 0xa3 or 0xa4 or 0xa5 or 0xfe06 or 0xfe15) arg = Read(4);
            else if (!(op is 0x00 or >= 0x02 and <= 0x0d or >= 0x14 and <= 0x1e or 0x25 or 0x26 or 0x2a or >= 0x58 and <= 0x69 or 0x6d or 0x8e or >= 0x90 and <= 0x95 or >= 0x9a and <= 0x9e or 0xa2 or 0xd1 or 0xd2 or >= 0xfe01 and <= 0xfe05))
                throw new NotSupportedException($"unsupported IL 0x{op:x} at IL_{at:x4}");
            result.Add(new(at, op, arg));
        }
        var offsets = result.Select((v, i) => (v.Offset, i)).ToDictionary(x => x.Offset, x => x.i);
        foreach (var i in result.Where(i => i.Code is >= 0x2b and <= 0x44))
            i.Operand = offsets.TryGetValue(i.Operand, out int n) ? n : throw new InvalidDataException("branch target is not an instruction boundary");
        return result.ToArray();
    }

    public static (int Pop, int Push) Effect(Ins i, bool returns) => i.Code switch
    {
        0x00 or 0x2b or 0x38 => (0, 0),
        >= 0x02 and <= 0x09 or 0x0e or 0x0f or 0x11 or 0x12 or >= 0x14 and <= 0x20 or 0x72 or 0x7e or 0x7f or 0xfe06 or 0xfe09 or 0xfe0a or 0xfe0c or 0xfe0d => (0, 1),
        >= 0x0a and <= 0x0d or 0x10 or 0x13 or 0x26 or 0x2c or 0x2d or 0x39 or 0x3a or 0x80 or 0xfe0b or 0xfe0e or 0xfe15 => (1, 0),
        0x25 => (1, 2),
        0x28 or 0x6f => (i.Site!.Count, i.Site.Returns ? 1 : 0),
        0x73 => (i.Site!.Signature.ParameterTypes.Length, 1),
        0x2a => (returns ? 1 : 0, 0),
        >= 0x2e and <= 0x37 or >= 0x3b and <= 0x44 or 0x7d or 0x81 => (2, 0),
        >= 0x58 and <= 0x64 or >= 0xfe01 and <= 0xfe05 or 0x8f or >= 0x90 and <= 0x95 or 0x9a or 0xa3 => (2, 1),
        0x9b or 0x9c or 0x9d or 0x9e or 0xa2 or 0xa4 => (3, 0),
        0x65 or 0x66 or >= 0x67 and <= 0x6e or 0x71 or 0x7b or 0x7c or 0x8c or 0x8d or 0x8e or 0xa5 or 0xd1 or 0xd2 => (1, 1),
        _ => throw new NotSupportedException($"unsupported stack effect {i}")
    };

    public static void Verify(Method m)
    {
        if (m.Code.Length == 0) throw new InvalidDataException("empty method");
        var q = new Queue<int>(); m.Code[0].Depth = 0; q.Enqueue(0);
        void Visit(int pc, int depth)
        {
            if ((uint)pc >= m.Code.Length) throw new InvalidDataException("control falls out of method");
            if (m.Code[pc].Depth == -1) { m.Code[pc].Depth = depth; q.Enqueue(pc); }
            else if (m.Code[pc].Depth != depth) throw new InvalidDataException("inconsistent stack at branch target");
        }
        // Validate operands even in unreachable instructions.
        foreach (var i in m.Code)
        {
            int op = i.Code;
            int local = op is >= 6 and <= 13 ? (op - 6) % 4 : i.Operand;
            int arg = op is >= 2 and <= 5 ? op - 2 : i.Operand;
            if ((op is >= 6 and <= 13 or 0x11 or 0x12 or 0x13 or 0xfe0c or 0xfe0d or 0xfe0e) && (uint)local >= m.Locals.Length) throw new InvalidDataException("invalid local index");
            if ((op is >= 2 and <= 5 or 0x0e or 0x0f or 0x10 or 0xfe09 or 0xfe0a or 0xfe0b) && (uint)arg >= m.Site.Count) throw new InvalidDataException("invalid argument index");
            _ = Effect(i, m.Site.Returns);
        }
        while (q.TryDequeue(out int pc))
        {
            var i = m.Code[pc]; var (pop, push) = Effect(i, m.Site.Returns);
            int next = i.Depth - pop + push;
            if (i.Depth < pop || next > m.MaxStack) throw new InvalidDataException($"invalid evaluation stack in {m.Name}: {i}");
            if (i.Code == 0x2a) { if (next != 0) throw new InvalidDataException("invalid return stack"); continue; }
            if (i.Code is >= 0x2b and <= 0x44) Visit(i.Operand, next);
            if (i.Code is not (0x2b or 0x38)) Visit(pc + 1, next);
        }
    }
}
