namespace ProLang.Portable;

/// <summary>
/// The guest object model: strings, struct records, arrays, boxes and managed addresses, all kept as
/// <c>HeapObject</c> entries in <c>Heap.HandleTable</c> and named by handle.
/// </summary>
/// <remarks>
/// <para>It also holds the value helpers built on them: the zero value of a type, equality, concatenation and
///   formatting of values for printing.</para>
/// </remarks>
internal static class Objects
{
    // The most Values one object or array may hold, and the most array elements that may be live at once.
    internal const int MaxObjectValues = 262144;
    internal const int MaxTotalArrayElements = 262144;
    // The longest string, in UTF-16 units.
    internal const int MaxStringLength = 32768;
    // Chains of managed addresses (an address of a field of an address ...) this deep are followed, no deeper.
    internal const int MaxAddressNesting = 32;
    // Units a number is formatted into: ten digits and a sign need at most 11.
    internal const int NumberBufferChars = 16;
    // Bytes charged to Heap.AllocationBytes, which paces the collector: per Value, per UTF-16 unit, per object.
    internal const int ValueBytes = 8;
    internal const int CharBytes = 2;
    internal const int ObjectOverheadBytes = 32;

    /// <summary>The zero value of each struct type, indexed by TypeDef row (row 0 is unused).</summary>
    /// <remarks>
    /// <para>An entry is a <c>Record</c> value that <c>ZeroValue</c> builds the first time the type needs one and
    ///   returns on every later request, or <c>default(Value)</c> while the type has none yet. The records are
    ///   permanent, so the collector never frees them, and every uninitialised struct of one type shares its
    ///   record. That is safe because a record is never edited in place; see <c>WriteAddress</c>.
    ///   <c>Loader.AllocateMemberTables</c> allocates the array.</para>
    /// </remarks>
    internal static Value[] ZeroCache = null!;
    /// <summary>
    /// The number of elements in all live arrays, which <c>NewArray</c> holds under <c>MaxTotalArrayElements</c>
    /// and the collector reduces when it frees an array.
    /// </summary>
    internal static int TotalArrayElements;

    /// <summary>Forgets the array element count.</summary>
    internal static void Reset()
    {
        TotalArrayElements = 0;
    }

    /// <summary>A <c>Value</c> of the given <c>ValueKind</c> whose <c>Data</c> is <c>handle</c>.</summary>
    internal static Value MakeReference(int kind, int handle)
    {
        Value value = default;
        value.Kind = kind;
        value.Data = handle;
        return value;
    }

    /// <summary>
    /// Returns the handle held by <c>value</c> after checking that the value's kind, the handle's range and the
    /// object's own kind all equal <c>kind</c>. Anything else fails with 'invalid managed reference'.
    /// </summary>
    internal static int RequireHandle(Value value, int kind)
    {
        if (value.Kind != kind || value.Data <= 0 || value.Data >= Heap.HandleTable.Length || Heap.HandleTable[value.Data].Kind != kind)
        {
            Runtime.Fail("invalid managed reference");
        }
        return value.Data;
    }

    /// <summary>
    /// Allocates a record-kind object of type code <c>type</c> with <c>count</c> null values, and charges its size
    /// to <c>Heap.AllocationBytes</c>. A caller that wants an array or a box changes <c>Kind</c> afterwards.
    /// <c>permanent</c> is 1 for an object the collector must never free. Fails with 'object size limit' beyond
    /// <c>MaxObjectValues</c>.
    /// </summary>
    internal static int NewObject(int type, int count, int permanent)
    {
        if (count < 0 || count > MaxObjectValues)
        {
            Runtime.Fail("object size limit");
        }
        int handle = Heap.AllocateObject(ValueKind.Record, permanent);
        Heap.HandleTable[handle].Type = type;
        Heap.HandleTable[handle].Count = count;
        Heap.HandleTable[handle].Values = new Value[count];
        Heap.AllocationBytes += count * ValueBytes + ObjectOverheadBytes;
        return handle;
    }

