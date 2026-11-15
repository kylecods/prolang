namespace ProLang.Portable;

/// <summary>
/// Arithmetic on guest values: building and checking <c>Int32</c> values, the truthiness rule, and the IL integer
/// operations.
/// </summary>
internal static class Arithmetic
{
    /// <summary>An <c>Int32</c> value holding <c>number</c>.</summary>
    internal static Value MakeInt32(int number)
    {
        Value value = default;
        value.Kind = ValueKind.Int32;
        value.Data = number;
        return value;
    }

    /// <summary>The number held by <c>value</c>. Fails with 'expected an int32 value' for any other kind.</summary>
    internal static int ExpectInt32(Value value)
    {
        if (value.Kind != ValueKind.Int32)
        {
            Runtime.Fail("expected an int32 value");
        }

        return (int)value.Data;
    }

    /// <summary>
    /// True for the type codes <c>Boolean</c> through <c>U4</c>. Values of those types are held as <c>Int32</c>;
    /// the engines use this test to decide where a value must be checked to be an <c>Int32</c>.
    /// </summary>
    internal static bool IsIntegerType(int type)
    {
        return type >= ElementType.Boolean && type <= ElementType.U4;
    }

    /// <summary>
    /// The <c>brtrue</c> and <c>brfalse</c> test: null is false, an <c>Int32</c> is true when it is not zero, and
    /// every other kind (a reference, an address, a box) is true.
    /// </summary>
    internal static bool IsTruthy(Value value)
    {
        return value.Kind != ValueKind.Null && (value.Kind != ValueKind.Int32 || value.Data != 0);
    }

    /// <summary>
    /// Evaluates a binary IL operation on two int32 values, with CLI semantics. <c>code</c> is <c>Il.Add</c> to
    /// <c>Il.ShrUn</c>, or <c>Il.Cgt</c> to <c>Il.CltUn</c>; the comparisons return 1 or 0. Division and remainder
    /// fail with 'division by zero' on a zero divisor, <c>int.MinValue / -1</c> fails with 'int32 division
    /// overflow', and a signed remainder by -1 is 0. A shift count is masked to five bits, and the unsigned forms
    /// treat the operands as <c>uint</c>.
    /// </summary>
    /// <remarks>
    /// <para>The case labels are the opcode values as numeric literals, each with a comment naming its <c>Il</c>
    ///   constant, because a C case label cannot be a variable. Any other code fails with 'arithmetic operation'.</para>
    /// </remarks>
    internal static int Binary(int code, int left, int right)
    {
        unchecked
        {
            // Case labels are Il opcodes (Il.Add to Il.CltUn), kept as literals because a C case label cannot be a variable.
            switch (code)
            {
                case 0x58: // Add
                    return left + right;
                case 0x59: // Sub
                    return left - right;
                case 0x5a: // Mul
                    return left * right;
                case 0x5b: // Div
                    if (right == 0)
                    {
                        Runtime.Fail("division by zero");
                    }

                    if (left == -2147483647 - 1 && right == -1)
                    {
                        Runtime.Fail("int32 division overflow");
                    }

                    return left / right;
                case 0x5c: // DivUn
                    if (right == 0)
                    {
                        Runtime.Fail("division by zero");
                    }

                    return (int)((uint)left / (uint)right);
                case 0x5d: // Rem
                    if (right == 0)
                    {
                        Runtime.Fail("division by zero");
                    }

                    return right == -1 ? 0 : left % right;
                case 0x5e: // RemUn
                    if (right == 0)
                    {
                        Runtime.Fail("division by zero");
                    }

                    return (int)((uint)left % (uint)right);
                case 0x5f: // And
                    return left & right;
                case 0x60: // Or
                    return left | right;
                case 0x61: // Xor
                    return left ^ right;
                case 0x62: // Shl
                    return left << (right & 31);
                case 0x63: // Shr
                    return left >> (right & 31);
                case 0x64: // ShrUn
                    return (int)((uint)left >> (right & 31));
                case 0xfe02: // Cgt
                    return left > right ? 1 : 0;
                case 0xfe03: // CgtUn
                    return (uint)left > (uint)right ? 1 : 0;
                case 0xfe04: // Clt
                    return left < right ? 1 : 0;
                case 0xfe05: // CltUn
                    return (uint)left < (uint)right ? 1 : 0;
            }
        }

        return Runtime.Fail("arithmetic operation");
    }
}
