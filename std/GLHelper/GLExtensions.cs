using System.Runtime.InteropServices;
using System.Text;

namespace GLHelper;

public static partial class GL
{
    // Delegate signatures
    private delegate void SwapIntervalEXTDelegate(int interval);
    private delegate void GenBuffersDelegate(int n, [Out] int[] buffers);
    private delegate void BindBufferDelegate(int target, int buffer);
    private delegate void BufferDataDelegate(int target, IntPtr size, IntPtr data, int usage);
    private delegate void DeleteBuffersDelegate(int n, [In] int[] buffers);
    private delegate void GenVertexArraysDelegate(int n, [Out] int[] arrays);
    private delegate void BindVertexArrayDelegate(int array);
    private delegate void DeleteVertexArraysDelegate(int n, [In] int[] arrays);
    private delegate void EnableVertexAttribArrayDelegate(int index);
    private delegate void DisableVertexAttribArrayDelegate(int index);
    private delegate void VertexAttribPointerDelegate(int index, int size, int type, [MarshalAs(UnmanagedType.Bool)] bool normalized, int stride, IntPtr pointer);
    private delegate int CreateShaderDelegate(int type);
    private delegate void ShaderSourceDelegate(int shader, int count, string[] @string, int[]? length);
    private delegate void CompileShaderDelegate(int shader);
    private delegate void GetShaderivDelegate(int shader, int pname, [Out] int[] @params);
    private delegate void GetShaderInfoLogDelegate(int shader, int maxLength, out int length, [Out] StringBuilder infoLog);
    private delegate void DeleteShaderDelegate(int shader);
    private delegate int CreateProgramDelegate();
    private delegate void AttachShaderDelegate(int program, int shader);
    private delegate void DetachShaderDelegate(int program, int shader);
    private delegate void LinkProgramDelegate(int program);
    private delegate void UseProgramDelegate(int program);
    private delegate void GetProgramivDelegate(int program, int pname, [Out] int[] @params);
    private delegate void GetProgramInfoLogDelegate(int program, int maxLength, out int length, [Out] StringBuilder infoLog);
    private delegate void DeleteProgramDelegate(int program);
    private delegate int GetUniformLocationDelegate(int program, string name);
    private delegate int GetAttribLocationDelegate(int program, string name);
    private delegate void Uniform1iDelegate(int location, int v0);
    private delegate void Uniform1fDelegate(int location, float v0);
    private delegate void Uniform2fDelegate(int location, float v0, float v1);
    private delegate void Uniform3fDelegate(int location, float v0, float v1, float v2);
    private delegate void Uniform4fDelegate(int location, float v0, float v1, float v2, float v3);
    private delegate void UniformMatrix4fvDelegate(int location, int count, [MarshalAs(UnmanagedType.Bool)] bool transpose, [In] float[] value);
    private delegate void ActiveTextureDelegate(int texture);
    private delegate void GenerateMipmapDelegate(int target);

    // Delegate instances
    private static SwapIntervalEXTDelegate? _wglSwapIntervalEXT;
    private static GenBuffersDelegate? _glGenBuffers;
    private static BindBufferDelegate? _glBindBuffer;
    private static BufferDataDelegate? _glBufferData;
    private static DeleteBuffersDelegate? _glDeleteBuffers;
    private static GenVertexArraysDelegate? _glGenVertexArrays;
    private static BindVertexArrayDelegate? _glBindVertexArray;
    private static DeleteVertexArraysDelegate? _glDeleteVertexArrays;
    private static EnableVertexAttribArrayDelegate? _glEnableVertexAttribArray;
    private static DisableVertexAttribArrayDelegate? _glDisableVertexAttribArray;
    private static VertexAttribPointerDelegate? _glVertexAttribPointer;
    private static CreateShaderDelegate? _glCreateShader;
    private static ShaderSourceDelegate? _glShaderSource;
    private static CompileShaderDelegate? _glCompileShader;
    private static GetShaderivDelegate? _glGetShaderiv;
    private static GetShaderInfoLogDelegate? _glGetShaderInfoLog;
    private static DeleteShaderDelegate? _glDeleteShader;
    private static CreateProgramDelegate? _glCreateProgram;
    private static AttachShaderDelegate? _glAttachShader;
    private static DetachShaderDelegate? _glDetachShader;
    private static LinkProgramDelegate? _glLinkProgram;
    private static UseProgramDelegate? _glUseProgram;
    private static GetProgramivDelegate? _glGetProgramiv;
    private static GetProgramInfoLogDelegate? _glGetProgramInfoLog;
    private static DeleteProgramDelegate? _glDeleteProgram;
    private static GetUniformLocationDelegate? _glGetUniformLocation;
    private static GetAttribLocationDelegate? _glGetAttribLocation;
    private static Uniform1iDelegate? _glUniform1i;
    private static Uniform1fDelegate? _glUniform1f;
    private static Uniform2fDelegate? _glUniform2f;
    private static Uniform3fDelegate? _glUniform3f;
    private static Uniform4fDelegate? _glUniform4f;
    private static UniformMatrix4fvDelegate? _glUniformMatrix4fv;
    private static ActiveTextureDelegate? _glActiveTexture;
    private static GenerateMipmapDelegate? _glGenerateMipmap;