    /// <summary>
    /// Allocates a string object of <c>count</c> UTF-16 units, all zero, with one spare unit at the end. Fails with
    /// 'string length limit' beyond <c>MaxStringLength</c>.
    /// </summary>
    internal static int NewText(int count, int permanent)
    {
        if (count < 0 || count > MaxStringLength)
        {
            Runtime.Fail("string length limit");
        }
        int handle = Heap.AllocateObject(ValueKind.String, permanent);
        Heap.HandleTable[handle].Type = ElementType.String;
        Heap.HandleTable[handle].Count = count;
        Heap.HandleTable[handle].Chars = new ushort[count + 1];
        Heap.AllocationBytes += (count + 1) * CharBytes + ObjectOverheadBytes;
        return handle;
    }

    /// <summary>
    /// The default value of a type code. A struct gives a record of its fields' zero values, built once and kept in
    /// <c>ZeroCache</c>; <c>Boolean</c> through <c>U4</c> give an <c>Int32</c> 0; every other type gives null.
    /// <c>nesting</c> bounds struct fields that contain structs, at <c>Limits.TypeNesting</c>.
    /// </summary>
    internal static Value ZeroValue(int type, int nesting)
    {
        if (nesting > Limits.TypeNesting)
        {
            Runtime.Fail("recursive struct");
        }

        if ((type & TypeEncoding.StructTagMask) == TypeEncoding.Struct)
        {
            int row = type & TypeEncoding.StructRowMask;
            if (row < 1 || row > Metadata.TableRows[MetadataTable.TypeDef])
            {
                Runtime.Fail("struct type");
            }

            if (ZeroCache[row].Kind == ValueKind.Record)
            {
                return ZeroCache[row];
            }

            int fieldCount = 0;
            for (int i = 0; i < TypeSystem.FieldCount; i++)
            {
                if (TypeSystem.FieldOwners[i] == row && TypeSystem.FieldIsStatic[i] == 0)
                {
                    fieldCount++;
                }
            }

            int recordHandle = NewObject(type, fieldCount, 1);
            for (int i = 0; i < TypeSystem.FieldCount; i++)
            {
                if (TypeSystem.FieldOwners[i] == row && TypeSystem.FieldIsStatic[i] == 0)
                {
                    Heap.HandleTable[recordHandle].Values[TypeSystem.FieldSlots[i]] = ZeroValue(TypeSystem.FieldTypes[i], nesting + 1);
                }
            }

            ZeroCache[row] = MakeReference(ValueKind.Record, recordHandle);
            return ZeroCache[row];
        }

        if (type >= ElementType.Boolean && type <= ElementType.U4)
        {
            return Arithmetic.MakeInt32(0);
        }

        return default(Value);
    }

    /// <summary>
    /// Allocates an array of <c>count</c> elements of element type <c>type</c>, each set to <c>ZeroValue(type)</c>.
    /// The count is held against <c>MaxTotalArrayElements</c>, and fails with 'array heap limit exceeded' past it.
    /// </summary>
    internal static Value NewArray(int type, int count)
    {
        if (count < 0 || count > MaxTotalArrayElements - TotalArrayElements)
        {
            Runtime.Fail("array heap limit exceeded");
        }

        TotalArrayElements += count;
        int handle = NewObject(type, count, 0);
        Value value = ZeroValue(type, 0);
        for (int i = 0; i < count; i++)
        {
            Heap.HandleTable[handle].Values[i] = value;
        }

        Heap.HandleTable[handle].Kind = ValueKind.Array;
        return MakeReference(ValueKind.Array, handle);
    }

    /// <summary>
    /// Checks that <c>array</c> is an array value and <c>index</c> is within it, and returns the array's handle (not
    /// the element: the caller indexes <c>Values</c> itself). Fails with 'expected non-null array' or 'array index
    /// out of bounds'.
    /// </summary>
    internal static int RequireArrayHandleInBounds(Value array, int index)
    {
        if (array.Kind != ValueKind.Array)
        {
            Runtime.Fail("expected non-null array");
        }
        int arrayHandle = RequireHandle(array, ValueKind.Array);
        if (index < 0 || index >= Heap.HandleTable[arrayHandle].Count)
        {
            Runtime.Fail("array index out of bounds");
        }
        return arrayHandle;
    }

