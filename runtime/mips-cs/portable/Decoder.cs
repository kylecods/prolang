namespace ProLang.Portable;

/// <summary>
/// Decodes a method's IL into <c>DecodedOp</c> records: the method header, the local variable types, and each
/// instruction with its operand resolved.
/// </summary>
/// <remarks>
/// <para>A call or <c>newobj</c> of a method outside the program is resolved to a <c>Natives</c> id here, so the
///   program fails at load, not at the call. An opcode the runtime does not support fails with 'unsupported IL
///   opcode'.</para>
/// </remarks>
internal static class Decoder
{
    // Method header (ECMA-335 II.25.4). The low two bits of the first byte select a tiny or a fat header.
    internal const int HeaderFormatMask = 3;
    internal const int TinyHeader = 2;
    internal const int TinyHeaderSizeShift = 2;
    internal const int FatHeader = 3;
    // A tiny header holds the code size in the upper six bits of its byte (above TinyHeaderSizeShift), has no locals
    // and a maximum stack of TinyMaxStack.
    internal const int TinyMaxStack = 8;
    // A fat header's flags. FatMoreSections marks exception handling sections, which are not supported.
    // Its size in 4-byte words is the top nibble of the flags (FatHeaderSizeShift) and is at least FatMinHeaderWords;
    // FatHeaderFixedBytes of it (flags, maximum stack, code size, local signature token) are read before it is skipped.
    internal const int FatMoreSections = 8;
    internal const int FatHeaderSizeShift = 12;
    internal const int FatMinHeaderWords = 3;
    internal const int FatHeaderFixedBytes = 12;
    // The most evaluation stack slots and the most locals a method may declare.
    internal const int MaxStack = 1024;
    internal const int MaxLocals = 1024;
    // The first byte of a local variable signature.
    internal const int LocalSignatureHeader = 7;

    /// <summary>
    /// Fills in the call-site fields of <c>op</c> from the token of a call, callvirt, newobj or ldftn.
    /// </summary>
    /// <remarks>
    /// <para>A MethodDef in this program sets <c>MethodIndex</c>, <c>ArgCount</c>, <c>ReturnsValue</c> and
    ///   <c>ResultType</c> from the callee's <c>MethodBody</c>. A MemberRef names a method outside the program: its
    ///   parent type is resolved, the type arguments of a generic parent are substituted into the signature, and
    ///   <c>Natives.ResolveNative</c> picks <c>NativeId</c>. For a native, <c>ArgCount</c> counts <c>this</c> and
    ///   <c>Type</c> is the type of the first parameter.</para>
    /// </remarks>
    private static void ResolveCallSite(ref DecodedOp op, uint token)
    {
        int row = (int)(token & MetadataToken.RowMask);
        if ((token >> MetadataToken.TableShift) == MetadataTable.MethodDef)
        {
            if (row < 1 || row > Loader.MethodCount)
            {
                Runtime.Fail("method token");
            }

            ref MethodBody callee = ref Loader.Bodies[row - 1];
            op.MethodIndex = row - 1;
            op.ArgCount = callee.ArgCount;
            op.ReturnsValue = callee.ReturnType == ElementType.Void ? 0 : 1;
            op.ResultType = callee.ReturnType;
            return;
        }

        if ((token >> MetadataToken.TableShift) != MetadataTable.MemberRef)
        {
            Runtime.Fail("call token");
        }

        ByteReader memberRow = Metadata.Row(MetadataTable.MemberRef, row);
        int parent = Metadata.ReadIndex(ref memberRow, Metadata.MemberRefParentSize());
        int tag = parent & MetadataToken.MemberRefParentTagMask;
        int table = tag == MetadataToken.MemberRefParentTypeDef ? MetadataTable.TypeDef : (tag == MetadataToken.MemberRefParentTypeRef ? MetadataTable.TypeRef : (tag == MetadataToken.MemberRefParentTypeSpec ? MetadataTable.TypeSpec : Runtime.Fail("member parent")));
        TypeSystem.ResolveTypeName(((uint)table << MetadataToken.TableShift) | (uint)(parent >> MetadataToken.MemberRefParentTagBits), 0);
        int nameOffset = Metadata.StringOffset(Metadata.ReadIndex(ref memberRow, Metadata.StringIndexSize));
        MethodSignature signature = TypeSystem.ReadMethodSignature(Metadata.ReadIndex(ref memberRow, Metadata.BlobIndexSize));
        if (table == MetadataTable.TypeSpec)
        {
            ByteReader typeSpecRow = Metadata.Row(MetadataTable.TypeSpec, parent >> MetadataToken.MemberRefParentTagBits);
            ByteReader typeSpecBlob = Metadata.Blob(Metadata.ReadIndex(ref typeSpecRow, Metadata.BlobIndexSize));
            Metadata.ReadU8(ref typeSpecBlob);
            Metadata.ReadU8(ref typeSpecBlob);
            Metadata.ReadCompressed(ref typeSpecBlob);
            int typeArgCount = Metadata.ReadCompressed(ref typeSpecBlob);
            if (typeArgCount > Limits.GenericArity)
            {
                Runtime.Fail("generic arity");
            }

            int[] typeArgs = new int[typeArgCount];
            for (int i = 0; i < typeArgCount; i++)
            {
                typeArgs[i] = TypeSystem.ReadType(ref typeSpecBlob, 0);
            }

            for (int i = 0; i < signature.ParamCount; i++)
            {
                if ((signature.ParamTypes[i] & TypeEncoding.GenericParamTagMask) == TypeEncoding.GenericParam)
                {
                    int genericIndex = signature.ParamTypes[i] & TypeEncoding.GenericParamIndexMask;
                    if (genericIndex >= typeArgCount)
                    {
                        Runtime.Fail("generic parameter");
                    }

                    signature.ParamTypes[i] = typeArgs[genericIndex];
                }
            }

            if ((signature.ReturnType & TypeEncoding.GenericParamTagMask) == TypeEncoding.GenericParam)
            {
                int genericIndex = signature.ReturnType & TypeEncoding.GenericParamIndexMask;
                if (genericIndex >= typeArgCount)
                {
                    Runtime.Fail("generic return");
                }

                signature.ReturnType = typeArgs[genericIndex];
            }
        }

        op.NativeId = Natives.ResolveNative(nameOffset, signature);
        op.ArgCount = signature.ParamCount + signature.HasThis;
        op.ReturnsValue = signature.ReturnType == ElementType.Void ? 0 : 1;
        op.ResultType = signature.ReturnType;
        op.Type = signature.ParamCount > 0 ? signature.ParamTypes[0] : 0;
    }

