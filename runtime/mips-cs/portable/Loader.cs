namespace ProLang.Portable;

/// <summary>
/// Loads a .NET assembly image: reads its PE and CLI headers, metadata streams and tables, builds the field and
/// method tables, and decodes and verifies every method.
/// </summary>
/// <remarks>
/// <para>The result is <c>Bodies</c>, one <c>MethodBody</c> per MethodDef, and <c>EntryMethod</c>, the index of the
///   entry point. The loader accepts only IL-only images with a managed entry point and static IL methods, and
///   refuses anything it does not understand through <c>Runtime.Fail</c>.</para>
/// </remarks>
internal static class Loader
{
    // An image smaller than a DOS header plus PE headers, or larger than 8 MiB, is refused.
    internal const int MinImageSize = 128;
    internal const int MaxImageSize = 8388608;

    // PE and CLI header values (ECMA-335 II.25 and the PE/COFF specification).
    internal const int DosMagic = 0x5a4d;
    // Offset, in the DOS header, of the file offset of the PE header (e_lfanew).
    internal const int PeOffsetPosition = 0x3c;
    internal const int PeSignature = 0x4550;
    internal const int MaxSections = 96;
    // Optional header magic: PE32 or PE32+.
    internal const int Pe32Magic = 0x10b;
    internal const int Pe32PlusMagic = 0x20b;
    // Offset of the data directories in the PE32 and PE32+ optional headers, the number of the CLI
    // directory among them, and the size of one directory entry (an RVA and a size).
    internal const int Pe32DataDirectories = 96;
    internal const int Pe32PlusDataDirectories = 112;
    internal const int CliDirectoryIndex = 14;
    internal const int DataDirectorySize = 8;
    // The COFF file header fields skipped after NumberOfSections: TimeDateStamp, PointerToSymbolTable and
    // NumberOfSymbols, four bytes each.
    internal const int CoffUnusedFieldsSize = 12;
    // A section header (40 bytes) opens with Name (8) and VirtualSize (4), then VirtualAddress, SizeOfRawData and
    // PointerToRawData are read, and it ends with PointerToRelocations and PointerToLinenumbers (4 each),
    // NumberOfRelocations and NumberOfLinenumbers (2 each) and Characteristics (4).
    internal const int SectionHeaderLeadingBytes = 12;
    internal const int SectionHeaderTrailingBytes = 16;
    internal const int CliHeaderSize = 72;
    // The CLI header opens with cb (4 bytes), MajorRuntimeVersion and MinorRuntimeVersion (2 each), before the
    // metadata directory (RVA and size) is read.
    internal const int CliHeaderLeadingBytes = 8;
    // CLI header flags: the program must be IL-only (COMIMAGE_FLAGS_ILONLY) with no native entry point
    // (COMIMAGE_FLAGS_NATIVE_ENTRYPOINT, the other bit of the mask).
    internal const int CliFlagsMask = 17;
    internal const int CliFlagsIlOnly = 1;

    // The metadata root signature, "BSJB", and the most streams it may declare.
    internal const int MetadataSignature = 0x424a5342;
    internal const int MaxStreams = 16;
    // After the signature, the metadata root has MajorVersion and MinorVersion (2 bytes each) and Reserved (4)
    // before the length of the version string.
    internal const int MetadataRootLeadingBytes = 8;
    // The #~ stream opens with Reserved (4 bytes), MajorVersion and MinorVersion (1 each) before HeapSizes, and has
    // the Sorted bit vector (8 bytes) after the Valid one.
    internal const int TablesHeaderLeadingBytes = 6;
    internal const int TablesSortedVectorSize = 8;
    // #~ stream HeapSizes flags: set when the string, guid or blob heap needs four-byte indexes.
    internal const int HeapStringsWide = 1;
    internal const int HeapGuidWide = 2;
    internal const int HeapBlobWide = 4;
    // Valid-table bit vector width, and the most rows any one table may hold.
    internal const int TableSlots = 64;
    internal const int MaxTableRows = 65535;

