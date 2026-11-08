using System;

namespace ProLang.Portable;
// This is a platform boundary, excluded from C translation. The PSP implementation
// supplies file IO, GU/controller/clock, cache maintenance and memory allocation only.
public static partial class Host
{
    static int frames, lines, rects, texts;
    static int limit = 60;
    static uint geometry = 2166136261;
    public static void Reset()
    {
        frames = lines = rects = texts = 0;
        geometry = 2166136261;
    }
    public static int Counter() => throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    public static int Helper(int n) => throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    public static int MethodSlot(int mid) => throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    public static int GlobalSlot(int index) => throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    public static void CodeReady(int mid, int name, uint[] code, int count) => throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    public static void Call(int mid, int frame, ref Val result) => throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    public static void ReleaseInts(int[] values) { }
    public static void ReleaseWords(uint[] values) { }
    public static void ReleaseVals(Val[] values) { }
    public static void ReleaseChars(ushort[] chars) { }
    public static void Collected(int count) { }
    public static void Fail(string message) => throw new InvalidOperationException(message);
    public static int Length(string s) => s.Length;
    public static void Ascii(string s, ushort[] p, int n)
    {
        for (int i = 0; i < n; i++)
        {
            p[i] = s[i];
        }
    }
    public static int Number(int n, bool unsigned, ushort[] p)
    {
        string s = unsigned ? ((uint)n).ToString(System.Globalization.CultureInfo.InvariantCulture) : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < s.Length; i++)
        {
            p[i] = s[i];
        }
        return s.Length;
    }
    public static void Write(ushort[] p, int n)
    {
        char[] chars = new char[n];
        for (int i = 0; i < n; i++)
        {
            chars[i] = (char)p[i];
        }
        Console.Write(chars);
    }
    public static void Init()
    {
    }

    public static void Clear(int colour)
    {
    }

    public static void Rect(int x, int y, int w, int h, int colour) => rects++;
    public static void Line(int x, int y, int xx, int yy, int colour)
    {
        lines++;
        geometry = unchecked((geometry ^ (uint)x) * 16777619);
        geometry = unchecked((geometry ^ (uint)y) * 16777619);
        geometry = unchecked((geometry ^ (uint)xx) * 16777619);
        geometry = unchecked((geometry ^ (uint)yy) * 16777619);
    }

    public static void Text(int x, int y, ushort[] p, int n, int colour) => texts++;
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

    public static int Millis() => frames * 17;
    public static void Sleep(int n)
    {
    }

    public static void Vsync()
    {
    }

    public static int Buttons() => frames >= limit - 1 ? 8 : 0;
}
