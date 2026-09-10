using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace GLHelper;

internal struct GlyphInfo
{
    public float U0;
    public float V0;
    public float U1;
    public float V1;
    public int Width;
    public int Height;
    public int Advance;
}

internal sealed class NativeGLFont
{
    public int Id;
    public int TextureId;
    public int LineHeight;
    public int Ascent;
    public readonly int[] Widths = new int[128];
    public readonly GlyphInfo[] Glyphs = new GlyphInfo[128];
}

public static partial class GL
{
    private static readonly ConcurrentDictionary<int, NativeGLFont> _fonts = new();
    private static int _nextFontId = 1;

    // ── Win32 GDI Declarations ───────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TEXTMETRICW
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public ushort tmFirstChar;
        public ushort tmLastChar;
        public ushort tmDefaultChar;
        public ushort tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public byte tmPitchAndFamily;
        public byte tmCharSet;
    }

    [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC", SetLastError = true)]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject", SetLastError = true)]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateFont(
        int nHeight, int nWidth, int nEscapement, int nOrientation,
        int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut,
        uint fdwCharSet, uint fdwOutputPrecision, uint fdwClipPrecision,
        uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextMetricsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextMetrics(IntPtr hdc, out TEXTMETRICW lptm);

    [LibraryImport("gdi32.dll", EntryPoint = "GetCharWidth32W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetCharWidth32(IntPtr hdc, uint iFirstChar, uint iLastChar, int* lpBuffer);

    [LibraryImport("gdi32.dll", EntryPoint = "SetTextColor", SetLastError = true)]
    private static partial uint SetTextColor(IntPtr hdc, uint crColor);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkColor", SetLastError = true)]
    private static partial uint SetBkColor(IntPtr hdc, uint crColor);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkMode", SetLastError = true)]
    private static partial int SetBkMode(IntPtr hdc, int iBkMode);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true)]
    private static partial IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TextOut(IntPtr hdc, int nXStart, int nYStart, string lpString, int cbString);

    // ── Built-in 8x8 font fallback ───────────────────────────────────────────
    private static readonly byte[][] Default8x8Font =
    [
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], // ' '
        [0x18, 0x3c, 0x3c, 0x18, 0x18, 0x00, 0x18, 0x00], // '!'
        [0x66, 0x66, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00], // '"'
        [0x6c, 0x6c, 0xfe, 0x6c, 0xfe, 0x6c, 0x6c, 0x00], // '#'
        [0x18, 0x3e, 0x60, 0x3c, 0x06, 0x7c, 0x18, 0x00], // '$'
        [0x00, 0x63, 0x66, 0x0c, 0x18, 0x33, 0x63, 0x00], // '%'
        [0x1c, 0x36, 0x1c, 0x3b, 0x6e, 0x66, 0x3b, 0x00], // '&'
        [0x06, 0x06, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00], // '\''
        [0x0c, 0x18, 0x30, 0x30, 0x30, 0x18, 0x0c, 0x00], // '('
        [0x30, 0x18, 0x0c, 0x0c, 0x0c, 0x18, 0x30, 0x00], // ')'
        [0x00, 0x66, 0x3c, 0xff, 0x3c, 0x66, 0x00, 0x00], // '*'
        [0x00, 0x18, 0x18, 0x7e, 0x18, 0x18, 0x00, 0x00], // '+'
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x18, 0x18, 0x30], // ','
        [0x00, 0x00, 0x00, 0x7e, 0x00, 0x00, 0x00, 0x00], // '-'
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x18, 0x18, 0x00], // '.'
        [0x03, 0x06, 0x0c, 0x18, 0x30, 0x60, 0x40, 0x00], // '/'
        [0x3c, 0x66, 0x6e, 0x76, 0x66, 0x66, 0x3c, 0x00], // '0'
        [0x18, 0x18, 0x38, 0x18, 0x18, 0x18, 0x7e, 0x00], // '1'
        [0x3c, 0x66, 0x06, 0x0c, 0x18, 0x30, 0x7e, 0x00], // '2'
        [0x3c, 0x66, 0x06, 0x1c, 0x06, 0x66, 0x3c, 0x00], // '3'
        [0x0c, 0x1c, 0x34, 0x64, 0x7e, 0x04, 0x0e, 0x00], // '4'
        [0x7e, 0x60, 0x7c, 0x06, 0x06, 0x66, 0x3c, 0x00], // '5'
        [0x1c, 0x30, 0x60, 0x7c, 0x66, 0x66, 0x3c, 0x00], // '6'
        [0x7e, 0x66, 0x06, 0x0c, 0x18, 0x18, 0x18, 0x00], // '7'
        [0x3c, 0x66, 0x66, 0x3c, 0x66, 0x66, 0x3c, 0x00], // '8'
        [0x3c, 0x66, 0x66, 0x3e, 0x06, 0x0c, 0x38, 0x00], // '9'
        [0x00, 0x18, 0x18, 0x00, 0x18, 0x18, 0x00, 0x00], // ':'
        [0x00, 0x18, 0x18, 0x00, 0x18, 0x18, 0x30, 0x00], // ';'
        [0x0c, 0x18, 0x30, 0x60, 0x30, 0x18, 0x0c, 0x00], // '<'
        [0x00, 0x00, 0x7e, 0x00, 0x7e, 0x00, 0x00, 0x00], // '='
        [0x30, 0x18, 0x0c, 0x06, 0x0c, 0x18, 0x30, 0x00], // '>'
        [0x3c, 0x66, 0x06, 0x0c, 0x18, 0x00, 0x18, 0x00], // '?'
        [0x3c, 0x66, 0x6e, 0x6a, 0x6e, 0x60, 0x3c, 0x00], // '@'
        [0x18, 0x3c, 0x66, 0x66, 0x7e, 0x66, 0x66, 0x00], // 'A'
        [0x7c, 0x66, 0x66, 0x7c, 0x66, 0x66, 0x7c, 0x00], // 'B'
        [0x3c, 0x66, 0x60, 0x60, 0x60, 0x66, 0x3c, 0x00], // 'C'
        [0x78, 0x6c, 0x66, 0x66, 0x66, 0x6c, 0x78, 0x00], // 'D'
        [0x7e, 0x60, 0x60, 0x7c, 0x60, 0x60, 0x7e, 0x00], // 'E'
        [0x7e, 0x60, 0x60, 0x7c, 0x60, 0x60, 0x60, 0x00], // 'F'
        [0x3c, 0x66, 0x60, 0x6e, 0x66, 0x66, 0x3c, 0x00], // 'G'
        [0x66, 0x66, 0x66, 0x7e, 0x66, 0x66, 0x66, 0x00], // 'H'
        [0x3c, 0x18, 0x18, 0x18, 0x18, 0x18, 0x3c, 0x00], // 'I'
        [0x0e, 0x06, 0x06, 0x06, 0x06, 0x66, 0x3c, 0x00], // 'J'
        [0x66, 0x6c, 0x78, 0x70, 0x78, 0x6c, 0x66, 0x00], // 'K'
        [0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x7e, 0x00], // 'L'
        [0x63, 0x77, 0x7f, 0x6b, 0x63, 0x63, 0x63, 0x00], // 'M'
        [0x66, 0x76, 0x7e, 0x7e, 0x6e, 0x66, 0x66, 0x00], // 'N'
        [0x3c, 0x66, 0x66, 0x66, 0x66, 0x66, 0x3c, 0x00], // 'O'
        [0x7c, 0x66, 0x66, 0x7c, 0x60, 0x60, 0x60, 0x00], // 'P'
        [0x3c, 0x66, 0x66, 0x66, 0x6a, 0x6c, 0x36, 0x00], // 'Q'
        [0x7c, 0x66, 0x66, 0x7c, 0x6c, 0x66, 0x66, 0x00], // 'R'
        [0x3c, 0x66, 0x60, 0x3c, 0x06, 0x66, 0x3c, 0x00], // 'S'
        [0x7e, 0x18, 0x18, 0x18, 0x18, 0x18, 0x18, 0x00], // 'T'
        [0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0x3c, 0x00], // 'U'
        [0x66, 0x66, 0x66, 0x66, 0x66, 0x3c, 0x18, 0x00], // 'V'
        [0x63, 0x63, 0x63, 0x6b, 0x7f, 0x77, 0x63, 0x00], // 'W'
        [0x66, 0x66, 0x3c, 0x18, 0x3c, 0x66, 0x66, 0x00], // 'X'
        [0x66, 0x66, 0x66, 0x3c, 0x18, 0x18, 0x18, 0x00], // 'Y'
        [0x7e, 0x06, 0x0c, 0x18, 0x30, 0x60, 0x7e, 0x00], // 'Z'
        [0x3c, 0x30, 0x30, 0x30, 0x30, 0x30, 0x3c, 0x00], // '['
        [0x40, 0x60, 0x30, 0x18, 0x0c, 0x06, 0x02, 0x00], // '\\'
        [0x3c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x3c, 0x00], // ']'
        [0x10, 0x38, 0x6c, 0xc6, 0x00, 0x00, 0x00, 0x00], // '^'
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff], // '_'
        [0x30, 0x18, 0x0c, 0x00, 0x00, 0x00, 0x00, 0x00], // '`'
        [0x00, 0x00, 0x3c, 0x06, 0x3e, 0x66, 0x3b, 0x00], // 'a'
        [0x60, 0x60, 0x7c, 0x66, 0x66, 0x66, 0x7c, 0x00], // 'b'
        [0x00, 0x00, 0x3c, 0x66, 0x60, 0x66, 0x3c, 0x00], // 'c'
        [0x06, 0x06, 0x3e, 0x66, 0x66, 0x66, 0x3e, 0x00], // 'd'
        [0x00, 0x00, 0x3c, 0x66, 0x7e, 0x60, 0x3c, 0x00], // 'e'
        [0x1c, 0x30, 0x30, 0x78, 0x30, 0x30, 0x30, 0x00], // 'f'
        [0x00, 0x00, 0x3b, 0x66, 0x66, 0x3e, 0x06, 0x3c], // 'g'
        [0x60, 0x60, 0x7c, 0x66, 0x66, 0x66, 0x66, 0x00], // 'h'
        [0x18, 0x00, 0x38, 0x18, 0x18, 0x18, 0x3c, 0x00], // 'i'
        [0x06, 0x00, 0x06, 0x06, 0x06, 0x06, 0x66, 0x3c], // 'j'
        [0x60, 0x60, 0x66, 0x6c, 0x78, 0x6c, 0x66, 0x00], // 'k'
        [0x38, 0x18, 0x18, 0x18, 0x18, 0x18, 0x3c, 0x00], // 'l'
        [0x00, 0x00, 0x66, 0x7f, 0x7f, 0x6b, 0x63, 0x00], // 'm'
        [0x00, 0x00, 0x7c, 0x66, 0x66, 0x66, 0x66, 0x00], // 'n'
        [0x00, 0x00, 0x3c, 0x66, 0x66, 0x66, 0x3c, 0x00], // 'o'
        [0x00, 0x00, 0x7c, 0x66, 0x66, 0x7c, 0x60, 0x60], // 'p'
        [0x00, 0x00, 0x3e, 0x66, 0x66, 0x3e, 0x06, 0x06], // 'q'
        [0x00, 0x00, 0x7c, 0x66, 0x60, 0x60, 0x60, 0x00], // 'r'
        [0x00, 0x00, 0x3e, 0x60, 0x3c, 0x06, 0x7c, 0x00], // 's'
        [0x18, 0x18, 0x7e, 0x18, 0x18, 0x18, 0x0e, 0x00], // 't'
        [0x00, 0x00, 0x66, 0x66, 0x66, 0x66, 0x3b, 0x00], // 'u'
        [0x00, 0x00, 0x66, 0x66, 0x66, 0x3c, 0x18, 0x00], // 'v'
        [0x00, 0x00, 0x63, 0x6b, 0x7f, 0x3e, 0x36, 0x00], // 'w'
        [0x00, 0x00, 0x66, 0x3c, 0x18, 0x3c, 0x66, 0x00], // 'x'
        [0x00, 0x00, 0x66, 0x66, 0x66, 0x3e, 0x06, 0x3c], // 'y'
        [0x00, 0x00, 0x7e, 0x0c, 0x18, 0x30, 0x7e, 0x00], // 'z'
        [0x0e, 0x18, 0x18, 0x70, 0x18, 0x18, 0x0e, 0x00], // '{'
        [0x18, 0x18, 0x18, 0x00, 0x18, 0x18, 0x18, 0x00], // '|'
        [0x70, 0x18, 0x18, 0x0e, 0x18, 0x18, 0x70, 0x00], // '}'
        [0x76, 0xdc, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]  // '~'
    ];

    private static NativeGLFont CreateFallbackFont()
    {
        const int atlasW = 128;
        const int atlasH = 64;
        byte[] pixels = new byte[atlasW * atlasH * 4];

        var font = new NativeGLFont
        {
            LineHeight = 8,
            Ascent = 7
        };

        for (int i = 0; i < 128; i++)
        {
            font.Widths[i] = (i >= 32 && i <= 126) ? 8 : 0;
        }

        // Pack into 16 columns x 6 rows of 8x8 glyphs
        for (int idx = 0; idx < Default8x8Font.Length; idx++)
        {
            int ascii = idx + 32;
            int col = idx % 16;
            int row = idx / 16;
            int gx = col * 8;
            int gy = row * 8;

            font.Glyphs[ascii] = new GlyphInfo
            {
                U0 = (float)gx / atlasW,
                V0 = (float)gy / atlasH,
                U1 = (float)(gx + 8) / atlasW,
                V1 = (float)(gy + 8) / atlasH,
                Width = 8,
                Height = 8,
                Advance = 8
            };

            var glyphData = Default8x8Font[idx];
            for (int r = 0; r < 8; r++)
            {
                byte bits = glyphData[r];
                for (int c = 0; c < 8; c++)
                {
                    bool set = (bits & (0x80 >> c)) != 0;
                    int px = gx + c;
                    int py = gy + r;
                    int offset = (py * atlasW + px) * 4;
                    pixels[offset] = 255;     // R
                    pixels[offset + 1] = 255; // G
                    pixels[offset + 2] = 255; // B
                    pixels[offset + 3] = set ? (byte)255 : (byte)0; // A
                }
            }
        }

        font.TextureId = UploadAtlas(atlasW, atlasH, pixels);
        return font;
    }

    private static unsafe int UploadAtlas(int width, int height, byte[] pixels)
    {
        int tex = GenTexture();
        BindTexture(TEXTURE_2D, tex);
        TexParameteri(TEXTURE_2D, TEXTURE_MIN_FILTER, LINEAR);
        TexParameteri(TEXTURE_2D, TEXTURE_MAG_FILTER, LINEAR);
        TexParameteri(TEXTURE_2D, TEXTURE_WRAP_S, CLAMP_TO_EDGE);
        TexParameteri(TEXTURE_2D, TEXTURE_WRAP_T, CLAMP_TO_EDGE);

        fixed (byte* p = pixels)
        {
            TexImage2D(TEXTURE_2D, 0, RGBA, width, height, 0, RGBA, UNSIGNED_BYTE, (IntPtr)p);
        }

        BindTexture(TEXTURE_2D, 0);
        return tex;
    }

    /// <summary>
    /// Builds a font texture atlas for the given font family and size.
    /// Returns a font handle integer.
    /// </summary>
    public static unsafe int BuildFont(string family, int size, bool bold)
    {
        if (size <= 0) size = 10;
        if (string.IsNullOrEmpty(family)) family = "Segoe UI";

        // Try Win32 GDI font rendering first
        try
        {
            IntPtr memDC = CreateCompatibleDC(IntPtr.Zero);
            if (memDC != IntPtr.Zero)
            {
                // DPI scale: GDI font heights are specified in negative pixels (-size)
                int fontHeight = -Math.Max(8, (int)Math.Round(size * 1.333f)); // convert pt to px
                int weight = bold ? 700 : 400;

                IntPtr hFont = CreateFont(
                    fontHeight, 0, 0, 0, weight, 0, 0, 0,
                    1, // DEFAULT_CHARSET
                    0, 0, 5, // CLEARTYPE_QUALITY
                    0, family);

                if (hFont != IntPtr.Zero)
                {
                    IntPtr oldFont = SelectObject(memDC, hFont);

                    if (GetTextMetrics(memDC, out TEXTMETRICW tm))
                    {
                        int[] widths = new int[128];
                        fixed (int* pWidths = widths)
                        {
                            GetCharWidth32(memDC, 0, 127, pWidths);
                        }

                        // Determine atlas dimensions
                        const int atlasW = 512;
                        const int atlasH = 512;
                        int cellH = tm.tmHeight + 4;

                        var bi = new BITMAPINFO
                        {
                            bmiHeader = new BITMAPINFOHEADER
                            {
                                biSize = (uint)sizeof(BITMAPINFOHEADER),
                                biWidth = atlasW,
                                biHeight = -atlasH, // Top-down
                                biPlanes = 1,
                                biBitCount = 32,
                                biCompression = 0
                            }
                        };

                        IntPtr hBitmap = CreateDIBSection(memDC, ref bi, 0, out IntPtr pBits, IntPtr.Zero, 0);
                        if (hBitmap != IntPtr.Zero && pBits != IntPtr.Zero)
                        {
                            IntPtr oldBitmap = SelectObject(memDC, hBitmap);

                            SetBkMode(memDC, 2); // OPAQUE
                            SetBkColor(memDC, 0x00000000); // Black background
                            SetTextColor(memDC, 0x00FFFFFF); // White text

                            var font = new NativeGLFont
                            {
                                LineHeight = tm.tmHeight,
                                Ascent = tm.tmAscent
                            };

                            Array.Copy(widths, font.Widths, 128);

                            int curX = 2;
                            int curY = 2;

                            for (int code = 32; code <= 126; code++)
                            {
                                int w = Math.Max(1, widths[code]);
                                if (curX + w + 4 >= atlasW)
                                {
                                    curX = 2;
                                    curY += cellH;
                                }

                                string charStr = ((char)code).ToString();
                                TextOut(memDC, curX + 1, curY + 1, charStr, 1);

                                font.Glyphs[code] = new GlyphInfo
                                {
                                    U0 = (float)curX / atlasW,
                                    V0 = (float)curY / atlasH,
                                    U1 = (float)(curX + w + 2) / atlasW,
                                    V1 = (float)(curY + tm.tmHeight + 2) / atlasH,
                                    Width = w + 2,
                                    Height = tm.tmHeight + 2,
                                    Advance = w
                                };

                                curX += w + 4;
                            }

                            // Convert DIB RGB text on black into white with alpha channel
                            byte[] rgbaPixels = new byte[atlasW * atlasH * 4];
                            byte* src = (byte*)pBits;

                            for (int p = 0; p < atlasW * atlasH; p++)
                            {
                                int baseOffset = p * 4;
                                byte b = src[baseOffset];
                                byte g = src[baseOffset + 1];
                                byte r = src[baseOffset + 2];

                                // Luma determines alpha
                                byte alpha = (byte)Math.Max(r, Math.Max(g, b));

                                rgbaPixels[baseOffset] = 255;
                                rgbaPixels[baseOffset + 1] = 255;
                                rgbaPixels[baseOffset + 2] = 255;
                                rgbaPixels[baseOffset + 3] = alpha;
                            }

                            font.TextureId = UploadAtlas(atlasW, atlasH, rgbaPixels);

                            // Clean up GDI objects
                            SelectObject(memDC, oldBitmap);
                            DeleteObject(hBitmap);
                            SelectObject(memDC, oldFont);
                            DeleteObject(hFont);
                            DeleteDC(memDC);

                            int fontId = Interlocked.Increment(ref _nextFontId);
                            font.Id = fontId;
                            _fonts[fontId] = font;
                            return fontId;
                        }
                    }

                    SelectObject(memDC, oldFont);
                    DeleteObject(hFont);
                }

                DeleteDC(memDC);
            }
        }
        catch
        {
            // Fall through to fallback font
        }

        // Fallback font
        var fallback = CreateFallbackFont();
        int fbId = Interlocked.Increment(ref _nextFontId);
        fallback.Id = fbId;
        _fonts[fbId] = fallback;
        return fbId;
    }

    /// <summary>
    /// Fills the font's 128 character advances into <paramref name="widths"/> and [lineHeight, ascent] into <paramref name="metrics"/>.
    /// </summary>
    public static void MeasureFont(int fontId, int[] widths, int widthsOffset, int[] metrics, int metricsOffset)
    {
        if (!_fonts.TryGetValue(fontId, out var font)) return;

        if (widths.Length >= widthsOffset + 128)
        {
            Array.Copy(font.Widths, 0, widths, widthsOffset, 128);
        }

        if (metrics.Length >= metricsOffset + 2)
        {
            metrics[metricsOffset] = font.LineHeight;
            metrics[metricsOffset + 1] = font.Ascent;
        }
    }

    /// <summary>
    /// Renders text using the specified font atlas at position (x, y) with the specified ARGB color.
    /// </summary>
    public static void DrawText(int fontId, int x, int y, string text, int argb)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!_fonts.TryGetValue(fontId, out var font)) return;

        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);

        if (a == 0) return;

        Enable(TEXTURE_2D);
        BindTexture(TEXTURE_2D, font.TextureId);
        Color4ub(r, g, b, a);

        Begin(QUADS);

        float curX = x;
        float curY = y;

        for (int i = 0; i < text.Length; i++)
        {
            int code = text[i];
            if (code < 32 || code > 126) code = 63; // '?'

            var gInfo = font.Glyphs[code];

            float x0 = curX;
            float y0 = curY;
            float x1 = curX + gInfo.Width;
            float y1 = curY + gInfo.Height;

            TexCoord2f(gInfo.U0, gInfo.V0); Vertex2f(x0, y0);
            TexCoord2f(gInfo.U1, gInfo.V0); Vertex2f(x1, y0);
            TexCoord2f(gInfo.U1, gInfo.V1); Vertex2f(x1, y1);
            TexCoord2f(gInfo.U0, gInfo.V1); Vertex2f(x0, y1);

            curX += gInfo.Advance;
        }

        End();

        BindTexture(TEXTURE_2D, 0);
        Disable(TEXTURE_2D);
    }

    /// <summary>
    /// Deletes a registered font and releases its OpenGL texture.
    /// </summary>
    public static void DeleteFont(int fontId)
    {
        if (_fonts.TryRemove(fontId, out var font))
        {
            if (font.TextureId != 0)
            {
                DeleteTexture(font.TextureId);
                font.TextureId = 0;
            }
        }
    }
}