    /// <summary>
    /// The same check as <c>RequireArrayHandleInBounds</c>, starting from a raw handle, and returns the handle. The
    /// JIT's array element helper (<c>pvm_array_element</c> in <c>psp/entry.h</c>) calls it before the C side takes
    /// the address of the slot, so the checks the JIT code relies on are the interpreter's own.
    /// </summary>
    internal static int ValidateArray(int handle, int index)
    {
        if (handle <= 0 || handle >= Heap.HandleTable.Length || Heap.HandleTable[handle].Kind != ValueKind.Array)
        {
            return Runtime.Fail("expected non-null array");
        }
        if (index < 0 || index >= Heap.HandleTable[handle].Count)
        {
            return Runtime.Fail("array index out of bounds");
        }
        return handle;
    }

    /// <summary>
    /// The length of the array at <c>handle</c>, failing with 'expected non-null array' if it is not one. The C
    /// glue returns this method itself as the <c>JitAbi.HelperArrayLength</c> helper, so its signature (an int
    /// handle in, an int out) is part of the ABI.
    /// </summary>
    internal static int ArrayLength(int handle)
    {
        if (handle <= 0 || handle >= Heap.HandleTable.Length || Heap.HandleTable[handle].Kind != ValueKind.Array)
        {
            return Runtime.Fail("expected non-null array");
        }
        return Heap.HandleTable[handle].Count;
    }

    /// <summary>
    /// Checks that <c>handle</c> is a record whose type owns the instance field <c>field</c> (a field number), and
    /// returns the field's slot in the record's values. The JIT's field helper (<c>pvm_field_element</c> in
    /// <c>psp/entry.h</c>) calls it. Fails with 'invalid managed reference', 'invalid field owner' or 'field
    /// index'.
    /// </summary>
    internal static int ValidateField(int handle, int field)
    {
        if (handle <= 0 || handle >= Heap.HandleTable.Length || Heap.HandleTable[handle].Kind != ValueKind.Record)
        {
            return Runtime.Fail("invalid managed reference");
        }
        if (field < 0 || field >= TypeSystem.FieldCount || TypeSystem.FieldIsStatic[field] != 0 ||
            Heap.HandleTable[handle].Type != (TypeEncoding.Struct | TypeSystem.FieldOwners[field]))
        {
            return Runtime.Fail("invalid field owner");
        }
        int slot = TypeSystem.FieldSlots[field];
        if (slot < 0 || slot >= Heap.HandleTable[handle].Count)
        {
            return Runtime.Fail("field index");
        }
        return slot;
    }

    /// <summary>
    /// A new address of a slot of <c>Interpreter.Memory</c>, given as an absolute index. It is a root address.
    /// </summary>
    internal static Value SlotAddress(int slot)
    {
        int handle = Heap.AllocateObject(ValueKind.Address, 0);
        Heap.HandleTable[handle].ValueIndex = slot;
        return MakeReference(ValueKind.Address, handle);
    }

    /// <summary>
    /// A new address of one array element. It is a root address that records the array and the element index.
    /// </summary>
    internal static Value ArrayAddress(Value array, int index)
    {
        int arrayHandle = RequireArrayHandleInBounds(array, index);
        int handle = Heap.AllocateObject(ValueKind.Address, 0);
        Heap.HandleTable[handle].ArrayHandle = arrayHandle;
        Heap.HandleTable[handle].ValueIndex = index;
        return MakeReference(ValueKind.Address, handle);
    }

    /// <summary>
    /// A new address of field slot <c>field</c> of the record that the address <c>parent</c> names. It remembers
    /// <c>parent</c>, so writing through it can copy the record; see <c>WriteAddress</c>.
    /// </summary>
    internal static Value FieldAddress(Value parent, int field)
    {
        RequireHandle(parent, ValueKind.Address);
        int handle = Heap.AllocateObject(ValueKind.Address, 0);
        Heap.HandleTable[handle].Parent = parent.Data;
        Heap.HandleTable[handle].ValueIndex = field;
        return MakeReference(ValueKind.Address, handle);
    }