    private static bool _extensionsLoaded;

    internal static void LoadExtensions()
    {
        if (_extensionsLoaded) return;
        _extensionsLoaded = true;

        _wglSwapIntervalEXT = LoadProc<SwapIntervalEXTDelegate>("wglSwapIntervalEXT");
        _glGenBuffers = LoadProc<GenBuffersDelegate>("glGenBuffers");
        _glBindBuffer = LoadProc<BindBufferDelegate>("glBindBuffer");
        _glBufferData = LoadProc<BufferDataDelegate>("glBufferData");
        _glDeleteBuffers = LoadProc<DeleteBuffersDelegate>("glDeleteBuffers");
        _glGenVertexArrays = LoadProc<GenVertexArraysDelegate>("glGenVertexArrays");
        _glBindVertexArray = LoadProc<BindVertexArrayDelegate>("glBindVertexArray");
        _glDeleteVertexArrays = LoadProc<DeleteVertexArraysDelegate>("glDeleteVertexArrays");
        _glEnableVertexAttribArray = LoadProc<EnableVertexAttribArrayDelegate>("glEnableVertexAttribArray");
        _glDisableVertexAttribArray = LoadProc<DisableVertexAttribArrayDelegate>("glDisableVertexAttribArray");
        _glVertexAttribPointer = LoadProc<VertexAttribPointerDelegate>("glVertexAttribPointer");
        _glCreateShader = LoadProc<CreateShaderDelegate>("glCreateShader");
        _glShaderSource = LoadProc<ShaderSourceDelegate>("glShaderSource");
        _glCompileShader = LoadProc<CompileShaderDelegate>("glCompileShader");
        _glGetShaderiv = LoadProc<GetShaderivDelegate>("glGetShaderiv");
        _glGetShaderInfoLog = LoadProc<GetShaderInfoLogDelegate>("glGetShaderInfoLog");
        _glDeleteShader = LoadProc<DeleteShaderDelegate>("glDeleteShader");
        _glCreateProgram = LoadProc<CreateProgramDelegate>("glCreateProgram");
        _glAttachShader = LoadProc<AttachShaderDelegate>("glAttachShader");
        _glDetachShader = LoadProc<DetachShaderDelegate>("glDetachShader");
        _glLinkProgram = LoadProc<LinkProgramDelegate>("glLinkProgram");
        _glUseProgram = LoadProc<UseProgramDelegate>("glUseProgram");
        _glGetProgramiv = LoadProc<GetProgramivDelegate>("glGetProgramiv");
        _glGetProgramInfoLog = LoadProc<GetProgramInfoLogDelegate>("glGetProgramInfoLog");
        _glDeleteProgram = LoadProc<DeleteProgramDelegate>("glDeleteProgram");
        _glGetUniformLocation = LoadProc<GetUniformLocationDelegate>("glGetUniformLocation");
        _glGetAttribLocation = LoadProc<GetAttribLocationDelegate>("glGetAttribLocation");
        _glUniform1i = LoadProc<Uniform1iDelegate>("glUniform1i");
        _glUniform1f = LoadProc<Uniform1fDelegate>("glUniform1f");
        _glUniform2f = LoadProc<Uniform2fDelegate>("glUniform2f");
        _glUniform3f = LoadProc<Uniform3fDelegate>("glUniform3f");
        _glUniform4f = LoadProc<Uniform4fDelegate>("glUniform4f");
        _glUniformMatrix4fv = LoadProc<UniformMatrix4fvDelegate>("glUniformMatrix4fv");
        _glActiveTexture = LoadProc<ActiveTextureDelegate>("glActiveTexture");
        _glGenerateMipmap = LoadProc<GenerateMipmapDelegate>("glGenerateMipmap");
    }

    private static T? LoadProc<T>(string name) where T : Delegate
    {
        var proc = WGL.GetAnyProcAddress(name);
        return proc == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(proc);
    }

    // ── VSync ───────────────────────────────────────────────────────────────────

    public static void SwapInterval(int interval)
        => _wglSwapIntervalEXT?.Invoke(interval);

    // ── Buffer Objects ──────────────────────────────────────────────────────────

    public static int GenBuffer()
    {
        var bufs = new int[1];
        _glGenBuffers?.Invoke(1, bufs);
        return bufs[0];
    }

    public static void BindBuffer(int target, int buffer)
        => _glBindBuffer?.Invoke(target, buffer);

