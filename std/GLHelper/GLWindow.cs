using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace GLHelper;

internal sealed class NativeGLWindow
{
    public int Id;
    public IntPtr Hwnd;
    public IntPtr Hdc;
    public IntPtr Hglrc;
    public int Width;
    public int Height;
    public bool ShouldClose;
    public readonly bool[] Keys = new bool[256];
    public readonly bool[] MouseButtons = new bool[8];
    public int MouseX;
    public int MouseY;
}

public static partial class GL
{
    private static readonly ConcurrentDictionary<int, NativeGLWindow> _windows = new();
    private static readonly ConcurrentDictionary<IntPtr, NativeGLWindow> _hwndMap = new();
    private static int _nextWindowId = 1;
    private static NativeGLWindow? _activeWindow;

    // Win32 constants
    private const uint CS_OWNDC = 0x0020;
    private const uint CS_HREDRAW = 0x0002;
    private const uint CS_VREDRAW = 0x0001;

    private const uint WS_OVERLAPPED = 0x00000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
    private const uint WS_VISIBLE = 0x10000000;

    private const int CW_USEDEFAULT = unchecked((int)0x80000000);

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_SIZE = 0x0005;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;

    private const uint PM_REMOVE = 0x0001;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Win32Point pt;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static unsafe partial ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRect(ref RECT lpRect, uint dwStyle, [MarshalAs(UnmanagedType.Bool)] bool bMenu);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    private static readonly WndProcDelegate _wndProcDelegate = ProcessMessage;
    private static bool _classRegistered;
    private const string WindowClassName = "ProLangGLWindowClass";