    /// <summary>
    /// Reads the value that the address at <c>handle</c> names. A root address reads its stack slot or array
    /// element; a field address reads the field of the record found by reading its parent address. <c>nesting</c>
    /// bounds the chain at <c>MaxAddressNesting</c>.
    /// </summary>
    internal static Value ReadAddress(int handle, int nesting)
    {
        if (nesting > MaxAddressNesting || handle <= 0 || handle >= Heap.HandleTable.Length || Heap.HandleTable[handle].Kind != ValueKind.Address)
        {
            Runtime.Fail("invalid managed address");
        }
        int parent = Heap.HandleTable[handle].Parent;
        int valueIndex = Heap.HandleTable[handle].ValueIndex;
        if (parent == 0)
        {
            int arrayHandle = Heap.HandleTable[handle].ArrayHandle;
            return arrayHandle == 0 ? Interpreter.Memory[valueIndex] : Heap.HandleTable[arrayHandle].Values[valueIndex];
        }
        int recordHandle = RequireHandle(ReadAddress(parent, nesting + 1), ValueKind.Record);
        if (valueIndex < 0 || valueIndex >= Heap.HandleTable[recordHandle].Count)
        {
            Runtime.Fail("field index");
        }
        return Heap.HandleTable[recordHandle].Values[valueIndex];
    }

    /// <summary>
    /// Stores <c>value</c> through the address at <c>handle</c>. A root address is written in place: its stack slot
    /// or its array element.
    /// </summary>
    /// <remarks>
    /// <para>A field address never edits a record. A struct is a value, and its record is shared by every copy of
    ///   it and by the cached zero record, so the write is copy-on-write: the record the parent address reads is
    ///   copied into a new, non-permanent record of the same type, the field is set in the copy, and the copy is
    ///   written back through the parent address. That write recurses up the chain of parents until it reaches a
    ///   root address, which stores the new record. Copies of the old struct keep seeing the old field values.
    ///   <c>nesting</c> bounds the chain at <c>MaxAddressNesting</c>.</para>
    /// </remarks>
    internal static void WriteAddress(int handle, Value value, int nesting)
    {
        if (nesting > MaxAddressNesting || handle <= 0 || handle >= Heap.HandleTable.Length || Heap.HandleTable[handle].Kind != ValueKind.Address)
        {
            Runtime.Fail("invalid managed address");
        }
        int parent = Heap.HandleTable[handle].Parent;
        int valueIndex = Heap.HandleTable[handle].ValueIndex;
        if (parent == 0)
        {
            int arrayHandle = Heap.HandleTable[handle].ArrayHandle;
            if (arrayHandle == 0)
            {
                Interpreter.Memory[valueIndex] = value;
            }
            else
            {
                Heap.HandleTable[arrayHandle].Values[valueIndex] = value;
            }
            return;
        }
        int oldRecord = RequireHandle(ReadAddress(parent, nesting + 1), ValueKind.Record);
        if (valueIndex < 0 || valueIndex >= Heap.HandleTable[oldRecord].Count)
        {
            Runtime.Fail("field index");
        }
        int newRecord = NewObject(Heap.HandleTable[oldRecord].Type, Heap.HandleTable[oldRecord].Count, 0);
        for (int i = 0; i < Heap.HandleTable[oldRecord].Count; i++)
        {
            Heap.HandleTable[newRecord].Values[i] = Heap.HandleTable[oldRecord].Values[i];
        }
        Heap.HandleTable[newRecord].Values[valueIndex] = value;
        WriteAddress(parent, MakeReference(ValueKind.Record, newRecord), nesting + 1);
    }

    /// <summary>The value that an address names, or <c>value</c> itself when it is not an address.</summary>
    internal static Value Deref(Value value)
    {
        return value.Kind == ValueKind.Address ? ReadAddress(value.Data, 0) : value;
    }

