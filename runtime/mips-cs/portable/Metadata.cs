namespace ProLang.Portable;

/// <summary>
/// The loaded .NET image and the byte-level readers over it: PE sections, metadata streams, tables, heaps and
/// tokens.
/// </summary>
/// <remarks>
/// <para>The image is one byte array, <c>Image</c>, and every offset in this class is a file offset into it.
///   <c>Loader</c> fills in the stream, table and section fields while it loads; <c>TypeSystem</c>, <c>Decoder</c>
///   and the others only read through here. Every read through a <c>ByteReader</c> (<c>ReadU8</c> to
///   <c>ReadCString</c>, <c>Blob</c>, <c>Row</c>) goes through <c>Consume</c>, which fails with 'binary read outside
///   image' rather than reading outside the image.</para>
/// <para>Four readers index <c>Image</c> directly instead: <c>NameStartsWith</c> and <c>NameEqualsText</c> compare each
///   index with <c>Image.Length</c> and answer false past the end; <c>NamesEqual</c> compares both indexes and fails
///   with the same message; <c>BytesEqual</c>, like <c>Decoder.LoadStringLiteral</c>, has no check of its own, and
///   relies on the offsets and counts its callers take from readers that <c>Consume</c> already validated. Every
///   access to <c>Image</c> is itself a checked array access, in C too, so a bad index fails rather than reads
///   elsewhere.</para>
/// <para>The <c>#~</c> stream's row counts, row widths and offsets are held per table id in <c>TableRows</c>,
///   <c>TableOffsets</c> and <c>TableWidths</c>. <c>StringIndexSize</c>, <c>BlobIndexSize</c> and
///   <c>GuidIndexSize</c> are 2 or 4, from the stream's HeapSizes flags.</para>
/// </remarks>
internal static class Metadata
{
    // Width in bytes of a heap index or coded index: NarrowIndex, or WideIndex once a heap or table is too large for it.
    internal const int NarrowIndex = 2;
    internal const int WideIndex = 4;
    // A coded index stays narrow while every table it can name has fewer rows than 1 << (NarrowIndexBits - tag bits).
    internal const int NarrowIndexBits = 16;

    internal static byte[] Image = null!;
    internal static int StringsOffset, BlobsOffset, UserStringsOffset, TablesOffset;
    internal static int ImageLength, StringsLength, BlobsLength, UserStringsLength, TablesLength;
    internal static int StringIndexSize, BlobIndexSize, GuidIndexSize, SectionCount;
    internal static int[] TableRows = null!, TableOffsets = null!, TableWidths = null!;
    internal static PeSection[] Sections = null!;

    /// <summary>
    /// Forgets the previous image's stream offsets, lengths, heap index widths and section count, so that a second
    /// load in the same process starts clean. <c>Loader.Load</c> replaces <c>Image</c> and the table and section
    /// arrays.
    /// </summary>
    internal static void Reset()
    {
        StringsOffset = BlobsOffset = UserStringsOffset = TablesOffset = -1;
        StringsLength = BlobsLength = UserStringsLength = TablesLength = 0;
        StringIndexSize = BlobIndexSize = GuidIndexSize = SectionCount = 0;
    }

    /// <summary>
    /// A reader over <c>length</c> bytes of the image beginning at file offset <c>start</c>, positioned at its
    /// first byte. The window is checked on every read, not here.
    /// </summary>
    internal static ByteReader CreateReader(int start, int length)
    {
        ByteReader reader = default;
        reader.Start = start;
        reader.Length = length;
        return reader;
    }

    /// <summary>
    /// Advances <c>reader</c> by <c>count</c> bytes and returns the file offset at which they begin. Fails with
    /// 'binary read outside image' if <c>count</c> is negative or the reader's position, window or <c>count</c>
    /// does not lie inside the image.
    /// </summary>
    internal static int Consume(ref ByteReader reader, int count)
    {
        if (count < 0 || reader.Length < 0 || reader.Position < 0 || reader.Position > reader.Length - count ||
            reader.Start < 0 || reader.Start > Image.Length - reader.Length)
        {
            Runtime.Fail("binary read outside image");
        }
        int offset = reader.Start + reader.Position;
        reader.Position += count;
        return offset;
    }

    /// <summary>Reads one byte.</summary>
    internal static int ReadU8(ref ByteReader reader)
    {
        int offset = Consume(ref reader, 1);
        return Image[offset];
    }

