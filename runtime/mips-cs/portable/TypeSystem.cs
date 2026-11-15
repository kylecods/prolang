namespace ProLang.Portable;

/// <summary>
/// Signature decoding and the field tables: turns the type notations in the metadata into the runtime's type codes.
/// </summary>
/// <remarks>
/// <para>Type codes are <c>ElementType</c> values for primitives, strings, objects and classes, and the composite
///   <c>TypeEncoding</c> forms for structs, generic parameters and arrays.</para>
/// <para>The field tables are parallel arrays indexed by field number (the Field row minus one), filled by
///   <c>Loader</c>: <c>FieldTypes</c> holds each field's type code, <c>FieldOwners</c> the TypeDef row that owns
///   it, <c>FieldSlots</c> the index of an instance field in its record's <c>Values</c> (-1 for a static field),
///   <c>FieldIsStatic</c> a 0/1 flag, and <c>FieldNames</c> the file offset of its name. <c>ResolvedTypeName</c>
///   and <c>ResolvedTypeNamespace</c> are the file offsets of the names that <c>ResolveTypeName</c> last found.</para>
/// </remarks>
internal static class TypeSystem
{
    // Most parameters a method signature may declare.
    internal const int MaxParameters = 64;
    // The first byte of a method signature (ECMA-335 II.23.2.1): the HASTHIS flag, and the calling
    // convention with the GENERIC flag. Only the default calling convention of a non-generic method is accepted.
    internal const int SignatureHasThis = 32;
    internal const int SignatureConventionMask = 31;

    internal static int FieldCount;
    internal static int[] FieldTypes = null!, FieldOwners = null!, FieldSlots = null!, FieldIsStatic = null!;
    internal static int[] FieldNames = null!;
    internal static int ResolvedTypeName;
    internal static int ResolvedTypeNamespace;

    /// <summary>Empties the field table count and forgets the last resolved type name.</summary>
    internal static void Reset()
    {
        FieldCount = 0;
        ResolvedTypeName = ResolvedTypeNamespace = 0;
    }

    /// <summary>
    /// Fails with 'invalid field range' unless <c>1 &lt;= first &lt;= last &lt;= FieldCount + 1</c>. A TypeDef's
    /// fields are the half-open range of Field rows from its own first field up to the next type's first field.
    /// </summary>
    internal static void CheckFieldRange(int first, int last)
    {
        if (first < 1 || first > last || last > FieldCount + 1)
        {
            Runtime.Fail("invalid field range");
        }
    }

    /// <summary>
    /// Turns a TypeDefOrRef coded index into a full metadata token (table id in the high byte, row in the rest).
    /// </summary>
    private static uint TypeDefOrRefToken(int coded)
    {
        int table = (coded & MetadataToken.TypeDefOrRefTagMask) == MetadataToken.TypeDefOrRefTypeDef ? MetadataTable.TypeDef : ((coded & MetadataToken.TypeDefOrRefTagMask) == MetadataToken.TypeDefOrRefTypeRef ? MetadataTable.TypeRef : MetadataTable.TypeSpec);
        return ((uint)table << MetadataToken.TableShift) | (uint)(coded >> MetadataToken.TypeDefOrRefTagBits);
    }

    /// <summary>
    /// Finds the name and namespace of the type that a TypeDef, TypeRef or TypeSpec token names and leaves them in
    /// <c>ResolvedTypeName</c> and <c>ResolvedTypeNamespace</c>. A TypeSpec is accepted only as a generic
    /// instantiation of a class, and resolves to that class. <c>nesting</c> bounds the recursion at
    /// <c>Limits.TypeNesting</c>.
    /// </summary>
    internal static void ResolveTypeName(uint token, int nesting)
    {
        if (nesting > Limits.TypeNesting)
        {
            Runtime.Fail("type nesting limit");
        }

        int table = (int)(token >> MetadataToken.TableShift);
        ByteReader reader = Metadata.Row(table, (int)(token & MetadataToken.RowMask));
        if (table == MetadataTable.TypeSpec)
        {
            ByteReader typeSpecBlob = Metadata.Blob(Metadata.ReadIndex(ref reader, Metadata.BlobIndexSize));
            if (Metadata.ReadU8(ref typeSpecBlob) != ElementType.GenericInst || Metadata.ReadU8(ref typeSpecBlob) != ElementType.Class)
            {
                Runtime.Fail("unsupported TypeSpec");
            }

            ResolveTypeName(TypeDefOrRefToken(Metadata.ReadCompressed(ref typeSpecBlob)), nesting + 1);
            return;
        }

        if (table == MetadataTable.TypeRef)
        {
            Metadata.ReadIndex(ref reader, Metadata.ResolutionScopeSize());
        }
        else if (table == MetadataTable.TypeDef)
        {
            Metadata.ReadU32(ref reader);
        }
        else
        {
            Runtime.Fail("unsupported type token");
        }

        ResolvedTypeName = Metadata.StringOffset(Metadata.ReadIndex(ref reader, Metadata.StringIndexSize));
        ResolvedTypeNamespace = Metadata.StringOffset(Metadata.ReadIndex(ref reader, Metadata.StringIndexSize));
    }

