namespace ProLang.Portable;

// Pointer-free value, arithmetic, and MIPS instruction operations shared by both targets.
public static partial class VM
{
    static Val Integer(int n)
    {
        Val v = default;
        v.kind = 1;
        v.data = n;
        return v;
    }

    static int Int(Val v)
    {
        if (v.kind != 1)
        {
            Bad("expected an int32 value");
        }

        return (int)v.data;
    }

    static bool IntType(int type)
    {
        return type >= 2 && type <= 9;
    }

    static bool Truth(Val v)
    {
        return v.kind != 0 && (v.kind != 1 || v.data != 0);
    }

    static int Binary(int code, int a, int b)
    {
        unchecked
        {
            switch (code)
            {
                case 0x58:
                    return a + b;
                case 0x59:
                    return a - b;
                case 0x5a:
                    return a * b;
                case 0x5b:
                    if (b == 0)
                    {
                        Bad("division by zero");
                    }

                    if (a == -2147483647 - 1 && b == -1)
                    {
                        Bad("int32 division overflow");
                    }

                    return a / b;
                case 0x5c:
                    if (b == 0)
                    {
                        Bad("division by zero");
                    }

                    return (int)((uint)a / (uint)b);
                case 0x5d:
                    if (b == 0)
                    {
                        Bad("division by zero");
                    }

                    return b == -1 ? 0 : a % b;
                case 0x5e:
                    if (b == 0)
                    {
                        Bad("division by zero");
                    }

                    return (int)((uint)a % (uint)b);
                case 0x5f:
                    return a & b;
                case 0x60:
                    return a | b;
                case 0x61:
                    return a ^ b;
                case 0x62:
                    return a << (b & 31);
                case 0x63:
                    return a >> (b & 31);
                case 0x64:
                    return (int)((uint)a >> (b & 31));
                case 0xfe02:
                    return a > b ? 1 : 0;
                case 0xfe03:
                    return (uint)a > (uint)b ? 1 : 0;
                case 0xfe04:
                    return a < b ? 1 : 0;
                case 0xfe05:
                    return (uint)a < (uint)b ? 1 : 0;
            }
        }

        return Bad("arithmetic operation");
    }

    static uint R(int rs, int rt, int rd, int shift, int fn)
    {
        return (uint)((rs << 21) | (rt << 16) | (rd << 11) | (shift << 6) | fn);
    }

    static uint I(int code, int rs, int rt, int imm)
    {
        return (uint)((code << 26) | (rs << 21) | (rt << 16) | (imm & 65535));
    }
}