    /// <summary>Reads a little-endian 16-bit value.</summary>
    internal static int ReadU16(ref ByteReader reader)
    {
        int offset = Consume(ref reader, 2);
        return Image[offset] | (Image[offset + 1] << 8);
    }

    /// <summary>Reads a little-endian 32-bit value.</summary>
    internal static uint ReadU32(ref ByteReader reader)
    {
        int offset = Consume(ref reader, 4);
        return (uint)(Image[offset] | (Image[offset + 1] << 8) | (Image[offset + 2] << 16) | (Image[offset + 3] << 24));
    }

    /// <summary>
    /// Reads a heap index or coded index of <c>size</c> bytes, <c>NarrowIndex</c> (2) or <c>WideIndex</c> (4).
    /// </summary>
    internal static int ReadIndex(ref ByteReader reader, int size)
    {
        return size == NarrowIndex ? ReadU16(ref reader) : (int)ReadU32(ref reader);
    }

    /// <summary>
    /// Reads an ECMA-335 II.23.2 compressed unsigned integer: one byte <c>0xxxxxxx</c>, two bytes <c>10xxxxxx</c>,
    /// or four bytes <c>110xxxxx</c>. Fails on any other leading bits.
    /// </summary>
    internal static int ReadCompressed(ref ByteReader reader)
    {
        int first = ReadU8(ref reader);
        if ((first & 128) == 0)
        {
            return first;
        }
        if ((first & 192) == 128)
        {
            return ((first & 63) << 8) | ReadU8(ref reader);
        }
        if ((first & 224) == 192)
        {
            int second = ReadU8(ref reader), third = ReadU8(ref reader), fourth = ReadU8(ref reader);
            return ((first & 31) << 24) | (second << 16) | (third << 8) | fourth;
        }
        return Runtime.Fail("invalid compressed integer");
    }

    /// <summary>
    /// Reads a NUL-terminated string and returns the file offset of its first byte; the reader ends just after the
    /// NUL.
    /// </summary>
    internal static int ReadCString(ref ByteReader reader)
    {
        int start = Consume(ref reader, 0);
        while (ReadU8(ref reader) != 0)
        {
        }
        return start;
    }