    // Attribute bits: a field or a method is static; a method's ImplFlags code type must be IL (zero).
    internal const int FieldStatic = 16;
    internal const int MethodStatic = 16;
    internal const int MethodCodeTypeMask = 3;
    // The first byte of a field signature.
    internal const int FieldSignatureHeader = 6;
    // A TypeDef row is Flags (4 bytes), TypeName, TypeNamespace, Extends, then FieldList and MethodList.
    internal const int TypeDefFlagsSize = 4;

    internal static int EntryMethod, MethodCount;
    internal static MethodBody[] Bodies = null!;

    /// <summary>Forgets the entry method and the method count of a previous load.</summary>
    internal static void Reset()
    {
        EntryMethod = MethodCount = 0;
    }

    /// <summary>
    /// Loads a .NET image: the PE and CLI headers, the metadata streams and tables, every field and method row,
    /// then decodes and verifies every method body. Each step may refuse the image through <c>Runtime.Fail</c>.
    /// </summary>
    internal static void Load(byte[] image, int length)
    {
        if (length < MinImageSize || length > MaxImageSize)
        {
            Runtime.Fail("image size limit");
        }

        BeginImage(image, length);
        uint cliRva = ReadPeHeaders(length);
        int metadataSize = 0;
        int metadataRoot = ReadCliHeader(cliRva, ref metadataSize);
        ReadMetadataStreams(metadataRoot, metadataSize);
        ReadTableLayout();
        AllocateMemberTables();
        LoadFields();
        AssignFieldSlots();
        AllocateInterpreterMemory();
        LoadMethods();
        PrepareMethodBodies();
        InitializeGlobals();
    }

    /// <summary>Points <c>Metadata</c> at the image and clears everything the later steps fill in.</summary>
    private static void BeginImage(byte[] image, int length)
    {
        Metadata.Image = image;
        Metadata.StringsOffset = Metadata.BlobsOffset = Metadata.UserStringsOffset = Metadata.TablesOffset = -1;
        Metadata.ImageLength = length;
        Metadata.TableRows = new int[TableSlots];
        Metadata.TableOffsets = new int[TableSlots];
        Metadata.TableWidths = new int[TableSlots];
    }

    /// <summary>
    /// Reads the DOS and PE headers and the section table, and returns the RVA of the CLI header.
    /// </summary>
    private static uint ReadPeHeaders(int length)
    {
        ByteReader reader = Metadata.CreateReader(0, length);
        if (Metadata.ReadU16(ref reader) != DosMagic)
        {
            Runtime.Fail("expected MZ");
        }

        reader.Position = PeOffsetPosition;
        int peOffset = (int)Metadata.ReadU32(ref reader);
        reader.Position = peOffset;
        if (Metadata.ReadU32(ref reader) != PeSignature)
        {
            Runtime.Fail("expected PE");
        }

        Metadata.ReadU16(ref reader);
        Metadata.SectionCount = Metadata.ReadU16(ref reader);
        if (Metadata.SectionCount < 1 || Metadata.SectionCount > MaxSections)
        {
            Runtime.Fail("section count");
        }

        Metadata.Consume(ref reader, CoffUnusedFieldsSize);
        int optionalHeaderSize = Metadata.ReadU16(ref reader);
        Metadata.ReadU16(ref reader);
        ByteReader optionalHeader = Metadata.CreateReader(Metadata.Consume(ref reader, optionalHeaderSize), optionalHeaderSize);
        int magic = Metadata.ReadU16(ref optionalHeader);
        if (magic != Pe32Magic && magic != Pe32PlusMagic)
        {
            Runtime.Fail("optional header");
        }

        optionalHeader.Position = (magic == Pe32Magic ? Pe32DataDirectories : Pe32PlusDataDirectories) + CliDirectoryIndex * DataDirectorySize;
        uint cliRva = Metadata.ReadU32(ref optionalHeader);
        if (Metadata.ReadU32(ref optionalHeader) < CliHeaderSize)
        {
            Runtime.Fail("CLI header");
        }

        Metadata.Sections = new PeSection[Metadata.SectionCount];
        for (int i = 0; i < Metadata.SectionCount; i++)
        {
            Metadata.Consume(ref reader, SectionHeaderLeadingBytes);
            Metadata.Sections[i].Rva = Metadata.ReadU32(ref reader);
            Metadata.Sections[i].RawSize = Metadata.ReadU32(ref reader);
            Metadata.Sections[i].RawOffset = Metadata.ReadU32(ref reader);
            Metadata.Consume(ref reader, SectionHeaderTrailingBytes);
        }

        return cliRva;
    }