    /// <summary>
    /// Equality as the IL compare instructions and the string <c>==</c> operator need it. Values of different kinds
    /// are never equal; strings compare by content; every other kind compares its <c>Data</c>, so references
    /// compare by identity. <c>identity</c> is nonzero for the IL compare instructions, which compare references,
    /// so two strings then fail with 'string reference identity comparison is unsupported'; the string <c>==</c>
    /// and <c>!=</c> operators pass 0.
    /// </summary>
    internal static bool ValuesEqual(Value first, Value second, int identity)
    {
        if (first.Kind != second.Kind)
        {
            return false;
        }

        if (first.Kind == ValueKind.String)
        {
            if (identity != 0)
            {
                Runtime.Fail("string reference identity comparison is unsupported");
            }

            int firstHandle = RequireHandle(first, ValueKind.String);
            int secondHandle = RequireHandle(second, ValueKind.String);
            if (Heap.HandleTable[firstHandle].Count != Heap.HandleTable[secondHandle].Count)
            {
                return false;
            }

            for (int i = 0; i < Heap.HandleTable[firstHandle].Count; i++)
            {
                if (Heap.HandleTable[firstHandle].Chars[i] != Heap.HandleTable[secondHandle].Chars[i])
                {
                    return false;
                }
            }

            return true;
        }

        return first.Data == second.Data;
    }

    /// <summary>The handle of a new string holding the first string followed by the second.</summary>
    internal static int Concat(int firstHandle, int secondHandle)
    {
        int text = NewText(Heap.HandleTable[firstHandle].Count + Heap.HandleTable[secondHandle].Count, 0);
        for (int i = 0; i < Heap.HandleTable[firstHandle].Count; i++)
        {
            Heap.HandleTable[text].Chars[i] = Heap.HandleTable[firstHandle].Chars[i];
        }

        for (int i = 0; i < Heap.HandleTable[secondHandle].Count; i++)
        {
            Heap.HandleTable[text].Chars[Heap.HandleTable[firstHandle].Count + i] = Heap.HandleTable[secondHandle].Chars[i];
        }

        return text;
    }

    /// <summary>Allocates a string object holding ASCII text that the host copies in.</summary>
    private static int NewAsciiText(string ascii)
    {
        int length = Host.Length(ascii);
        int text = NewText(length, 0);
        Host.Ascii(ascii, Heap.HandleTable[text].Chars, length);
        return text;
    }

    /// <summary>
    /// The handle of a string that shows <c>value</c>, as <c>print</c> and string concatenation need it. A box is
    /// formatted as its contents under the boxed type; null is the empty string; a string is returned as it is. Any
    /// other value must be an <c>Int32</c>, shown according to <c>declaredType</c>: <c>Boolean</c> as False or
    /// True, <c>Char</c> as one character, <c>U4</c> as unsigned decimal, and anything else as signed decimal.
    /// </summary>
    internal static int FormatValue(Value value, int declaredType)
    {
        if (value.Kind == ValueKind.Box)
        {
            int boxHandle = RequireHandle(value, ValueKind.Box);
            return FormatValue(Heap.HandleTable[boxHandle].Values[0], Heap.HandleTable[boxHandle].Type);
        }

        if (value.Kind == ValueKind.Null)
        {
            return NewText(0, 0);
        }

        if (value.Kind == ValueKind.String)
        {
            return RequireHandle(value, ValueKind.String);
        }

        int number = Arithmetic.ExpectInt32(value);
        if (declaredType == ElementType.Boolean)
        {
            return NewAsciiText(number == 0 ? "False" : "True");
        }

        if (declaredType == ElementType.Char)
        {
            int charText = NewText(1, 0);
            Heap.HandleTable[charText].Chars[0] = (ushort)number;
            return charText;
        }

        int text = NewText(NumberBufferChars, 0);
        int length = Host.Number(number, declaredType == ElementType.U4, Heap.HandleTable[text].Chars);
        Heap.HandleTable[text].Count = length;
        return text;
    }
}