    /// <summary>
    /// The index of the instruction that starts at byte <c>offset</c>, by binary search over <c>body.Ops</c>, which
    /// are in offset order. Fails with 'invalid branch target' if none does.
    /// </summary>
    private static int FindOpIndex(MethodBody body, int offset)
    {
        int low = 0, high = body.OpCount;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (body.Ops[middle].Offset < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low >= body.OpCount || body.Ops[low].Offset != offset)
        {
            Runtime.Fail("invalid branch target");
        }

        return low;
    }

    /// <summary>
    /// Decodes one method: its header, its local variable types and its instructions, then turns each branch's byte
    /// target into an instruction index.
    /// </summary>
    internal static void Decode(int methodId)
    {
        ref MethodBody body = ref Loader.Bodies[methodId];
        int headerOffset = Metadata.Rva((uint)body.Rva, 1);
        ByteReader reader = Metadata.CreateReader(0, Metadata.ImageLength);
        reader.Position = headerOffset;
        int localToken = 0;
        int codeSize = ReadMethodHeader(ref body, ref reader, ref localToken);
        ReadLocals(ref body, localToken);
        DecodeInstructions(ref body, ref reader, codeSize);
        ResolveBranchTargets(ref body);
    }

    /// <summary>
    /// Reads the tiny or fat method header at <c>reader</c>'s position, sets <c>body.MaxStack</c>, and returns the
    /// code size. <c>localToken</c> receives the StandAloneSig token of the locals, which is zero when there are
    /// none. On return the reader is at the first instruction.
    /// </summary>
    private static int ReadMethodHeader(ref MethodBody body, ref ByteReader reader, ref int localToken)
    {
        int headerOffset = reader.Position;
        int headerByte = Metadata.ReadU8(ref reader), codeSize = 0;
        body.MaxStack = TinyMaxStack;
        if ((headerByte & HeaderFormatMask) == TinyHeader)
        {
            codeSize = headerByte >> TinyHeaderSizeShift;
        }
        else if ((headerByte & HeaderFormatMask) == FatHeader)
        {
            int fatFlags = headerByte | (Metadata.ReadU8(ref reader) << 8);
            if ((fatFlags & FatMoreSections) != 0 || (fatFlags >> FatHeaderSizeShift) < FatMinHeaderWords)
            {
                Runtime.Fail("unsupported method header or exception sections");
            }

            body.MaxStack = Metadata.ReadU16(ref reader);
            codeSize = (int)Metadata.ReadU32(ref reader);
            localToken = (int)Metadata.ReadU32(ref reader);
            Metadata.Consume(ref reader, (fatFlags >> FatHeaderSizeShift) * 4 - FatHeaderFixedBytes);
        }
        else
        {
            Runtime.Fail("invalid method header");
        }

        if (body.MaxStack > MaxStack || codeSize < 1)
        {
            Runtime.Fail("method limits");
        }

        Metadata.Rva((uint)body.Rva, reader.Position - headerOffset + codeSize);
        return codeSize;
    }

