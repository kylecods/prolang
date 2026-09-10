using System.Runtime.InteropServices;

namespace GLHelper;

[StructLayout(LayoutKind.Sequential)]
internal struct PIXELFORMATDESCRIPTOR
{
    public ushort nSize;
    public ushort nVersion;
    public uint dwFlags;
    public byte iPixelType;
    public byte cColorBits;
    public byte cRedBits;
    public byte cRedShift;
    public byte cGreenBits;
    public byte cGreenShift;
    public byte cBlueBits;
    public byte cBlueShift;
    public byte cAlphaBits;
    public byte cAlphaShift;
    public byte cAccumBits;
    public byte cAccumRedBits;
    public byte cAccumGreenBits;
    public byte cAccumBlueBits;
    public byte cAccumAlphaBits;
    public byte cDepthBits;
    public byte cStencilBits;
    public byte cAuxBuffers;
    public byte iLayerType;
    public byte bReserved;
    public uint dwLayerMask;
    public uint dwVisibleMask;
    public uint dwDamageMask;
}

internal static partial class WGL
{
    public const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    public const uint PFD_SUPPORT_OPENGL = 0x00000020;
    public const uint PFD_DOUBLEBUFFER = 0x00000001;
    public const byte PFD_TYPE_RGBA = 0;
    public const byte PFD_MAIN_PLANE = 0;

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SwapBuffers(IntPtr hdc);

    [LibraryImport("opengl32.dll", SetLastError = true)]
    public static partial IntPtr wglCreateContext(IntPtr hdc);

    [LibraryImport("opengl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [LibraryImport("opengl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool wglDeleteContext(IntPtr hglrc);

    [LibraryImport("opengl32.dll", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial IntPtr wglGetProcAddress(string name);

    [LibraryImport("opengl32.dll")]
    public static partial IntPtr wglGetCurrentContext();

    [LibraryImport("opengl32.dll")]
    public static partial IntPtr wglGetCurrentDC();

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial IntPtr GetProcAddress(IntPtr hModule, string procName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr _opengl32Handle = IntPtr.Zero;

    public static IntPtr GetAnyProcAddress(string name)
    {
        var proc = wglGetProcAddress(name);
        if (proc == IntPtr.Zero || proc == (IntPtr)1 || proc == (IntPtr)2 || proc == (IntPtr)3 || proc == (IntPtr)(-1))
        {
            if (_opengl32Handle == IntPtr.Zero)
            {
                _opengl32Handle = GetModuleHandle("opengl32.dll");
            }
            proc = GetProcAddress(_opengl32Handle, name);
        }
        return proc;
    }
}