    /// <summary>
    /// Reads the CLI header: checks that the program is IL-only, records the entry method, and returns the file
    /// offset of the metadata root, with its size in <c>metadataSize</c>.
    /// </summary>
    private static int ReadCliHeader(uint cliRva, ref int metadataSize)
    {
        ByteReader cliHeader = Metadata.CreateReader(Metadata.Rva(cliRva, CliHeaderSize), CliHeaderSize);
        Metadata.Consume(ref cliHeader, CliHeaderLeadingBytes);
        uint metadataRva = Metadata.ReadU32(ref cliHeader);
        metadataSize = (int)Metadata.ReadU32(ref cliHeader);
        uint cliFlags = Metadata.ReadU32(ref cliHeader);
        uint entryToken = Metadata.ReadU32(ref cliHeader);
        if ((cliFlags & CliFlagsMask) != CliFlagsIlOnly || (entryToken >> MetadataToken.TableShift) != MetadataTable.MethodDef)
        {
            Runtime.Fail("managed entry point required");
        }

        EntryMethod = (int)(entryToken & MetadataToken.RowMask) - 1;
        return Metadata.Rva(metadataRva, metadataSize);
    }

    /// <summary>
    /// Reads the metadata root and its stream headers, and records where <c>#Strings</c>, <c>#Blob</c>, <c>#US</c>
    /// and <c>#~</c> are.
    /// </summary>
    private static void ReadMetadataStreams(int metadataRoot, int metadataSize)
    {
        ByteReader metadataReader = Metadata.CreateReader(metadataRoot, metadataSize);
        if (Metadata.ReadU32(ref metadataReader) != MetadataSignature)
        {
            Runtime.Fail("metadata signature");
        }

        Metadata.Consume(ref metadataReader, MetadataRootLeadingBytes);
        Metadata.Consume(ref metadataReader, (int)Metadata.ReadU32(ref metadataReader));
        Metadata.ReadU16(ref metadataReader);
        int streamCount = Metadata.ReadU16(ref metadataReader);
        if (streamCount > MaxStreams)
        {
            Runtime.Fail("stream count");
        }

        for (int i = 0; i < streamCount; i++)
        {
            int streamOffset = (int)Metadata.ReadU32(ref metadataReader), streamSize = (int)Metadata.ReadU32(ref metadataReader);
            int streamName = Metadata.ReadCString(ref metadataReader);
            metadataReader.Position = (metadataReader.Position + 3) & ~3;
            if (streamOffset < 0 || streamSize < 0 || streamOffset > metadataSize - streamSize)
            {
                Runtime.Fail("stream bounds");
            }

            if (Metadata.NameEqualsText(streamName, "#Strings"))
            {
                Metadata.StringsOffset = metadataRoot + streamOffset;
                Metadata.StringsLength = streamSize;
            }

            if (Metadata.NameEqualsText(streamName, "#Blob"))
            {
                Metadata.BlobsOffset = metadataRoot + streamOffset;
                Metadata.BlobsLength = streamSize;
            }

            if (Metadata.NameEqualsText(streamName, "#US"))
            {
                Metadata.UserStringsOffset = metadataRoot + streamOffset;
                Metadata.UserStringsLength = streamSize;
            }

            if (Metadata.NameEqualsText(streamName, "#~"))
            {
                Metadata.TablesOffset = metadataRoot + streamOffset;
                Metadata.TablesLength = streamSize;
            }
        }

        if (Metadata.TablesOffset < 0 || Metadata.StringsOffset < 0 || Metadata.BlobsOffset < 0)
        {
            Runtime.Fail("missing metadata streams");
        }
    }