    /// <summary>
    /// Reads the local variable signature named by <c>localToken</c> (if any) into <c>body.LocalCount</c>,
    /// <c>LocalTypes</c> and <c>LocalDefaults</c>.
    /// </summary>
    private static void ReadLocals(ref MethodBody body, int localToken)
    {
        if (localToken == 0)
        {
            return;
        }

        if (((uint)localToken >> MetadataToken.TableShift) != MetadataTable.StandAloneSig)
        {
            Runtime.Fail("local signature token");
        }

        ByteReader signatureRow = Metadata.Row(MetadataTable.StandAloneSig, localToken & MetadataToken.RowMask);
        ByteReader localSignature = Metadata.Blob(Metadata.ReadIndex(ref signatureRow, Metadata.BlobIndexSize));
        if (Metadata.ReadU8(ref localSignature) != LocalSignatureHeader)
        {
            Runtime.Fail("locals signature");
        }

        body.LocalCount = Metadata.ReadCompressed(ref localSignature);
        if (body.LocalCount > MaxLocals)
        {
            Runtime.Fail("local count limit");
        }

        body.LocalTypes = new int[body.LocalCount];
        body.LocalDefaults = new Value[body.LocalCount];
        for (int i = 0; i < body.LocalCount; i++)
        {
            body.LocalTypes[i] = TypeSystem.ReadType(ref localSignature, 0);
            body.LocalDefaults[i] = Objects.ZeroValue(body.LocalTypes[i], 0);
        }
    }

    /// <summary>
    /// Decodes <c>codeSize</c> bytes of IL, starting at <c>reader</c>'s position, into <c>body.Ops</c>. A branch
    /// operand is left as an absolute byte offset; <c>ResolveBranchTargets</c> rewrites it once every instruction's
    /// offset is known.
    /// </summary>
    private static void DecodeInstructions(ref MethodBody body, ref ByteReader reader, int codeSize)
    {
        ByteReader ilReader = Metadata.CreateReader(Metadata.Consume(ref reader, codeSize), codeSize);
        body.Ops = new DecodedOp[codeSize];
        while (ilReader.Position < codeSize)
        {
            ref DecodedOp op = ref body.Ops[body.OpCount++];
            op.Offset = ilReader.Position;
            op.MethodIndex = -1;
            op.StackDepth = -1;
            int code = Metadata.ReadU8(ref ilReader);
            if (code == Il.TwoBytePrefix)
            {
                code = Il.TwoByteBase | Metadata.ReadU8(ref ilReader);
            }

            op.Code = code;
            ReadOperand(ref op, code, ref ilReader);
            ResolveOperand(ref op, code);
            if (code == Il.Ldstr)
            {
                LoadStringLiteral(ref op);
            }
        }
    }