    /// <summary>
    /// True when the NUL-terminated ASCII name at file offset <c>nameOffset</c> begins with <c>text</c>. It stops
    /// at the NUL or at the end of the image.
    /// </summary>
    internal static bool NameStartsWith(int nameOffset, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if ((uint)(nameOffset + i) >= (uint)Image.Length || Image[nameOffset + i] == 0 || Image[nameOffset + i] != text[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>True when the NUL-terminated name at <c>nameOffset</c> is exactly <c>text</c>.</summary>
    internal static bool NameEqualsText(int nameOffset, string text)
    {
        return NameStartsWith(nameOffset, text) && (uint)(nameOffset + text.Length) < (uint)Image.Length && Image[nameOffset + text.Length] == 0;
    }

    /// <summary>
    /// True when the NUL-terminated names at the two file offsets are byte for byte equal. Fails if either runs off
    /// the image.
    /// </summary>
    internal static bool NamesEqual(int firstOffset, int secondOffset)
    {
        while (true)
        {
            if ((uint)firstOffset >= (uint)Image.Length || (uint)secondOffset >= (uint)Image.Length)
            {
                Runtime.Fail("binary read outside image");
            }
            if (Image[firstOffset] != Image[secondOffset])
            {
                return false;
            }
            if (Image[firstOffset] == 0)
            {
                return true;
            }
            firstOffset++;
            secondOffset++;
        }
    }

    /// <summary>
    /// True when <c>count</c> image bytes at the two file offsets are equal. It checks nothing itself: the one caller
    /// passes the <c>Start</c> and <c>Length</c> of two blob readers, whose windows <c>Blob</c> has already checked.
    /// </summary>
    internal static bool BytesEqual(int firstOffset, int secondOffset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (Image[firstOffset + i] != Image[secondOffset + i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The width in bytes of a coded index that spends <c>tagBits</c> bits on its tag and can name any table whose
    /// bit is set in <c>tableMask</c>: <c>NarrowIndex</c> while every such table has fewer than <c>1 &lt;&lt; (16 -
    /// tagBits)</c> rows, else <c>WideIndex</c>.
    /// </summary>
    internal static int CodedIndexSize(int tagBits, ulong tableMask)
    {
        for (int table = 0; table < MetadataTable.Count; table++)
        {
            if ((tableMask & (1UL << table)) != 0 && TableRows[table] >= (1 << (NarrowIndexBits - tagBits)))
            {
                return WideIndex;
            }
        }

        return NarrowIndex;
    }

    /// <summary>The width of a TypeDefOrRef coded index (TypeDef, TypeRef or TypeSpec).</summary>
    internal static int TypeDefOrRefSize()
    {
        return CodedIndexSize(MetadataToken.TypeDefOrRefTagBits, (1UL << MetadataTable.TypeDef) | (1UL << MetadataTable.TypeRef) | (1UL << MetadataTable.TypeSpec));
    }

    /// <summary>
    /// The width of a MemberRefParent coded index (TypeDef, TypeRef, ModuleRef, MethodDef or TypeSpec).
    /// </summary>
    internal static int MemberRefParentSize()
    {
        return CodedIndexSize(MetadataToken.MemberRefParentTagBits, (1UL << MetadataTable.TypeDef) | (1UL << MetadataTable.TypeRef) | (1UL << MetadataTable.ModuleRef) | (1UL << MetadataTable.MethodDef) | (1UL << MetadataTable.TypeSpec));
    }

    /// <summary>The width of a MethodDefOrRef coded index (one tag bit): a MethodDef or a MemberRef.</summary>
    internal static int MethodDefOrRefSize()
    {
        return CodedIndexSize(1, (1UL << MetadataTable.MethodDef) | (1UL << MetadataTable.MemberRef));
    }

    /// <summary>
    /// The width of a ResolutionScope coded index, the scope of a TypeRef (Module, ModuleRef, AssemblyRef or TypeRef).
    /// </summary>
    internal static int ResolutionScopeSize()
    {
        return CodedIndexSize(MetadataToken.ResolutionScopeTagBits, (1UL << MetadataTable.Module) | (1UL << MetadataTable.ModuleRef) | (1UL << MetadataTable.AssemblyRef) | (1UL << MetadataTable.TypeRef));
    }

    /// <summary>
    /// The width in bytes of one row of metadata table <c>table</c>. The column widths are the ECMA-335 II.22
    /// layouts, built from the heap index widths and from <c>CodedIndexSize</c> for coded indexes. Fails with
    /// 'unsupported metadata table' for an id it does not know.
    /// </summary>
    internal static int RowSize(int table)
    {
        // Case labels are MetadataTable ids, kept as literals because a C case label cannot be a variable.
        switch (table)
        {
            case 0: // Module
                return 2 + StringIndexSize + 3 * GuidIndexSize;
            case 1: // TypeRef: ResolutionScope, name, namespace
                return ResolutionScopeSize() + 2 * StringIndexSize;
            case 2: // TypeDef
                return 8 + 2 * StringIndexSize + TypeDefOrRefSize();
            case 3: // FieldPtr
            case 5: // MethodPtr
            case 7: // ParamPtr
            case 19: // EventPtr
            case 22: // PropertyPtr
                return 2;
            case 4: // Field
                return 2 + StringIndexSize + BlobIndexSize;
            case 6: // MethodDef
                return 10 + StringIndexSize + BlobIndexSize;
            case 8: // Param
                return 4 + StringIndexSize;
            case 9: // InterfaceImpl
                return 2 + TypeDefOrRefSize();
            case 10: // MemberRef
                return MemberRefParentSize() + StringIndexSize + BlobIndexSize;
            case 11: // Constant: HasConstant
                return 2 + CodedIndexSize(2, (1UL << MetadataTable.Field) | (1UL << MetadataTable.Param) | (1UL << MetadataTable.Property)) + BlobIndexSize;
            case 12: // CustomAttribute: HasCustomAttribute (sized for every table), CustomAttributeType
                return CodedIndexSize(5, (1UL << MetadataTable.Count) - 1) + CodedIndexSize(3, (1UL << MetadataTable.MethodDef) | (1UL << MetadataTable.MemberRef)) + BlobIndexSize;
            case 13: // FieldMarshal: HasFieldMarshal
                return CodedIndexSize(1, (1UL << MetadataTable.Field) | (1UL << MetadataTable.Param)) + BlobIndexSize;
            case 14: // DeclSecurity: HasDeclSecurity
                return 2 + CodedIndexSize(2, (1UL << MetadataTable.TypeDef) | (1UL << MetadataTable.MethodDef) | (1UL << MetadataTable.Assembly)) + BlobIndexSize;
            case 15: // ClassLayout
                return 8;
            case 16: // FieldLayout
                return 6;
            case 17: // StandAloneSig
            case 27: // TypeSpec
                return BlobIndexSize;
            case 18: // EventMap
            case 21: // PropertyMap
            case 41: // NestedClass
                return 4;
            case 20: // Event
                return 2 + StringIndexSize + TypeDefOrRefSize();
            case 23: // Property
                return 2 + StringIndexSize + BlobIndexSize;
            case 24: // MethodSemantics: HasSemantics
                return 4 + CodedIndexSize(1, (1UL << MetadataTable.Event) | (1UL << MetadataTable.Property));
            case 25: // MethodImpl: MethodDefOrRef twice
                return 2 + 2 * MethodDefOrRefSize();
            case 26: // ModuleRef
                return StringIndexSize;
            case 28: // ImplMap: MemberForwarded
                return 4 + StringIndexSize + CodedIndexSize(1, (1UL << MetadataTable.Field) | (1UL << MetadataTable.MethodDef));
            case 29: // FieldRva
                return 6;
            case 30: // EncLog
                return 8;
            case 31: // EncMap
            case 33: // AssemblyProcessor
                return 4;
            case 32: // Assembly
                return 16 + BlobIndexSize + 2 * StringIndexSize;
            case 34: // AssemblyOs
            case 36: // AssemblyRefProcessor
                return 12;
            case 35: // AssemblyRef
                return 12 + 2 * BlobIndexSize + 2 * StringIndexSize;
            case 37: // AssemblyRefOs
                return 14;
            case 38: // File
                return 4 + StringIndexSize + BlobIndexSize;
            case 39: // ExportedType: Implementation
            case 40: // ManifestResource: Implementation
                return 8 + 2 * StringIndexSize + CodedIndexSize(2, (1UL << MetadataTable.File) | (1UL << MetadataTable.AssemblyRef) | (1UL << MetadataTable.ExportedType));
            case 42: // GenericParam: TypeOrMethodDef
                return 4 + CodedIndexSize(1, (1UL << MetadataTable.TypeDef) | (1UL << MetadataTable.MethodDef)) + StringIndexSize;
            case 43: // MethodSpec: MethodDefOrRef
                return MethodDefOrRefSize() + BlobIndexSize;
            case 44: // GenericParamConstraint
                return 2 + TypeDefOrRefSize();
        }

        return Runtime.Fail("unsupported metadata table");
    }

    /// <summary>
    /// A reader over one row of a metadata table, sized to the table's row width. <c>row</c> is 1-based. Fails with
    /// 'invalid metadata row' outside the table.
    /// </summary>
    internal static ByteReader Row(int table, int row)
    {
        if (table < 0 || table >= MetadataTable.Count || row < 1 || row > TableRows[table])
        {
            Runtime.Fail("invalid metadata row");
        }

        return CreateReader(TablesOffset + TableOffsets[table] + (row - 1) * TableWidths[table], TableWidths[table]);
    }

    /// <summary>
    /// A reader over the blob at <c>offset</c> in the <c>#Blob</c> heap: it reads the blob's compressed length
    /// prefix and returns a reader over the bytes that follow.
    /// </summary>
    internal static ByteReader Blob(int offset)
    {
        ByteReader reader = CreateReader(BlobsOffset, BlobsLength);
        reader.Position = offset;
        int size = ReadCompressed(ref reader);
        int start = Consume(ref reader, size);
        return CreateReader(start, size);
    }

    /// <summary>
    /// The file offset of the string at <c>index</c> in the <c>#Strings</c> heap, after checking that it is
    /// NUL-terminated inside the heap.
    /// </summary>
    internal static int StringOffset(int index)
    {
        ByteReader reader = CreateReader(StringsOffset, StringsLength);
        reader.Position = index;
        return ReadCString(ref reader);
    }

    /// <summary>
    /// Maps a relative virtual address and a length to a file offset, by finding the section that contains the
    /// whole range. Fails with 'RVA outside image' if no section does or the range runs past the end of the image.
    /// </summary>
    internal static int Rva(uint address, int length)
    {
        if (length < 0)
        {
            Runtime.Fail("RVA outside image");
        }

        for (int i = 0; i < SectionCount; i++)
        {
            PeSection section = Sections[i];
            if (address >= section.Rva && (ulong)address + (uint)length <= (ulong)section.Rva + section.RawSize)
            {
                ulong fileOffset = (ulong)section.RawOffset + address - section.Rva;
                if (fileOffset + (uint)length <= (uint)ImageLength)
                {
                    return (int)fileOffset;
                }
            }
        }

        return Runtime.Fail("RVA outside image");
    }
}
