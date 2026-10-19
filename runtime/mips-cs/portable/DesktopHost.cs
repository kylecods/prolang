using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ProLang.Portable;

// This is a platform boundary, excluded from C translation. The PSP implementation
// supplies file IO, GU/controller/clock, cache maintenance and memory allocation only.
public static unsafe class Host
{
    static int frames, lines, rects, texts;
    static int limit = 60;
    static uint geometry = 2166136261;
    public static void* Perm(int n) => NativeMemory.AllocZeroed((nuint)Math.Max(n, 1));
    public static void* Alloc(int n) => Perm(n);
    public static void FreePermanent(void* p) => NativeMemory.Free(p);
    public static void FreeCode(void* p) => NativeMemory.Free(p);
    public static void Roots(void* p, int n) { }
    public static void* Counter() => null;
    public static void* Helper(int n) => null;
    public static void* Code(int n) => Perm(n);
    public static void CodeReady(int mid, byte* name, uint* code, int count) { }
    public static void Call(uint* code, Val* frame, Val* result) => throw new PlatformNotSupportedException("MIPS code executes only on PSP");
    public static void Fail(string message) => throw new InvalidOperationException(message);
    public static bool Equal(byte* p, string s) { for (int i = 0; i < s.Length; i++) if (p[i] == 0 || p[i] != s[i]) return false; return p[s.Length] == 0; }
    public static bool Starts(byte* p, string s) { for (int i = 0; i < s.Length; i++) if (p[i] == 0 || p[i] != s[i]) return false; return true; }
    public static bool Same(byte* a, byte* b) { for (int i = 0; ; i++) { if (a[i] != b[i]) return false; if (a[i] == 0) return true; } }
    public static bool BytesEqual(byte* a, byte* b, int n) { for (int i = 0; i < n; i++) if (a[i] != b[i]) return false; return true; }
    public static int Length(string s) => s.Length;
    public static void Ascii(string s, ushort* p, int n) { for (int i = 0; i < n; i++) p[i] = s[i]; }
    public static int Number(int n, bool unsigned, ushort* p)
    {
        string s = unsigned ? ((uint)n).ToString(System.Globalization.CultureInfo.InvariantCulture) : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < s.Length; i++) p[i] = s[i]; return s.Length;
    }
    public static void Write(ushort* p, int n) => Console.Write(new string((char*)p, 0, n));
    public static void Init() { }
    public static void Clear(int colour) { }
    public static void Rect(int x, int y, int w, int h, int colour) => rects++;
    public static void Line(int x, int y, int xx, int yy, int colour)
    {
        lines++;
        geometry = unchecked((geometry ^ (uint)x) * 16777619);
        geometry = unchecked((geometry ^ (uint)y) * 16777619);
        geometry = unchecked((geometry ^ (uint)xx) * 16777619);
        geometry = unchecked((geometry ^ (uint)yy) * 16777619);
    }
    public static void Text(int x, int y, ushort* p, int n, int colour) => texts++;
    public static void Present(int steps, int calls)
    {
        frames++; Console.WriteLine($"FRAME {frames} lines={lines} rects={rects} texts={texts} geometry={geometry:x8} instructions={steps} calls={calls}");
        lines = rects = texts = 0; geometry = 2166136261;
    }
    public static void CollectStats() { }
    public static int Millis() => frames * 17;
    public static void Sleep(int n) { }
    public static void Vsync() { }
    public static int Buttons() => frames >= limit - 1 ? 8 : 0;
    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1) throw new ArgumentException("Portable program.dll [frames]");
            if (args.Length > 1) limit = int.Parse(args[1]);
            byte[] data = File.ReadAllBytes(args[0]);
            fixed (byte* p = data) VM.Run(p, data.Length, 0);
            Console.WriteLine("PROLANG OK"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("PROLANG FAIL: " + e.Message); return 1; }
    }
}
