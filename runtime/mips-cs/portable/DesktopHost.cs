using System;
using System.Collections.Generic;

namespace ProLang.Portable;
/// <summary>
/// The desktop implementation of the platform boundary: everything the portable runtime asks of the system it runs
/// on.
/// </summary>
/// <remarks>
/// <para>This is a platform boundary, excluded from C translation. The portable classes call <c>Host</c> for
///   failure, output, display, input, time, releasing buffers, and executable code. On the PSP the same names are
///   the C functions of <c>psp/host.h</c> and <c>psp/entry.h</c> (<c>Host_Fail</c>, <c>Host_Write</c>, and so on),
///   which supply file I/O, GU, controller and clock, cache maintenance and memory allocation only. The build
///   type-checks the translated sources against this file, and never translates it.</para>
/// <para>This version runs no MIPS code, so the JIT's address queries and <c>Call</c> throw unless
///   <c>JitCapture</c> is on. Its drawing calls only count what they are given, and hash the line coordinates into
///   a geometry value, so a run can be compared with the PSP's. Its clock is deterministic (17 ms for each frame
///   presented) and it reports Start held on the last frame, which is what makes a frame-limited guest such as the
///   cube exit.</para>
/// </remarks>
public static partial class Host
{
    private static int frames, lines, rects, texts;
    private static int limit = 60;
    private static uint geometry = 2166136261;
    /// <summary>
    /// Zeroes the per-frame drawing counters, the frame count and the geometry hash, so that a second run in the
    /// same process starts from the beginning.
    /// </summary>
    public static void Reset()
    {
        frames = lines = rects = texts = 0;
        geometry = 2166136261;
    }
    /// <summary>
    /// Opt-in capture mode for the JIT golden test (see <c>JitGoldenTests</c>). With <c>JitCapture</c> on, the
    /// JIT's address queries return fixed fake addresses and <c>CodeReady</c> records a copy of each method's
    /// emitted words, so the generated code can be hashed without a PSP. With it off (the default) every query
    /// throws exactly as before: MIPS code only executes on the PSP.
    /// </summary>
    internal static bool JitCapture;
    /// <summary>The method ids that <c>CodeReady</c> captured, in the order the JIT compiled them.</summary>
    internal static List<int> CapturedMethods = new List<int>();
    /// <summary>The words captured for each entry of <c>CapturedMethods</c>.</summary>
    internal static List<uint[]> CapturedCode = new List<uint[]>();
    /// <summary>
    /// The address of the instruction step counter that JIT code charges. A fixed fake address under
    /// <c>JitCapture</c>, otherwise it throws.
    /// </summary>
    public static int Counter() => JitCapture ? 0x08C00000 : throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    /// <summary>
    /// The address of JIT helper number <c>n</c> (a <c>JitAbi</c> helper number). A fixed fake address under
    /// <c>JitCapture</c>, otherwise it throws.
    /// </summary>
    public static int Helper(int n) => JitCapture ? 0x08900000 + n * 0x100 : throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    /// <summary>
    /// The address of the code slot of method <c>mid</c>, through which JIT code calls it. A fixed fake address
    /// under <c>JitCapture</c>, otherwise it throws.
    /// </summary>
    public static int MethodSlot(int mid) => JitCapture ? 0x08A00000 + mid * 4 : throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    /// <summary>
    /// The address of slot <c>index</c> of the globals area. A fixed fake address under <c>JitCapture</c>,
    /// otherwise it throws.
    /// </summary>
    public static int GlobalSlot(int index) => JitCapture ? 0x08B00000 + index * 8 : throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    /// <summary>
    /// Receives the finished words of method <c>mid</c> from the JIT. On the PSP this copies them into executable
    /// memory, flushes the caches and logs a METHOD line; here it records a copy under <c>JitCapture</c> and
    /// otherwise throws.
    /// </summary>
    public static void CodeReady(int mid, int name, uint[] code, int count)
    {
        if (!JitCapture)
        {
            throw new PlatformNotSupportedException("MIPS code executes only on PSP");
        }

        uint[] copy = new uint[count];
        Array.Copy(code, copy, count);
        CapturedMethods.Add(mid);
        CapturedCode.Add(copy);
    }
    /// <summary>
    /// Calls the compiled code of method <c>mid</c> with its frame at <c>frame</c>. There is no MIPS execution on
    /// the desktop, so it throws.
    /// </summary>
    public static void Call(int mid, int frame, ref Value result) => throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    /// <summary>
    /// The <c>Release*</c> methods give back the arrays that the runtime has finished with (temporary buffers, and
    /// the arrays of objects the collector has freed). The PSP frees them explicitly; here the CLR collects them,
    /// so they do nothing.
    /// </summary>
    public static void ReleaseInts(int[] values) { }
    public static void ReleaseOps(DecodedOp[] values) { }
    public static void ReleaseWords(uint[] values) { }
    public static void ReleaseVals(Value[] values) { }
    public static void ReleaseChars(ushort[] chars) { }
    /// <summary>
    /// Called after each collection with the running collection count. The PSP counts them for its log; here it
    /// does nothing.
    /// </summary>
    public static void Collected(int count) { }
    /// <summary>
    /// Ends the program with <c>message</c>. It throws <c>InvalidOperationException</c>, whose message the tests
    /// match; on the PSP it prints the message and unwinds to <c>main</c>.
    /// </summary>
    public static void Fail(string message) => throw new InvalidOperationException(message);
    /// <summary>
    /// The length of a literal string that the runtime itself supplies, such as <c>True</c> and <c>False</c>.
    /// </summary>
    public static int Length(string s) => s.Length;
    /// <summary>
    /// Copies the first <c>n</c> characters of the ASCII text <c>s</c> into the UTF-16 buffer <c>p</c>.
    /// </summary>
    public static void Ascii(string s, ushort[] p, int n)
    {
        for (int i = 0; i < n; i++)
        {
            p[i] = s[i];
        }
    }
    /// <summary>
    /// Writes <c>n</c> into <c>p</c> as decimal text, signed or unsigned, and returns the number of characters
    /// written.
    /// </summary>
    public static int Number(int n, bool unsigned, ushort[] p)
    {
        string s = unsigned ? ((uint)n).ToString(System.Globalization.CultureInfo.InvariantCulture) : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < s.Length; i++)
        {
            p[i] = s[i];
        }
        return s.Length;
    }
    /// <summary>
    /// Writes the first <c>n</c> UTF-16 units of <c>p</c> to the console. On the PSP the text is converted to UTF-8
    /// and handed to the host's text output.
    /// </summary>
    public static void Write(ushort[] p, int n)
    {
        char[] chars = new char[n];
        for (int i = 0; i < n; i++)
        {
            chars[i] = (char)p[i];
        }
        Console.Write(chars);
    }
    /// <summary>Initialises the display. The desktop has none, so it does nothing.</summary>
    public static void Init()
    {
    }

    /// <summary>Clears the screen. Nothing is drawn on the desktop.</summary>
    public static void Clear(int colour)
    {
    }

    /// <summary>Counts a filled rectangle.</summary>
    public static void Rect(int x, int y, int w, int h, int colour) => rects++;
    /// <summary>
    /// Counts a line and folds its four coordinates into the frame's geometry hash (FNV-1a), which a run reports
    /// per frame so that two runs, and two platforms, can be compared on what they drew.
    /// </summary>
    public static void Line(int x, int y, int xx, int yy, int colour)
    {
        lines++;
        geometry = unchecked((geometry ^ (uint)x) * 16777619);
        geometry = unchecked((geometry ^ (uint)y) * 16777619);
        geometry = unchecked((geometry ^ (uint)xx) * 16777619);
        geometry = unchecked((geometry ^ (uint)yy) * 16777619);
    }

    /// <summary>Counts a text draw.</summary>
    public static void Text(int x, int y, ushort[] p, int n, int colour) => texts++;
    /// <summary>
    /// Ends a frame: counts it, writes one <c>FRAME</c> line with the draw counts, the geometry hash and the
    /// instruction and call counts, and zeroes the per-frame counters. <c>CollectStats</c> follows each present and
    /// does nothing on the desktop. The benchmark harness patches this method and the text after it by pattern, so
    /// their text must not change.
    /// </summary>
    public static void Present(int steps, int calls)
    {
        frames++;
        Console.WriteLine($"FRAME {frames} lines={lines} rects={rects} texts={texts} geometry={geometry:x8} instructions={steps} calls={calls}");
        lines = rects = texts = 0;
        geometry = 2166136261;
    }

    public static void CollectStats()
    {
    }

    /// <summary>
    /// The clock for guest code: 17 ms for each frame presented, so a run's timing-dependent output is the same
    /// every time.
    /// </summary>
    public static int Millis() => frames * 17;
    /// <summary>Sleeps for <c>n</c> milliseconds. The desktop does not wait.</summary>
    public static void Sleep(int n)
    {
    }

    /// <summary>Waits for the vertical blank. The desktop does not wait.</summary>
    public static void Vsync()
    {
    }

    /// <summary>
    /// The controller state: the Start bit (8) once the frame count reaches <c>limit - 1</c>, so a run ends after
    /// <c>limit</c> frames.
    /// </summary>
    public static int Buttons() => frames >= limit - 1 ? 8 : 0;
}
