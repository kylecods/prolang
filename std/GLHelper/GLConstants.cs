namespace GLHelper;

public static partial class GL
{
    // ── Buffer Bits ─────────────────────────────────────────────────────────────
    public const int DEPTH_BUFFER_BIT   = 0x00000100;
    public const int STENCIL_BUFFER_BIT = 0x00000400;
    public const int COLOR_BUFFER_BIT   = 0x00004000;

    // ── Primitives ──────────────────────────────────────────────────────────────
    public const int POINTS         = 0x0000;
    public const int LINES          = 0x0001;
    public const int LINE_LOOP      = 0x0002;
    public const int LINE_STRIP     = 0x0003;
    public const int TRIANGLES      = 0x0004;
    public const int TRIANGLE_STRIP = 0x0005;
    public const int TRIANGLE_FAN   = 0x0006;
    public const int QUADS          = 0x0007;
    public const int QUAD_STRIP     = 0x0008;
    public const int POLYGON        = 0x0009;

    // ── Matrix Modes ────────────────────────────────────────────────────────────
    public const int MODELVIEW  = 0x1700;
    public const int PROJECTION = 0x1701;
    public const int TEXTURE    = 0x1702;

    // ── Enable / Disable Capabilities ───────────────────────────────────────────
    public const int POINT_SMOOTH    = 0x0B10;
    public const int LINE_SMOOTH     = 0x0B20;
    public const int CULL_FACE       = 0x0B44;
    public const int LIGHTING        = 0x0B50;
    public const int COLOR_MATERIAL  = 0x0B57;
    public const int FOG             = 0x0B60;
    public const int DEPTH_TEST      = 0x0B71;
    public const int STENCIL_TEST    = 0x0B90;
    public const int NORMALIZE       = 0x0BA1;
    public const int ALPHA_TEST      = 0x0BC0;
    public const int BLEND           = 0x0BE2;
    public const int DITHER          = 0x0BD0;
    public const int SCISSOR_TEST    = 0x0C11;
    public const int TEXTURE_2D      = 0x0DE1;

    // ── Depth / Comparison Functions ────────────────────────────────────────────
    public const int NEVER    = 0x0200;
    public const int LESS     = 0x0201;
    public const int EQUAL    = 0x0202;
    public const int LEQUAL   = 0x0203;
    public const int GREATER  = 0x0204;
    public const int NOTEQUAL = 0x0205;
    public const int GEQUAL   = 0x0206;
    public const int ALWAYS   = 0x0207;

    // ── Culling ─────────────────────────────────────────────────────────────────
    public const int FRONT          = 0x0404;
    public const int BACK           = 0x0405;
    public const int FRONT_AND_BACK = 0x0408;
    public const int CW             = 0x0900;
    public const int CCW            = 0x0901;

    // ── Blending Factors ────────────────────────────────────────────────────────
    public const int ZERO                = 0;
    public const int ONE                 = 1;
    public const int SRC_COLOR           = 0x0300;
    public const int ONE_MINUS_SRC_COLOR = 0x0301;
    public const int SRC_ALPHA           = 0x0302;
    public const int ONE_MINUS_SRC_ALPHA = 0x0303;
    public const int DST_ALPHA           = 0x0304;
    public const int ONE_MINUS_DST_ALPHA = 0x0305;
    public const int DST_COLOR           = 0x0306;
    public const int ONE_MINUS_DST_COLOR = 0x0307;

    // ── Polygon Modes ───────────────────────────────────────────────────────────
    public const int POINT = 0x1B00;
    public const int LINE  = 0x1B01;
    public const int FILL  = 0x1B02;

    // ── Shading ─────────────────────────────────────────────────────────────────
    public const int FLAT   = 0x1D00;
    public const int SMOOTH = 0x1D01;

    // ── Data Types ──────────────────────────────────────────────────────────────
    public const int BYTE           = 0x1400;
    public const int UNSIGNED_BYTE  = 0x1401;
    public const int SHORT          = 0x1402;
    public const int UNSIGNED_SHORT = 0x1403;
    public const int INT            = 0x1404;
    public const int UNSIGNED_INT   = 0x1405;
    public const int FLOAT          = 0x1406;
    public const int DOUBLE         = 0x140A;

    // ── Pixel Formats ───────────────────────────────────────────────────────────
    public const int COLOR_INDEX     = 0x1900;
    public const int RED             = 0x1903;
    public const int GREEN           = 0x1904;
    public const int BLUE            = 0x1905;
    public const int ALPHA           = 0x1906;
    public const int RGB             = 0x1907;
    public const int RGBA            = 0x1908;
    public const int LUMINANCE       = 0x1909;
    public const int LUMINANCE_ALPHA = 0x190A;
    public const int BGR             = 0x80E0;
    public const int BGRA            = 0x80E1;