    /// <summary>Reads one type from a signature blob and returns its type code.</summary>
    /// <remarks>
    /// <para>The <c>ElementType</c> code for void, bool, char, the integer types up to 32 bits, string, object and
    ///   native int; <c>TypeEncoding.Struct | row</c> for a value type defined in this program;
    ///   <c>TypeEncoding.GenericParam | index</c> for <c>!n</c>; <c>ElementType.Class</c> for a class or a generic
    ///   instantiation of one; and, for a single-dimension array, <c>PrimitiveArray | element</c> or
    ///   <c>CompositeArray | element</c>. Arrays of arrays, arrays of void, external value types and anything else
    ///   fail.</para>
    /// </remarks>
    internal static int ReadType(ref ByteReader reader, int nesting)
    {
        if (nesting > Limits.TypeNesting)
        {
            Runtime.Fail("type nesting limit");
        }

        int typeCode = Metadata.ReadU8(ref reader);
        if (typeCode == ElementType.Void || typeCode == ElementType.Boolean || typeCode == ElementType.Char || typeCode == ElementType.I1 || typeCode == ElementType.U1 || typeCode == ElementType.I2 || typeCode == ElementType.U2 || typeCode == ElementType.I4 || typeCode == ElementType.U4 || typeCode == ElementType.String || typeCode == ElementType.Object || typeCode == ElementType.I)
        {
            return typeCode;
        }

        if (typeCode == ElementType.ValueType)
        {
            int typeDefOrRef = Metadata.ReadCompressed(ref reader);
            if ((typeDefOrRef & MetadataToken.TypeDefOrRefTagMask) != MetadataToken.TypeDefOrRefTypeDef)
            {
                Runtime.Fail("external value type");
            }

            return TypeEncoding.Struct | (typeDefOrRef >> MetadataToken.TypeDefOrRefTagBits);
        }

        if (typeCode == ElementType.Class)
        {
            Metadata.ReadCompressed(ref reader);
            return ElementType.Class;
        }

        if (typeCode == ElementType.Var)
        {
            return TypeEncoding.GenericParam | Metadata.ReadCompressed(ref reader);
        }

        if (typeCode == ElementType.SzArray)
        {
            int elementType = ReadType(ref reader, nesting + 1);
            if (elementType == ElementType.Void || (elementType & TypeEncoding.ArrayMask) != 0)
            {
                Runtime.Fail("unsupported array type");
            }

            return elementType < TypeEncoding.PrimitiveArray ? TypeEncoding.PrimitiveArray | elementType : TypeEncoding.CompositeArray | elementType;
        }

        if (typeCode == ElementType.GenericInst)
        {
            if (Metadata.ReadU8(ref reader) != ElementType.Class)
            {
                Runtime.Fail("generic value type");
            }

            Metadata.ReadCompressed(ref reader);
            int argCount = Metadata.ReadCompressed(ref reader);
            if (argCount > Limits.GenericArity)
            {
                Runtime.Fail("generic arity limit");
            }

            for (int i = 0; i < argCount; i++)
            {
                ReadType(ref reader, nesting + 1);
            }

            return ElementType.Class;
        }

        return Runtime.Fail("unsupported signature type");
    }

    /// <summary>
    /// Decodes the method signature blob at <c>blobIndex</c>: the HASTHIS flag, the parameter count (at most
    /// <c>MaxParameters</c>), the return type and the parameter types. A generic method or a non-default calling
    /// convention fails.
    /// </summary>
    internal static MethodSignature ReadMethodSignature(int blobIndex)
    {
        ByteReader reader = Metadata.Blob(blobIndex);
        MethodSignature signature = default;
        int flags = Metadata.ReadU8(ref reader);
        if ((flags & SignatureConventionMask) != 0)
        {
            Runtime.Fail("generic method signature");
        }

        signature.HasThis = (flags & SignatureHasThis) != 0 ? 1 : 0;
        signature.ParamCount = Metadata.ReadCompressed(ref reader);
        if (signature.ParamCount > MaxParameters)
        {
            Runtime.Fail("parameter limit");
        }

        signature.ReturnType = ReadType(ref reader, 0);
        signature.ParamTypes = new int[signature.ParamCount];
        for (int i = 0; i < signature.ParamCount; i++)
        {
            signature.ParamTypes[i] = ReadType(ref reader, 0);
        }

        return signature;
    }

