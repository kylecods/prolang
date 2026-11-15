using System;

namespace ProLang.Portable;

/// <summary>
/// Desktop-only golden harness for the MIPS JIT. It never executes MIPS code: it loads a guest, runs <c>Compile</c>
/// with the host's capture mode on (fixed fake helper, slot and counter addresses), and hashes the words handed to
/// <c>Host.CodeReady</c>. Refactors of the loader, decoder, planner and JIT must leave every line of this output
/// byte-identical.
/// </summary>
internal static class JitGoldenTests
{
    private const uint FnvOffset = 2166136261;
    private const uint FnvPrime = 16777619;

    /// <summary>
    /// Prepares exactly as <c>Runtime.Run</c> does (reset, load) but compiles instead of invoking, then prints one
    /// <c>METHOD &lt;id&gt; words=&lt;count&gt; fnv=&lt;8 hex&gt;</c> line per method, in method-id order, and a
    /// <c>TOTAL methods=&lt;n&gt; words=&lt;sum&gt; fnv=&lt;8 hex&gt;</c> line, an FNV-1a hash over every word of
    /// every method in order. With <c>printWords</c>, each method line is followed by its words in hex, for diffing
    /// a mismatch.
    /// </summary>
    internal static void JitGolden(byte[] image, int length, bool printWords)
    {
        Runtime.Reset();
        Loader.Load(image, length);
        Host.CapturedMethods.Clear();
        Host.CapturedCode.Clear();
        Host.JitCapture = true;
        try
        {
            Jit.Compile();
        }
        finally
        {
            Host.JitCapture = false;
        }

        int count = Host.CapturedMethods.Count;
        uint[][] code = new uint[Loader.MethodCount][];
        for (int i = 0; i < count; i++)
        {
            code[Host.CapturedMethods[i]] = Host.CapturedCode[i];
        }

        uint total = FnvOffset;
        long words = 0;
        for (int methodId = 0; methodId < Loader.MethodCount; methodId++)
        {
            uint[] method = code[methodId];
            if (method == null)
            {
                throw new InvalidOperationException("JIT produced no code for method " + methodId);
            }

            uint hash = FnvOffset;
            for (int i = 0; i < method.Length; i++)
            {
                hash = FnvWord(hash, method[i]);
                total = FnvWord(total, method[i]);
            }

            words += method.Length;
            Console.WriteLine("METHOD " + methodId + " words=" + method.Length + " fnv=" + hash.ToString("x8"));
            if (printWords)
            {
                for (int i = 0; i < method.Length; i++)
                {
                    Console.WriteLine("  " + i.ToString("x4") + " " + method[i].ToString("x8"));
                }
            }
        }

        Console.WriteLine("TOTAL methods=" + Loader.MethodCount + " words=" + words + " fnv=" + total.ToString("x8"));
    }

    /// <summary>FNV-1a over the word's four little-endian bytes.</summary>
    private static uint FnvWord(uint hash, uint word)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash = unchecked((hash ^ ((word >> shift) & 0xff)) * FnvPrime);
        }

        return hash;
    }
}
