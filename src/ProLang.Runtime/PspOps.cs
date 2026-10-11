namespace ProLang.Runtime;

/// <summary>MSIL imports implemented by the PSP interpreter's native host.</summary>
public static class PspOps
{
    private static void RequirePsp() => throw new PlatformNotSupportedException("PSP builtins require the PSP runtime.");
    /// <summary>Initialize the GU display and controller.</summary>
    public static void Init() => RequirePsp();
    /// <summary>Clear the drawing buffer to an RGB color.</summary>
    public static void Clear(int color) => RequirePsp();
    /// <summary>Draw a filled rectangle.</summary>
    public static void FillRect(int x, int y, int w, int h, int color) => RequirePsp();
    /// <summary>Draw text with the PSP bitmap font.</summary>
    public static void DrawText(int x, int y, string text, int color) => RequirePsp();
    /// <summary>Present the completed drawing buffer.</summary>
    public static void SwapBuffers() => RequirePsp();
    /// <summary>Wait for vertical blank.</summary>
    public static void Vsync() => RequirePsp();
    /// <summary>Read the controller button mask.</summary>
    public static int ButtonsHeld() { RequirePsp(); return 0; }
    /// <summary>Test whether the given button mask is held.</summary>
    public static bool ButtonPressed(int button) { RequirePsp(); return false; }
    /// <summary>Draw a line between two points.</summary>
    public static void DrawLine(int x1, int y1, int x2, int y2, int color) => RequirePsp();
}
