using System;

namespace ProLang.Portable;

/// <summary>Desktop-only checks of the portable collector and managed addresses.</summary>
internal static class HeapTests
{
    /// <summary>
    /// Checks that the collector keeps what is reachable from the value stack, permanent objects, address chains
    /// and the output buffer, frees the rest and reuses its handles, and that array and field validation and
    /// address reads and writes behave as the interpreter and the JIT's helpers expect.
    /// </summary>
    internal static void TestHeap()
    {
        Objects.TotalArrayElements = 0;
        Natives.OutputHandle = 0;
        Heap.Reset();
        Interpreter.Memory = new Value[16];
        int text = Objects.NewText(1, 0);
        Heap.HandleTable[text].Chars[0] = 'X';
        Value array = Objects.NewArray(14, 1);
        if (Objects.ValidateArray(array.Data, 0) != array.Data || Objects.ArrayLength(array.Data) != 1)
        {
            throw new Exception("portable array validation failed");
        }
        try
        {
            Objects.ValidateArray(array.Data, 1);
            throw new Exception("invalid array index was accepted");
        }
        catch (InvalidOperationException e) when (e.Message == "array index out of bounds")
        {
        }
        Heap.HandleTable[array.Data].Values[0] = Objects.MakeReference(2, text);
        Interpreter.Memory[0] = Objects.ArrayAddress(array, 0);
        int permanent = Objects.NewObject(1, 1, 1);
        Heap.HandleTable[permanent].Values[0] = Objects.MakeReference(2, text);
        int dead = Objects.NewText(10, 0);
        Heap.Collect();
        if (Heap.HandleTable[dead].Kind != 0 || Objects.ReadAddress(Interpreter.Memory[0].Data, 0).Data != text ||
            Heap.HandleTable[text].Chars[0] != 'X' || Heap.HandleTable[permanent].Values[0].Data != text)
        {
            throw new Exception("portable collector roots failed");
        }

        Value replacement = Arithmetic.MakeInt32(7);
        Objects.WriteAddress(Interpreter.Memory[0].Data, replacement, 0);
        if (Arithmetic.ExpectInt32(Heap.HandleTable[array.Data].Values[0]) != 7)
        {
            throw new Exception("portable array address failed");
        }

        int record = Objects.NewObject(0x100001, 1, 0);
        TypeSystem.FieldCount = 1;
        TypeSystem.FieldOwners = new int[1];
        TypeSystem.FieldSlots = new int[1];
        TypeSystem.FieldIsStatic = new int[1];
        TypeSystem.FieldOwners[0] = 1;
        if (Objects.ValidateField(record, 0) != 0)
        {
            throw new Exception("portable field validation failed");
        }
        TypeSystem.FieldIsStatic[0] = 1;
        try
        {
            Objects.ValidateField(record, 0);
            throw new Exception("invalid field was accepted");
        }
        catch (InvalidOperationException e) when (e.Message == "invalid field owner")
        {
        }
        TypeSystem.FieldIsStatic[0] = 0;
        Heap.HandleTable[record].Values[0] = Arithmetic.MakeInt32(4);
        Interpreter.Memory[1] = Objects.MakeReference(4, record);
        Interpreter.Memory[2] = Objects.FieldAddress(Objects.SlotAddress(1), 0);
        Interpreter.Memory[3] = Objects.MakeReference(7, 42); // Delegate values are method indices, not heap handles.
        Natives.OutputHandle = Objects.NewText(1, 0);
        Heap.Collect();
        Objects.WriteAddress(Interpreter.Memory[2].Data, Arithmetic.MakeInt32(9), 0);
        if (Arithmetic.ExpectInt32(Objects.ReadAddress(Interpreter.Memory[2].Data, 0)) != 9 || Interpreter.Memory[3].Data != 42 || Heap.HandleTable[Natives.OutputHandle].Kind != 2)
        {
            throw new Exception("portable nested address or output root failed");
        }

        for (int cycle = 0; cycle < 40; cycle++)
        {
            for (int i = 0; i < 500; i++)
            {
                Objects.NewText(8, 0);
            }
            Heap.Collect();
        }
        if (Heap.HandleTable[Interpreter.Memory[0].Data].Kind != 5 || Heap.HandleTable[array.Data].Kind != 3)
        {
            throw new Exception("portable collector lost a live address");
        }
        Console.WriteLine("PASS portable heap collection and address roots");
    }
}