    /// <summary>
    /// Reads the <c>#~</c> stream header: the heap index widths and each table's row count, then every table's row
    /// width and offset within the stream.
    /// </summary>
    private static void ReadTableLayout()
    {
        ByteReader tablesReader = Metadata.CreateReader(Metadata.TablesOffset, Metadata.TablesLength);
        Metadata.Consume(ref tablesReader, TablesHeaderLeadingBytes);
        int heapSizes = Metadata.ReadU8(ref tablesReader);
        Metadata.ReadU8(ref tablesReader);
        uint validLow = Metadata.ReadU32(ref tablesReader), validHigh = Metadata.ReadU32(ref tablesReader);
        Metadata.Consume(ref tablesReader, TablesSortedVectorSize);
        Metadata.StringIndexSize = (heapSizes & HeapStringsWide) != 0 ? Metadata.WideIndex : Metadata.NarrowIndex;
        Metadata.GuidIndexSize = (heapSizes & HeapGuidWide) != 0 ? Metadata.WideIndex : Metadata.NarrowIndex;
        Metadata.BlobIndexSize = (heapSizes & HeapBlobWide) != 0 ? Metadata.WideIndex : Metadata.NarrowIndex;
        for (int i = 0; i < TableSlots; i++)
        {
            if (((i < 32 ? validLow : validHigh) & (1U << (i & 31))) != 0)
            {
                int rowCount = (int)Metadata.ReadU32(ref tablesReader);
                if (rowCount < 0 || rowCount > MaxTableRows || (i >= MetadataTable.Count && rowCount > 0))
                {
                    Runtime.Fail("metadata table limit");
                }

                Metadata.TableRows[i] = rowCount;
            }
        }

        for (int i = 0; i < MetadataTable.Count; i++)
        {
            if (Metadata.TableRows[i] > 0)
            {
                Metadata.TableWidths[i] = Metadata.RowSize(i);
                Metadata.TableOffsets[i] = tablesReader.Position;
                Metadata.Consume(ref tablesReader, Metadata.TableWidths[i] * Metadata.TableRows[i]);
            }
        }
    }

    /// <summary>
    /// Takes the method and field counts from the tables, checks that the entry method is one of them, and
    /// allocates the per-method and per-field arrays the later steps fill in, and <c>Objects.ZeroCache</c>.
    /// </summary>
    private static void AllocateMemberTables()
    {
        MethodCount = Metadata.TableRows[MetadataTable.MethodDef];
        TypeSystem.FieldCount = Metadata.TableRows[MetadataTable.Field];
        if (EntryMethod < 0 || EntryMethod >= MethodCount)
        {
            Runtime.Fail("entry token");
        }

        Bodies = new MethodBody[MethodCount];
        TypeSystem.FieldTypes = new int[TypeSystem.FieldCount];
        TypeSystem.FieldOwners = new int[TypeSystem.FieldCount];
        TypeSystem.FieldSlots = new int[TypeSystem.FieldCount];
        TypeSystem.FieldIsStatic = new int[TypeSystem.FieldCount];
        TypeSystem.FieldNames = new int[TypeSystem.FieldCount];
        Objects.ZeroCache = new Value[Metadata.TableRows[MetadataTable.TypeDef] + 1];
    }

    /// <summary>Reads each Field row: whether the field is static, its name and its type.</summary>
    private static void LoadFields()
    {
        for (int i = 0; i < TypeSystem.FieldCount; i++)
        {
            ByteReader fieldRow = Metadata.Row(MetadataTable.Field, i + 1);
            TypeSystem.FieldIsStatic[i] = (Metadata.ReadU16(ref fieldRow) & FieldStatic) != 0 ? 1 : 0;
            TypeSystem.FieldNames[i] = Metadata.StringOffset(Metadata.ReadIndex(ref fieldRow, Metadata.StringIndexSize));
            ByteReader fieldSignature = Metadata.Blob(Metadata.ReadIndex(ref fieldRow, Metadata.BlobIndexSize));
            if (Metadata.ReadU8(ref fieldSignature) != FieldSignatureHeader)
            {
                Runtime.Fail("field signature");
            }

            TypeSystem.FieldTypes[i] = TypeSystem.ReadType(ref fieldSignature, 0);
        }
    }