    /// <summary>
    /// Maps the field token of a <c>ldfld</c>, <c>stfld</c>, <c>ldsfld</c> or similar instruction to a field
    /// number. A Field token is that row minus one; a MemberRef of a field of a type in this program is matched
    /// against the Field rows by owning type, name and signature bytes. A field of an external type fails.
    /// </summary>
    internal static int ResolveFieldIndex(uint token)
    {
        int row = (int)(token & MetadataToken.RowMask);
        if ((token >> MetadataToken.TableShift) == MetadataTable.Field)
        {
            if (row < 1 || row > FieldCount)
            {
                Runtime.Fail("field token");
            }

            return row - 1;
        }

        if ((token >> MetadataToken.TableShift) != MetadataTable.MemberRef)
        {
            return Runtime.Fail("field token");
        }

        ByteReader memberRow = Metadata.Row(MetadataTable.MemberRef, row);
        int parent = Metadata.ReadIndex(ref memberRow, Metadata.MemberRefParentSize());
        if ((parent & MetadataToken.MemberRefParentTagMask) != MetadataToken.MemberRefParentTypeDef)
        {
            Runtime.Fail("external field");
        }

        int nameOffset = Metadata.StringOffset(Metadata.ReadIndex(ref memberRow, Metadata.StringIndexSize));
        ByteReader memberSignature = Metadata.Blob(Metadata.ReadIndex(ref memberRow, Metadata.BlobIndexSize));
        for (int i = 0; i < FieldCount; i++)
        {
            if (FieldOwners[i] == (parent >> MetadataToken.MemberRefParentTagBits) && Metadata.NamesEqual(FieldNames[i], nameOffset))
            {
                ByteReader fieldRow = Metadata.Row(MetadataTable.Field, i + 1);
                Metadata.ReadU16(ref fieldRow);
                Metadata.ReadIndex(ref fieldRow, Metadata.StringIndexSize);
                ByteReader fieldSignature = Metadata.Blob(Metadata.ReadIndex(ref fieldRow, Metadata.BlobIndexSize));
                if (memberSignature.Length == fieldSignature.Length && Metadata.BytesEqual(memberSignature.Start, fieldSignature.Start, memberSignature.Length))
                {
                    return i;
                }
            }
        }

        return Runtime.Fail("unresolved field");
    }

    /// <summary>
    /// The type code that a type token operand names: <c>TypeEncoding.Struct | row</c> for a TypeDef, and for a
    /// TypeRef into <c>System</c> only Int32, UInt32, Boolean, Char, Byte, String and Object (as <c>I4</c>,
    /// <c>U4</c>, <c>Boolean</c>, <c>Char</c>, <c>U1</c>, <c>String</c> and <c>Object</c>). Any other type fails.
    /// </summary>
    internal static int TypeFromToken(uint token)
    {
        if ((token >> MetadataToken.TableShift) == MetadataTable.TypeDef)
        {
            return TypeEncoding.Struct | (int)(token & MetadataToken.RowMask);
        }

        ResolveTypeName(token, 0);
        if (!Metadata.NameEqualsText(ResolvedTypeNamespace, "System"))
        {
            return Runtime.Fail("external type");
        }

        if (Metadata.NameEqualsText(ResolvedTypeName, "Int32"))
        {
            return ElementType.I4;
        }

        if (Metadata.NameEqualsText(ResolvedTypeName, "UInt32"))
        {
            return ElementType.U4;
        }

        if (Metadata.NameEqualsText(ResolvedTypeName, "Boolean"))
        {
            return ElementType.Boolean;
        }

        if (Metadata.NameEqualsText(ResolvedTypeName, "Char"))
        {
            return ElementType.Char;
        }

        if (Metadata.NameEqualsText(ResolvedTypeName, "Byte"))
        {
            return ElementType.U1;
        }

        if (Metadata.NameEqualsText(ResolvedTypeName, "String"))
        {
            return ElementType.String;
        }

        if (Metadata.NameEqualsText(ResolvedTypeName, "Object"))
        {
            return ElementType.Object;
        }

        return Runtime.Fail("unsupported token type");
    }
}