    public static unsafe void BufferData(int target, int byteSize, float[] data, int usage)
    {
        if (_glBufferData == null) return;
        fixed (float* p = data)
        {
            _glBufferData(target, (IntPtr)byteSize, (IntPtr)p, usage);
        }
    }

    public static unsafe void BufferDataInts(int target, int byteSize, int[] data, int usage)
    {
        if (_glBufferData == null) return;
        fixed (int* p = data)
        {
            _glBufferData(target, (IntPtr)byteSize, (IntPtr)p, usage);
        }
    }

    public static void DeleteBuffer(int buffer)
        => _glDeleteBuffers?.Invoke(1, [buffer]);

    // ── Vertex Array Objects ────────────────────────────────────────────────────

    public static int GenVertexArray()
    {
        var vaos = new int[1];
        _glGenVertexArrays?.Invoke(1, vaos);
        return vaos[0];
    }

    public static void BindVertexArray(int vao)
        => _glBindVertexArray?.Invoke(vao);

    public static void DeleteVertexArray(int vao)
        => _glDeleteVertexArrays?.Invoke(1, [vao]);

    public static void EnableVertexAttribArray(int index)
        => _glEnableVertexAttribArray?.Invoke(index);

    public static void DisableVertexAttribArray(int index)
        => _glDisableVertexAttribArray?.Invoke(index);

    public static void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset)
        => _glVertexAttribPointer?.Invoke(index, size, type, normalized, stride, (IntPtr)offset);

    // ── Shaders ─────────────────────────────────────────────────────────────────

    public static int CreateShader(int type)
        => _glCreateShader?.Invoke(type) ?? 0;

    public static void ShaderSource(int shader, string source)
        => _glShaderSource?.Invoke(shader, 1, [source], [source.Length]);

    public static void CompileShader(int shader)
        => _glCompileShader?.Invoke(shader);

    public static int GetShaderiv(int shader, int pname)
    {
        var res = new int[1];
        _glGetShaderiv?.Invoke(shader, pname, res);
        return res[0];
    }

    public static string GetShaderInfoLog(int shader)
    {
        if (_glGetShaderInfoLog == null) return string.Empty;
        var sb = new StringBuilder(2048);
        _glGetShaderInfoLog(shader, 2048, out _, sb);
        return sb.ToString();
    }

    public static void DeleteShader(int shader)
        => _glDeleteShader?.Invoke(shader);

    // ── Programs ────────────────────────────────────────────────────────────────

    public static int CreateProgram()
        => _glCreateProgram?.Invoke() ?? 0;

    public static void AttachShader(int program, int shader)
        => _glAttachShader?.Invoke(program, shader);

    public static void DetachShader(int program, int shader)
        => _glDetachShader?.Invoke(program, shader);

    public static void LinkProgram(int program)
        => _glLinkProgram?.Invoke(program);

    public static void UseProgram(int program)
        => _glUseProgram?.Invoke(program);

    public static int GetProgramiv(int program, int pname)
    {
        var res = new int[1];
        _glGetProgramiv?.Invoke(program, pname, res);
        return res[0];
    }

    public static string GetProgramInfoLog(int program)
    {
        if (_glGetProgramInfoLog == null) return string.Empty;
        var sb = new StringBuilder(2048);
        _glGetProgramInfoLog(program, 2048, out _, sb);
        return sb.ToString();
    }

    public static void DeleteProgram(int program)
        => _glDeleteProgram?.Invoke(program);

    // ── Uniforms ────────────────────────────────────────────────────────────────

    public static int GetUniformLocation(int program, string name)
        => _glGetUniformLocation?.Invoke(program, name) ?? -1;

    public static int GetAttribLocation(int program, string name)
        => _glGetAttribLocation?.Invoke(program, name) ?? -1;

    public static void Uniform1i(int location, int v0)
        => _glUniform1i?.Invoke(location, v0);

    public static void Uniform1f(int location, float v0)
        => _glUniform1f?.Invoke(location, v0);

    public static void Uniform2f(int location, float v0, float v1)
        => _glUniform2f?.Invoke(location, v0, v1);

    public static void Uniform3f(int location, float v0, float v1, float v2)
        => _glUniform3f?.Invoke(location, v0, v1, v2);

    public static void Uniform4f(int location, float v0, float v1, float v2, float v3)
        => _glUniform4f?.Invoke(location, v0, v1, v2, v3);

    public static void UniformMatrix4fv(int location, int count, bool transpose, float[] value)
        => _glUniformMatrix4fv?.Invoke(location, count, transpose, value);

    public static void ActiveTexture(int texture)
        => _glActiveTexture?.Invoke(texture);

    public static void GenerateMipmap(int target)
        => _glGenerateMipmap?.Invoke(target);
}