    private static unsafe void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        var hInstance = WGL.GetModuleHandle(null);
        fixed (char* classNamePtr = WindowClassName)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)sizeof(WNDCLASSEX),
                style = CS_OWNDC | CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = hInstance,
                hIcon = IntPtr.Zero,
                hCursor = LoadCursor(IntPtr.Zero, (IntPtr)32512), // IDC_ARROW
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = classNamePtr,
                hIconSm = IntPtr.Zero
            };

            RegisterClassEx(ref wc);
        }
        _classRegistered = true;
    }

    private static IntPtr ProcessMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_hwndMap.TryGetValue(hWnd, out var window))
        {
            switch (msg)
            {
                case WM_SIZE:
                    window.Width = (int)(lParam.ToInt64() & 0xFFFF);
                    window.Height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                    break;
                case WM_CLOSE:
                    window.ShouldClose = true;
                    return IntPtr.Zero;
                case WM_DESTROY:
                    window.ShouldClose = true;
                    return IntPtr.Zero;
                case WM_ERASEBKGND:
                    return (IntPtr)1; // Suppress background erase to prevent flickering
                case WM_KEYDOWN:
                    var kd = (int)wParam.ToInt64();
                    if (kd >= 0 && kd < 256) window.Keys[kd] = true;
                    break;
                case WM_KEYUP:
                    var ku = (int)wParam.ToInt64();
                    if (ku >= 0 && ku < 256) window.Keys[ku] = false;
                    break;
                case WM_MOUSEMOVE:
                    window.MouseX = (short)(lParam.ToInt64() & 0xFFFF);
                    window.MouseY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                    break;
                case WM_LBUTTONDOWN:
                    window.MouseButtons[0] = true;
                    break;
                case WM_LBUTTONUP:
                    window.MouseButtons[0] = false;
                    break;
                case WM_RBUTTONDOWN:
                    window.MouseButtons[1] = true;
                    break;
                case WM_RBUTTONUP:
                    window.MouseButtons[1] = false;
                    break;
                case WM_MBUTTONDOWN:
                    window.MouseButtons[2] = true;
                    break;
                case WM_MBUTTONUP:
                    window.MouseButtons[2] = false;
                    break;
            }
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Creates an OpenGL window and initializes an OpenGL rendering context.
    /// Returns the window ID handle.
    /// </summary>
    public static int CreateWindow(string title, int width, int height)
    {
        EnsureClassRegistered();

        var hInstance = WGL.GetModuleHandle(null);

        RECT rect = new RECT { left = 0, top = 0, right = width, bottom = height };
        AdjustWindowRect(ref rect, WS_OVERLAPPEDWINDOW, false);

        int winWidth = rect.right - rect.left;
        int winHeight = rect.bottom - rect.top;

        var hwnd = CreateWindowEx(
            0, WindowClassName, title,
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT, CW_USEDEFAULT,
            winWidth, winHeight,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create native Win32 window for OpenGL.");
        }

        var hdc = GetDC(hwnd);

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = WGL.PFD_DRAW_TO_WINDOW | WGL.PFD_SUPPORT_OPENGL | WGL.PFD_DOUBLEBUFFER,
            iPixelType = WGL.PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = WGL.PFD_MAIN_PLANE
        };

        int format = WGL.ChoosePixelFormat(hdc, ref pfd);
        if (format == 0 || !WGL.SetPixelFormat(hdc, format, ref pfd))
        {
            ReleaseDC(hwnd, hdc);
            DestroyWindow(hwnd);
            throw new InvalidOperationException("Failed to set OpenGL pixel format.");
        }

        var hglrc = WGL.wglCreateContext(hdc);
        if (hglrc == IntPtr.Zero || !WGL.wglMakeCurrent(hdc, hglrc))
        {
            ReleaseDC(hwnd, hdc);
            DestroyWindow(hwnd);
            throw new InvalidOperationException("Failed to create or activate OpenGL context.");
        }

        LoadExtensions();

        // Enable VSync by default if extension available
        SwapInterval(1);

        int id = Interlocked.Increment(ref _nextWindowId);
        var win = new NativeGLWindow
        {
            Id = id,
            Hwnd = hwnd,
            Hdc = hdc,
            Hglrc = hglrc,
            Width = width,
            Height = height,
            ShouldClose = false
        };

        _windows[id] = win;
        _hwndMap[hwnd] = win;
        _activeWindow = win;

        // Initial viewport
        Viewport(0, 0, width, height);

        return id;
    }

    /// <summary>Makes the OpenGL context of the specified window current.</summary>
    public static void MakeCurrent(int windowId)
    {
        if (_windows.TryGetValue(windowId, out var win))
        {
            WGL.wglMakeCurrent(win.Hdc, win.Hglrc);
            _activeWindow = win;
        }
    }

    /// <summary>Swaps the front and back buffers of the window.</summary>
    public static void SwapBuffers(int windowId)
    {
        if (_windows.TryGetValue(windowId, out var win))
        {
            WGL.SwapBuffers(win.Hdc);
        }
    }

    /// <summary>Pumps OS window messages. Call once per frame in your render loop.</summary>
    public static void PollEvents()
    {
        while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    /// <summary>Returns true if the user requested the window to close (e.g. clicked X or Alt+F4).</summary>
    public static bool WindowShouldClose(int windowId)
    {
        return !_windows.TryGetValue(windowId, out var win) || win.ShouldClose;
    }

    /// <summary>Destroys the window and releases its OpenGL context.</summary>
    public static void CloseWindow(int windowId)
    {
        if (_windows.TryRemove(windowId, out var win))
        {
            _hwndMap.TryRemove(win.Hwnd, out _);
            if (ReferenceEquals(_activeWindow, win))
            {
                _activeWindow = null;
            }

            WGL.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            WGL.wglDeleteContext(win.Hglrc);
            ReleaseDC(win.Hwnd, win.Hdc);
            DestroyWindow(win.Hwnd);
        }
    }

    /// <summary>Returns the current client width of the window in pixels.</summary>
    public static int GetWindowWidth(int windowId)
        => _windows.TryGetValue(windowId, out var win) ? win.Width : 0;

    /// <summary>Returns the current client height of the window in pixels.</summary>
    public static int GetWindowHeight(int windowId)
        => _windows.TryGetValue(windowId, out var win) ? win.Height : 0;

    /// <summary>Returns true if the key corresponding to the given virtual key code is currently held down.</summary>
    public static bool IsKeyDown(int keyCode)
    {
        if (_activeWindow == null || keyCode < 0 || keyCode >= 256) return false;
        return _activeWindow.Keys[keyCode];
    }

    /// <summary>Returns the mouse cursor X position relative to the active window's client area.</summary>
    public static int GetMouseX() => _activeWindow?.MouseX ?? 0;

    /// <summary>Returns the mouse cursor Y position relative to the active window's client area.</summary>
    public static int GetMouseY() => _activeWindow?.MouseY ?? 0;

    /// <summary>Returns true if the specified mouse button (0=Left, 1=Right, 2=Middle) is currently held down.</summary>
    public static bool IsMouseButtonDown(int button)
    {
        if (_activeWindow == null || button < 0 || button >= 8) return false;
        return _activeWindow.MouseButtons[button];
    }
}