    /// <summary>
    /// Walks the TypeDef rows to find each type's field range (it runs to the next type's first field), records the
    /// owner of every field, and numbers each type's instance fields from zero. Static fields get slot -1.
    /// </summary>
    private static void AssignFieldSlots()
    {
        for (int i = 1; i <= Metadata.TableRows[MetadataTable.TypeDef]; i++)
        {
            ByteReader typeRow = Metadata.Row(MetadataTable.TypeDef, i);
            Metadata.Consume(ref typeRow, TypeDefFlagsSize + 2 * Metadata.StringIndexSize + Metadata.TypeDefOrRefSize());
            int fieldListStart = Metadata.ReadU16(ref typeRow), fieldListEnd = TypeSystem.FieldCount + 1;
            if (i < Metadata.TableRows[MetadataTable.TypeDef])
            {
                ByteReader nextTypeRow = Metadata.Row(MetadataTable.TypeDef, i + 1);
                Metadata.Consume(ref nextTypeRow, TypeDefFlagsSize + 2 * Metadata.StringIndexSize + Metadata.TypeDefOrRefSize());
                fieldListEnd = Metadata.ReadU16(ref nextTypeRow);
            }

            TypeSystem.CheckFieldRange(fieldListStart, fieldListEnd);
            int slot = 0;
            for (int fieldIndex = fieldListStart - 1; fieldIndex < fieldListEnd - 1; fieldIndex++)
            {
                TypeSystem.FieldOwners[fieldIndex] = i;
                TypeSystem.FieldSlots[fieldIndex] = TypeSystem.FieldIsStatic[fieldIndex] == 0 ? slot++ : -1;
            }
        }
    }

    /// <summary>
    /// Allocates <c>Interpreter.Memory</c>: the value stack, followed by one slot for each field (only the static
    /// ones are used).
    /// </summary>
    private static void AllocateInterpreterMemory()
    {
        Interpreter.Memory = new Value[Limits.ValueStackSlots + TypeSystem.FieldCount];
        Interpreter.GlobalsBase = Limits.ValueStackSlots;
    }

    /// <summary>
    /// Reads each MethodDef row: its RVA, name and signature. Only static IL methods are accepted.
    /// </summary>
    private static void LoadMethods()
    {
        for (int i = 0; i < MethodCount; i++)
        {
            ByteReader methodRow = Metadata.Row(MetadataTable.MethodDef, i + 1);
            ref MethodBody body = ref Bodies[i];
            body.Rva = (int)Metadata.ReadU32(ref methodRow);
            if ((Metadata.ReadU16(ref methodRow) & MethodCodeTypeMask) != 0 || (Metadata.ReadU16(ref methodRow) & MethodStatic) == 0)
            {
                Runtime.Fail("static IL methods required");
            }

            body.NameOffset = Metadata.StringOffset(Metadata.ReadIndex(ref methodRow, Metadata.StringIndexSize));
            body.SignatureIndex = Metadata.ReadIndex(ref methodRow, Metadata.BlobIndexSize);
            MethodSignature methodSignature = TypeSystem.ReadMethodSignature(body.SignatureIndex);
            body.ArgCount = methodSignature.ParamCount;
            body.ReturnType = methodSignature.ReturnType;
            body.ArgTypes = methodSignature.ParamTypes;
        }
    }

    /// <summary>
    /// Decodes every method, then verifies every method. The two passes stay separate: no method is verified before
    /// all of them are decoded.
    /// </summary>
    private static void PrepareMethodBodies()
    {
        for (int i = 0; i < MethodCount; i++)
        {
            Decoder.Decode(i);
        }

        for (int i = 0; i < MethodCount; i++)
        {
            Verifier.Verify(i);
        }
    }

    /// <summary>Gives every static field its type's zero value.</summary>
    private static void InitializeGlobals()
    {
        for (int i = 0; i < TypeSystem.FieldCount; i++)
        {
            Interpreter.Memory[Interpreter.GlobalsBase + i] = Objects.ZeroValue(TypeSystem.FieldTypes[i], 0);
        }
    }
}