    // ── Texture Parameters ──────────────────────────────────────────────────────
    public const int TEXTURE_MAG_FILTER = 0x2800;
    public const int TEXTURE_MIN_FILTER = 0x2801;
    public const int TEXTURE_WRAP_S     = 0x2802;
    public const int TEXTURE_WRAP_T     = 0x2803;
    public const int NEAREST            = 0x2600;
    public const int LINEAR             = 0x2601;
    public const int NEAREST_MIPMAP_NEAREST = 0x2700;
    public const int LINEAR_MIPMAP_NEAREST  = 0x2701;
    public const int NEAREST_MIPMAP_LINEAR  = 0x2702;
    public const int LINEAR_MIPMAP_LINEAR   = 0x2703;
    public const int REPEAT             = 0x2901;
    public const int CLAMP_TO_EDGE      = 0x812F;

    // ── Modern GL: Buffer Objects ───────────────────────────────────────────────
    public const int ARRAY_BUFFER         = 0x8892;
    public const int ELEMENT_ARRAY_BUFFER = 0x8893;
    public const int STATIC_DRAW          = 0x88E4;
    public const int DYNAMIC_DRAW         = 0x88E8;
    public const int STREAM_DRAW          = 0x88E0;

    // ── Modern GL: Shaders ──────────────────────────────────────────────────────
    public const int FRAGMENT_SHADER = 0x8B30;
    public const int VERTEX_SHADER   = 0x8B31;
    public const int COMPILE_STATUS  = 0x8B81;
    public const int LINK_STATUS     = 0x8B82;
    public const int INFO_LOG_LENGTH = 0x8B84;

    // ── String Queries ──────────────────────────────────────────────────────────
    public const int VENDOR                   = 0x1F00;
    public const int RENDERER                 = 0x1F01;
    public const int VERSION                  = 0x1F02;
    public const int EXTENSIONS               = 0x1F03;
    public const int SHADING_LANGUAGE_VERSION = 0x8B8C;

    // ── Virtual Key Codes (Standard Windows VK) ─────────────────────────────────
    public const int KEY_BACKSPACE = 0x08;
    public const int KEY_TAB       = 0x09;
    public const int KEY_ENTER     = 0x0D;
    public const int KEY_ESCAPE    = 0x1B;
    public const int KEY_SPACE     = 0x20;
    public const int KEY_PAGE_UP   = 0x21;
    public const int KEY_PAGE_DOWN = 0x22;
    public const int KEY_END       = 0x23;
    public const int KEY_HOME      = 0x24;
    public const int KEY_LEFT      = 0x25;
    public const int KEY_UP        = 0x26;
    public const int KEY_RIGHT     = 0x27;
    public const int KEY_DOWN      = 0x28;
    public const int KEY_INSERT    = 0x2D;
    public const int KEY_DELETE    = 0x2E;

    public const int KEY_0 = 0x30;
    public const int KEY_1 = 0x31;
    public const int KEY_2 = 0x32;
    public const int KEY_3 = 0x33;
    public const int KEY_4 = 0x34;
    public const int KEY_5 = 0x35;
    public const int KEY_6 = 0x36;
    public const int KEY_7 = 0x37;
    public const int KEY_8 = 0x38;
    public const int KEY_9 = 0x39;

    public const int KEY_A = 0x41;
    public const int KEY_B = 0x42;
    public const int KEY_C = 0x43;
    public const int KEY_D = 0x44;
    public const int KEY_E = 0x45;
    public const int KEY_F = 0x46;
    public const int KEY_G = 0x47;
    public const int KEY_H = 0x48;
    public const int KEY_I = 0x49;
    public const int KEY_J = 0x4A;
    public const int KEY_K = 0x4B;
    public const int KEY_L = 0x4C;
    public const int KEY_M = 0x4D;
    public const int KEY_N = 0x4E;
    public const int KEY_O = 0x4F;
    public const int KEY_P = 0x50;
    public const int KEY_Q = 0x51;
    public const int KEY_R = 0x52;
    public const int KEY_S = 0x53;
    public const int KEY_T = 0x54;
    public const int KEY_U = 0x55;
    public const int KEY_V = 0x56;
    public const int KEY_W = 0x57;
    public const int KEY_X = 0x58;
    public const int KEY_Y = 0x59;
    public const int KEY_Z = 0x5A;

    public const int KEY_F1  = 0x70;
    public const int KEY_F2  = 0x71;
    public const int KEY_F3  = 0x72;
    public const int KEY_F4  = 0x73;
    public const int KEY_F5  = 0x74;
    public const int KEY_F6  = 0x75;
    public const int KEY_F7  = 0x76;
    public const int KEY_F8  = 0x77;
    public const int KEY_F9  = 0x78;
    public const int KEY_F10 = 0x79;
    public const int KEY_F11 = 0x7A;
    public const int KEY_F12 = 0x7B;

    public const int KEY_SHIFT   = 0x10;
    public const int KEY_CONTROL = 0x11;
    public const int KEY_ALT     = 0x12;

    public const int MOUSE_LEFT   = 0;
    public const int MOUSE_RIGHT  = 1;
    public const int MOUSE_MIDDLE = 2;
}