    /// <summary>
    /// Reads the inline operand that follows an opcode. An opcode this runtime does not support is refused here. A
    /// branch operand becomes an absolute byte offset, because the IL gives it relative to the next instruction.
    /// </summary>
    private static void ReadOperand(ref DecodedOp op, int code, ref ByteReader ilReader)
    {
        if ((code >= Il.LdargS && code <= Il.StlocS) || code == Il.LdcI4S || (code >= Il.BrS && code <= Il.BltUnS))
        {
            op.Operand = code >= Il.LdcI4S ? (sbyte)Metadata.ReadU8(ref ilReader) : Metadata.ReadU8(ref ilReader);
        }
        else if (code == Il.LdcI4 || code == Il.Call || (code >= Il.Br && code <= Il.BltUn) || code == Il.Callvirt || (code >= Il.Ldobj && code <= Il.Newobj) || (code >= Il.Ldfld && code <= Il.Stobj) || code == Il.Box || code == Il.Newarr || code == Il.Ldelema || code == Il.Ldelem || code == Il.Stelem || code == Il.UnboxAny || code == Il.Ldftn || code == Il.Initobj)
        {
            op.Operand = (int)Metadata.ReadU32(ref ilReader);
        }
        else if (code >= Il.Ldarg && code <= Il.Stloc)
        {
            op.Operand = Metadata.ReadU16(ref ilReader);
        }
        else if (!(code == Il.Nop || (code >= Il.Ldarg_0 && code <= Il.Stloc_3) || (code >= Il.Ldnull && code <= Il.LdcI4_8) || code == Il.Dup || code == Il.Pop || code == Il.Ret || (code >= Il.Add && code <= Il.ConvI4) || code == Il.ConvU4 || code == Il.Ldlen || (code >= Il.LdelemI1 && code <= Il.LdelemU4) || (code >= Il.LdelemRef && code <= Il.StelemI4) || code == Il.StelemRef || code == Il.ConvU2 || code == Il.ConvU1 || code == Il.ConvI || code == Il.ConvU || (code >= Il.Ceq && code <= Il.CltUn)))
        {
            Runtime.Fail("unsupported IL opcode");
        }

        if (code >= Il.BrS && code <= Il.BltUn)
        {
            op.Operand += ilReader.Position;
        }
    }

    /// <summary>
    /// Resolves a metadata token operand: a call site (call, callvirt, newobj, ldftn), a field, or a type.
    /// </summary>
    private static void ResolveOperand(ref DecodedOp op, int code)
    {
        if (code == Il.Call || code == Il.Callvirt || code == Il.Newobj || code == Il.Ldftn)
        {
            ResolveCallSite(ref op, (uint)op.Operand);
        }

        if (code >= Il.Ldfld && code <= Il.Stsfld)
        {
            op.Operand = TypeSystem.ResolveFieldIndex((uint)op.Operand);
        }

        if (code == Il.Box || code == Il.Newarr || code == Il.UnboxAny || code == Il.Initobj || code == Il.Ldobj || code == Il.Stobj || code == Il.Ldelem || code == Il.Stelem || code == Il.Ldelema)
        {
            op.Type = TypeSystem.TypeFromToken((uint)op.Operand);
        }
    }

    /// <summary>
    /// Turns an <c>ldstr</c> operand (a <c>#US</c> stream token) into a string value in <c>op.Literal</c>, held in
    /// a permanent text object. The copy loop indexes <c>Metadata.Image</c> directly; it stays inside the
    /// <c>byteCount</c> bytes that <c>Consume</c> has just checked.
    /// </summary>
    private static void LoadStringLiteral(ref DecodedOp op)
    {
        if (((uint)op.Operand >> MetadataToken.TableShift) != MetadataToken.UserString)
        {
            Runtime.Fail("literal token");
        }

        ByteReader userStrings = Metadata.CreateReader(Metadata.UserStringsOffset, Metadata.UserStringsLength);
        userStrings.Position = op.Operand & MetadataToken.RowMask;
        int byteCount = Metadata.ReadCompressed(ref userStrings);
        if (byteCount < 1 || (byteCount & 1) == 0)
        {
            Runtime.Fail("literal length");
        }

        int textOffset = Metadata.Consume(ref userStrings, byteCount);
        int textHandle = Objects.NewText((byteCount - 1) / 2, 1);
        for (int i = 0; i < Heap.HandleTable[textHandle].Count; i++)
        {
            Heap.HandleTable[textHandle].Chars[i] = (ushort)(Metadata.Image[textOffset + i * 2] | (Metadata.Image[textOffset + i * 2 + 1] << 8));
        }

        op.Literal = Objects.MakeReference(ValueKind.String, textHandle);
    }

    /// <summary>
    /// Replaces each branch's absolute byte target with the index of the instruction that starts there.
    /// </summary>
    private static void ResolveBranchTargets(ref MethodBody body)
    {
        for (int i = 0; i < body.OpCount; i++)
        {
            if (body.Ops[i].Code >= Il.BrS && body.Ops[i].Code <= Il.BltUn)
            {
                body.Ops[i].Operand = FindOpIndex(body, body.Ops[i].Operand);
            }
        }
    }
}
