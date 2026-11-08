using System;

namespace ProLang.Portable;

// Desktop-only checks of the portable collector and managed addresses.
public static partial class VM
{
    static void TestHeap()
    {
        totalElements = 0;
        output = 0;
        ResetHeap();
        memory = new Val[16];
        int text = NewText(1, 0);
        objects[text].chars[0] = 'X';
        Val array = NewArray(14, 1);
        if (ValidateArray(array.data, 0) != array.data || ArrayLength(array.data) != 1)
        {
            throw new Exception("portable array validation failed");
        }
        try
        {
            ValidateArray(array.data, 1);
            throw new Exception("invalid array index was accepted");
        }
        catch (InvalidOperationException e) when (e.Message == "array index out of bounds")
        {
        }
        objects[array.data].values[0] = Ref(2, text);
        memory[0] = ArrayAddress(array, 0);
        int permanent = NewObject(1, 1, 1);
        objects[permanent].values[0] = Ref(2, text);
        int dead = NewText(10, 0);
        Collect();
        if (objects[dead].kind != 0 || ReadAddress(memory[0].data, 0).data != text ||
            objects[text].chars[0] != 'X' || objects[permanent].values[0].data != text)
        {
            throw new Exception("portable collector roots failed");
        }

        Val replacement = Integer(7);
        WriteAddress(memory[0].data, replacement, 0);
        if (Int(objects[array.data].values[0]) != 7)
        {
            throw new Exception("portable array address failed");
        }

        int record = NewObject(0x100001, 1, 0);
        fieldCount = 1;
        fieldOwners = new int[1];
        fieldSlots = new int[1];
        fieldStatic = new int[1];
        fieldOwners[0] = 1;
        if (ValidateField(record, 0) != 0)
        {
            throw new Exception("portable field validation failed");
        }
        fieldStatic[0] = 1;
        try
        {
            ValidateField(record, 0);
            throw new Exception("invalid field was accepted");
        }
        catch (InvalidOperationException e) when (e.Message == "invalid field owner")
        {
        }
        fieldStatic[0] = 0;
        objects[record].values[0] = Integer(4);
        memory[1] = Ref(4, record);
        memory[2] = FieldAddress(SlotAddress(1), 0);
        memory[3] = Ref(7, 42); // Delegate values are method indices, not heap handles.
        output = NewText(1, 0);
        Collect();
        WriteAddress(memory[2].data, Integer(9), 0);
        if (Int(ReadAddress(memory[2].data, 0)) != 9 || memory[3].data != 42 || objects[output].kind != 2)
        {
            throw new Exception("portable nested address or output root failed");
        }

        for (int cycle = 0; cycle < 40; cycle++)
        {
            for (int i = 0; i < 500; i++)
            {
                NewText(8, 0);
            }
            Collect();
        }
        if (objects[memory[0].data].kind != 5 || objects[array.data].kind != 3)
        {
            throw new Exception("portable collector lost a live address");
        }
        Console.WriteLine("PASS portable heap collection and address roots");
    }
}
