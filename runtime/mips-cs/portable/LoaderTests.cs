using System;

namespace ProLang.Portable;
/// <summary>
/// Desktop-only checks for the portable loader's pointer and offset boundaries. It is also the entry point of the
/// whole self-test: it runs the value, heap and plan suites after its own checks.
/// </summary>
internal static class LoaderTests
{
    /// <summary>
    /// Checks that <c>Metadata.Rva</c>, <c>TypeSystem.CheckFieldRange</c> and <c>Metadata.ReadCString</c> accept
    /// the boundary cases and reject out-of-range ones with the expected messages, then runs
    /// <c>TestValueOperations</c>, <c>HeapTests.TestHeap</c> and <c>PlanTests.TestPlan</c>.
    /// </summary>
    internal static void TestLoaderBounds()
    {
        PeSection section = new()
        {
            Rva = 0x1000,
            RawOffset = 0xfffffff0,
            RawSize = 0x100
        };
        Metadata.Sections = new PeSection[1];
        Metadata.Sections[0] = section;
        Metadata.SectionCount = 1;
        Metadata.ImageLength = 128;
        ExpectBoundsFailure(() => Metadata.Rva(0x1020, 4));
        ExpectBoundsFailure(() => Metadata.Rva(0x1000, -1));
        Metadata.Sections[0].RawOffset = 96;
        if (Metadata.Rva(0x101c, 4) != 124)
        {
            throw new Exception("valid RVA boundary rejected");
        }

        TypeSystem.FieldCount = 3;
        TypeSystem.CheckFieldRange(1, 4);
        foreach (var (first, last) in new[] { (0, 1), (3, 2), (1, 5) })
        {
            try
            {
                TypeSystem.CheckFieldRange(first, last);
            }
            catch (InvalidOperationException e) when (e.Message == "invalid field range")
            {
                continue;
            }

            throw new Exception("expected field range failure");
        }

        Metadata.Image = new byte[1];
        ByteReader reader = Metadata.CreateReader(0, 1);
        if (Metadata.ReadCString(ref reader) != 0)
        {
            throw new Exception("empty string rejected");
        }

        reader.Position = -1;
        ExpectStringBoundsFailure(ref reader);
        reader.Position = 2;
        ExpectStringBoundsFailure(ref reader);
        Console.WriteLine("PASS portable loader bounds");
        TestValueOperations();
        HeapTests.TestHeap();
        PlanTests.TestPlan();
    }

    /// <summary>
    /// Checks <c>Arithmetic</c> (wrap-around, <c>int.MinValue % -1</c>, unsigned shift, division by zero) and the
    /// <c>MipsAssembler</c> encoders against known instruction words.
    /// </summary>
    private static void TestValueOperations()
    {
        if (Arithmetic.ExpectInt32(Arithmetic.MakeInt32(-42)) != -42 || Arithmetic.IsTruthy(Arithmetic.MakeInt32(0)) || !Arithmetic.IsTruthy(Arithmetic.MakeInt32(-1)) ||
            Arithmetic.Binary(0x58, int.MaxValue, 1) != int.MinValue ||
            Arithmetic.Binary(0x5d, int.MinValue, -1) != 0 || Arithmetic.Binary(0x64, -1, 1) != int.MaxValue ||
            MipsAssembler.EncodeR(8, 9, 8, 0, 33) != 0x01094021 || MipsAssembler.EncodeI(9, 0, 8, -1) != 0x2408ffff)
        {
            throw new Exception("portable value operations failed");
        }

        try
        {
            Arithmetic.Binary(0x5b, 1, 0);
        }
        catch (InvalidOperationException e) when (e.Message == "division by zero")
        {
            Console.WriteLine("PASS portable value operations");
            return;
        }

        throw new Exception("expected division by zero failure");
    }

    /// <summary>Requires <c>action</c> to fail with 'RVA outside image'.</summary>
    private static void ExpectBoundsFailure(Action action)
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

    /// <summary>Requires reading a string from <c>reader</c> to fail with 'binary read outside image'.</summary>
    private static void ExpectStringBoundsFailure(ref ByteReader reader)
    {
        try
        {
            Metadata.ReadCString(ref reader);
        }
        catch (InvalidOperationException e) when (e.Message == "binary read outside image")
        {
            return;
        }

        throw new Exception("expected string bounds failure");
    }
}
