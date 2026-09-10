using System.Runtime.InteropServices;

namespace GLHelper;

public static partial class GL
{
    private const string GlDll = "opengl32.dll";

    [LibraryImport(GlDll, EntryPoint = "glClearColor")]
    public static partial void ClearColor(float red, float green, float blue, float alpha);

    public static void ClearColorD(double red, double green, double blue, double alpha)
        => ClearColor((float)red, (float)green, (float)blue, (float)alpha);

    [LibraryImport(GlDll, EntryPoint = "glClear")]
    public static partial void Clear(int mask);

    [LibraryImport(GlDll, EntryPoint = "glViewport")]
    public static partial void Viewport(int x, int y, int width, int height);

    [LibraryImport(GlDll, EntryPoint = "glEnable")]
    public static partial void Enable(int cap);

    [LibraryImport(GlDll, EntryPoint = "glDisable")]
    public static partial void Disable(int cap);

    [LibraryImport(GlDll, EntryPoint = "glIsEnabled")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsEnabled(int cap);

    [LibraryImport(GlDll, EntryPoint = "glDepthFunc")]
    public static partial void DepthFunc(int func);

    [LibraryImport(GlDll, EntryPoint = "glDepthMask")]
    public static partial void DepthMask([MarshalAs(UnmanagedType.Bool)] bool flag);

    [LibraryImport(GlDll, EntryPoint = "glCullFace")]
    public static partial void CullFace(int mode);

    [LibraryImport(GlDll, EntryPoint = "glFrontFace")]
    public static partial void FrontFace(int mode);

    [LibraryImport(GlDll, EntryPoint = "glBlendFunc")]
    public static partial void BlendFunc(int sfactor, int dfactor);

    [LibraryImport(GlDll, EntryPoint = "glLineWidth")]
    public static partial void LineWidth(float width);

    [LibraryImport(GlDll, EntryPoint = "glPointSize")]
    public static partial void PointSize(float size);

    [LibraryImport(GlDll, EntryPoint = "glPolygonMode")]
    public static partial void PolygonMode(int face, int mode);

    [LibraryImport(GlDll, EntryPoint = "glScissor")]
    public static partial void Scissor(int x, int y, int width, int height);

    // ── Matrix Stack & Transformations ──────────────────────────────────────────

    [LibraryImport(GlDll, EntryPoint = "glMatrixMode")]
    public static partial void MatrixMode(int mode);

    [LibraryImport(GlDll, EntryPoint = "glLoadIdentity")]
    public static partial void LoadIdentity();

    [LibraryImport(GlDll, EntryPoint = "glPushMatrix")]
    public static partial void PushMatrix();

    [LibraryImport(GlDll, EntryPoint = "glPopMatrix")]
    public static partial void PopMatrix();

    [LibraryImport(GlDll, EntryPoint = "glRotatef")]
    public static partial void Rotatef(float angle, float x, float y, float z);

    [LibraryImport(GlDll, EntryPoint = "glRotated")]
    public static partial void Rotated(double angle, double x, double y, double z);

    [LibraryImport(GlDll, EntryPoint = "glTranslatef")]
    public static partial void Translatef(float x, float y, float z);

    [LibraryImport(GlDll, EntryPoint = "glTranslated")]
    public static partial void Translated(double x, double y, double z);

    [LibraryImport(GlDll, EntryPoint = "glScalef")]
    public static partial void Scalef(float x, float y, float z);

    [LibraryImport(GlDll, EntryPoint = "glScaled")]
    public static partial void Scaled(double x, double y, double z);

    [LibraryImport(GlDll, EntryPoint = "glOrtho")]
    public static partial void Ortho(double left, double right, double bottom, double top, double zNear, double zFar);

    [LibraryImport(GlDll, EntryPoint = "glFrustum")]
    public static partial void Frustum(double left, double right, double bottom, double top, double zNear, double zFar);

    [LibraryImport(GlDll, EntryPoint = "glLoadMatrixf")]
    public static unsafe partial void LoadMatrixf(float* m);

    public static unsafe void LoadMatrix(float[] m)
    {
        if (m.Length < 16) return;
        fixed (float* p = m) { LoadMatrixf(p); }
    }

    [LibraryImport(GlDll, EntryPoint = "glMultMatrixf")]
    public static unsafe partial void MultMatrixf(float* m);

    public static unsafe void MultMatrix(float[] m)
    {
        if (m.Length < 16) return;
        fixed (float* p = m) { MultMatrixf(p); }
    }

    // ── Immediate Mode ──────────────────────────────────────────────────────────

    [LibraryImport(GlDll, EntryPoint = "glBegin")]
    public static partial void Begin(int mode);

    [LibraryImport(GlDll, EntryPoint = "glEnd")]
    public static partial void End();

    [LibraryImport(GlDll, EntryPoint = "glVertex2f")]
    public static partial void Vertex2f(float x, float y);

    [LibraryImport(GlDll, EntryPoint = "glVertex2d")]
    public static partial void Vertex2d(double x, double y);

    [LibraryImport(GlDll, EntryPoint = "glVertex2i")]
    public static partial void Vertex2i(int x, int y);

    [LibraryImport(GlDll, EntryPoint = "glVertex3f")]
    public static partial void Vertex3f(float x, float y, float z);

    [LibraryImport(GlDll, EntryPoint = "glVertex3d")]
    public static partial void Vertex3d(double x, double y, double z);

    [LibraryImport(GlDll, EntryPoint = "glColor3f")]
    public static partial void Color3f(float red, float green, float blue);

    [LibraryImport(GlDll, EntryPoint = "glColor3d")]
    public static partial void Color3d(double red, double green, double blue);

    [LibraryImport(GlDll, EntryPoint = "glColor4f")]
    public static partial void Color4f(float red, float green, float blue, float alpha);

    [LibraryImport(GlDll, EntryPoint = "glColor4d")]
    public static partial void Color4d(double red, double green, double blue, double alpha);

    [LibraryImport(GlDll, EntryPoint = "glColor3ub")]
    public static partial void Color3ub(byte red, byte green, byte blue);

    public static void Color3i(int red, int green, int blue)
        => Color3ub((byte)red, (byte)green, (byte)blue);

    [LibraryImport(GlDll, EntryPoint = "glColor4ub")]
    public static partial void Color4ub(byte red, byte green, byte blue, byte alpha);

    public static void Color4i(int red, int green, int blue, int alpha)
        => Color4ub((byte)red, (byte)green, (byte)blue, (byte)alpha);

    [LibraryImport(GlDll, EntryPoint = "glNormal3f")]
    public static partial void Normal3f(float nx, float ny, float nz);

    [LibraryImport(GlDll, EntryPoint = "glNormal3d")]
    public static partial void Normal3d(double nx, double ny, double nz);

    [LibraryImport(GlDll, EntryPoint = "glTexCoord2f")]
    public static partial void TexCoord2f(float s, float t);

    [LibraryImport(GlDll, EntryPoint = "glTexCoord2d")]
    public static partial void TexCoord2d(double s, double t);

    // ── Drawing Arrays ──────────────────────────────────────────────────────────

    [LibraryImport(GlDll, EntryPoint = "glDrawArrays")]
    public static partial void DrawArrays(int mode, int first, int count);

    [LibraryImport(GlDll, EntryPoint = "glDrawElements")]
    public static partial void DrawElements(int mode, int count, int type, IntPtr indices);

    public static void DrawElementsOffset(int mode, int count, int type, int offset)
        => DrawElements(mode, count, type, (IntPtr)offset);

    // ── Textures ────────────────────────────────────────────────────────────────

    [LibraryImport(GlDll, EntryPoint = "glGenTextures")]
    private static unsafe partial void glGenTextures(int n, int* textures);

    public static unsafe int GenTexture()
    {
        int tex = 0;
        glGenTextures(1, &tex);
        return tex;
    }

    [LibraryImport(GlDll, EntryPoint = "glBindTexture")]
    public static partial void BindTexture(int target, int texture);

    [LibraryImport(GlDll, EntryPoint = "glDeleteTextures")]
    private static unsafe partial void glDeleteTextures(int n, int* textures);

    public static unsafe void DeleteTexture(int texture)
    {
        int tex = texture;
        glDeleteTextures(1, &tex);
    }

    [LibraryImport(GlDll, EntryPoint = "glTexParameteri")]
    public static partial void TexParameteri(int target, int pname, int param);

    [LibraryImport(GlDll, EntryPoint = "glTexParameterf")]
    public static partial void TexParameterf(int target, int pname, float param);

    [LibraryImport(GlDll, EntryPoint = "glTexImage2D")]
    public static partial void TexImage2D(
        int target, int level, int internalformat,
        int width, int height, int border,
        int format, int type, IntPtr pixels);

    public static unsafe void TexImage2DInts(
        int target, int level, int internalformat,
        int width, int height, int border,
        int format, int type, int[] pixels)
    {
        fixed (int* p = pixels)
        {
            TexImage2D(target, level, internalformat, width, height, border, format, type, (IntPtr)p);
        }
    }

    // ── State Queries ───────────────────────────────────────────────────────────

    [LibraryImport(GlDll, EntryPoint = "glGetError")]
    public static partial int GetError();

    [LibraryImport(GlDll, EntryPoint = "glGetString")]
    private static partial IntPtr glGetString(int name);

    public static string GetString(int name)
    {
        var ptr = glGetString(name);
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
    }
}
