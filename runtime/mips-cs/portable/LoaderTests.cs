using System;

namespace ProLang.Portable;
// Desktop-only checks for the portable loader's pointer and offset boundaries.
public static partial class VM
{
    internal static void TestLoaderBounds()
    {
        Section section = new()
        {
            rva = 0x1000,
            raw = 0xfffffff0,
            size = 0x100
        };
        sections = new Section[1];
        sections[0] = section;
        sectionCount = 1;
        imageLength = 128;
        ExpectBoundsFailure(() => Rva(0x1020, 4));
        ExpectBoundsFailure(() => Rva(0x1000, -1));
        sections[0].raw = 96;
        if (Rva(0x101c, 4) != 124)
        {
            throw new Exception("valid RVA boundary rejected");
        }

        fieldCount = 3;
        CheckFieldRange(1, 4);
        foreach (var (first, last) in new[] { (0, 1), (3, 2), (1, 5) })
        {
            try
            {
                CheckFieldRange(first, last);
            }
            catch (InvalidOperationException e) when (e.Message == "invalid field range")
            {
                continue;
            }

            throw new Exception("expected field range failure");
        }

        image = new byte[1];
        Reader reader = Read(0, 1);
        if (Z(ref reader) != 0)
        {
            throw new Exception("empty string rejected");
        }

        reader.pos = -1;
        ExpectStringBoundsFailure(ref reader);
        reader.pos = 2;
        ExpectStringBoundsFailure(ref reader);
        Console.WriteLine("PASS portable loader bounds");
        TestValueOperations();
        TestHeap();
        TestPlan();
    }

    static void TestValueOperations()
    {
        if (Int(Integer(-42)) != -42 || Truth(Integer(0)) || !Truth(Integer(-1)) ||
            Binary(0x58, int.MaxValue, 1) != int.MinValue ||
            Binary(0x5d, int.MinValue, -1) != 0 || Binary(0x64, -1, 1) != int.MaxValue ||
            R(8, 9, 8, 0, 33) != 0x01094021 || I(9, 0, 8, -1) != 0x2408ffff)
        {
            throw new Exception("portable value operations failed");
        }

        try
        {
            Binary(0x5b, 1, 0);
        }
        catch (InvalidOperationException e) when (e.Message == "division by zero")
        {
            Console.WriteLine("PASS portable value operations");
            return;
        }

        throw new Exception("expected division by zero failure");
    }

    static void ExpectBoundsFailure(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException e) when (e.Message == "RVA outside image")
        {
            return;
        }

        throw new Exception("expected RVA bounds failure");
    }

    static void ExpectStringBoundsFailure(ref Reader reader)
    {
        try
        {
            Z(ref reader);
        }
        catch (InvalidOperationException e) when (e.Message == "binary read outside image")
        {
            return;
        }

        throw new Exception("expected string bounds failure");
    }
}
