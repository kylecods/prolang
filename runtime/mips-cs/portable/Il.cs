namespace ProLang.Portable;

/// <summary>
/// CIL opcode values (ECMA-335 III.1.2), named by their mnemonic in PascalCase: <c>ldc.i4.0</c> is <c>LdcI4_0</c>,
/// <c>ldarg.s</c> is <c>LdargS</c>, <c>bne.un.s</c> is <c>BneUnS</c>.
/// </summary>
/// <remarks>
/// <para>A two-byte opcode (prefix 0xfe) is stored as 0xfe00 | second byte, which is how <c>Decoder</c> forms
///   <c>DecodedOp.Code</c>. Only the opcodes the decoder accepts are listed.</para>
/// <para>Switches over opcodes (<c>Interpreter.ExecuteResolved</c>, <c>Arithmetic.Binary</c> and the JIT's
///   arithmetic emitter) keep numeric literals for their case labels, because a C case label cannot be a variable.
///   Each such label carries a comment naming its mnemonic from this class.</para>
/// </remarks>
internal static class Il
{
    // The first byte of a two-byte opcode, and the value its second byte is added to.
    internal const int TwoBytePrefix = 0xfe;
    internal const int TwoByteBase = 0xfe00;

    internal const int Nop = 0x00;
    internal const int Ldarg_0 = 0x02;
    internal const int Ldarg_1 = 0x03;
    internal const int Ldarg_2 = 0x04;
    internal const int Ldarg_3 = 0x05;
    internal const int Ldloc_0 = 0x06;
    internal const int Ldloc_1 = 0x07;
    internal const int Ldloc_2 = 0x08;
    internal const int Ldloc_3 = 0x09;
    internal const int Stloc_0 = 0x0a;
    internal const int Stloc_1 = 0x0b;
    internal const int Stloc_2 = 0x0c;
    internal const int Stloc_3 = 0x0d;
    internal const int LdargS = 0x0e;
    internal const int LdargaS = 0x0f;
    internal const int StargS = 0x10;
    internal const int LdlocS = 0x11;
    internal const int LdlocaS = 0x12;
    internal const int StlocS = 0x13;
    internal const int Ldnull = 0x14;
    internal const int LdcI4_M1 = 0x15;
    internal const int LdcI4_0 = 0x16;
    internal const int LdcI4_1 = 0x17;
    internal const int LdcI4_2 = 0x18;
    internal const int LdcI4_3 = 0x19;
    internal const int LdcI4_4 = 0x1a;
    internal const int LdcI4_5 = 0x1b;
    internal const int LdcI4_6 = 0x1c;
    internal const int LdcI4_7 = 0x1d;
    internal const int LdcI4_8 = 0x1e;
    internal const int LdcI4S = 0x1f;
    internal const int LdcI4 = 0x20;
    internal const int Dup = 0x25;
    internal const int Pop = 0x26;
    internal const int Call = 0x28;
    internal const int Ret = 0x2a;
    internal const int BrS = 0x2b;
    internal const int BrfalseS = 0x2c;
    internal const int BrtrueS = 0x2d;
    internal const int BeqS = 0x2e;
    internal const int BgeS = 0x2f;
    internal const int BgtS = 0x30;
    internal const int BleS = 0x31;
    internal const int BltS = 0x32;
    internal const int BneUnS = 0x33;
    internal const int BgeUnS = 0x34;
    internal const int BgtUnS = 0x35;
    internal const int BleUnS = 0x36;
    internal const int BltUnS = 0x37;
    internal const int Br = 0x38;
    internal const int Brfalse = 0x39;
    internal const int Brtrue = 0x3a;
    internal const int Beq = 0x3b;
    internal const int Bge = 0x3c;
    internal const int Bgt = 0x3d;
    internal const int Ble = 0x3e;
    internal const int Blt = 0x3f;
    internal const int BneUn = 0x40;
    internal const int BgeUn = 0x41;
    internal const int BgtUn = 0x42;
    internal const int BleUn = 0x43;
    internal const int BltUn = 0x44;
    internal const int Add = 0x58;
    internal const int Sub = 0x59;
    internal const int Mul = 0x5a;
    internal const int Div = 0x5b;
    internal const int DivUn = 0x5c;
    internal const int Rem = 0x5d;
    internal const int RemUn = 0x5e;
    internal const int And = 0x5f;
    internal const int Or = 0x60;
    internal const int Xor = 0x61;
    internal const int Shl = 0x62;
    internal const int Shr = 0x63;
    internal const int ShrUn = 0x64;
    internal const int Neg = 0x65;
    internal const int Not = 0x66;
    internal const int ConvI1 = 0x67;
    internal const int ConvI2 = 0x68;
    internal const int ConvI4 = 0x69;
    internal const int ConvU4 = 0x6d;
    internal const int Callvirt = 0x6f;
    internal const int Ldobj = 0x71;
    internal const int Ldstr = 0x72;
    internal const int Newobj = 0x73;
    internal const int Ldfld = 0x7b;
    internal const int Ldflda = 0x7c;
    internal const int Stfld = 0x7d;
    internal const int Ldsfld = 0x7e;
    internal const int Ldsflda = 0x7f;
    internal const int Stsfld = 0x80;
    internal const int Stobj = 0x81;
    internal const int Box = 0x8c;
    internal const int Newarr = 0x8d;
    internal const int Ldlen = 0x8e;
    internal const int Ldelema = 0x8f;
    internal const int LdelemI1 = 0x90;
    internal const int LdelemU1 = 0x91;
    internal const int LdelemI2 = 0x92;
    internal const int LdelemU2 = 0x93;
    internal const int LdelemI4 = 0x94;
    internal const int LdelemU4 = 0x95;
    internal const int LdelemRef = 0x9a;
    internal const int StelemI = 0x9b;
    internal const int StelemI1 = 0x9c;
    internal const int StelemI2 = 0x9d;
    internal const int StelemI4 = 0x9e;
    internal const int StelemRef = 0xa2;
    internal const int Ldelem = 0xa3;
    internal const int Stelem = 0xa4;
    internal const int UnboxAny = 0xa5;
    internal const int ConvU2 = 0xd1;
    internal const int ConvU1 = 0xd2;
    internal const int ConvI = 0xd3;
    internal const int ConvU = 0xe0;
    internal const int Ceq = 0xfe01;
    internal const int Cgt = 0xfe02;
    internal const int CgtUn = 0xfe03;
    internal const int Clt = 0xfe04;
    internal const int CltUn = 0xfe05;
    internal const int Ldftn = 0xfe06;
    internal const int Ldarg = 0xfe09;
    internal const int Ldarga = 0xfe0a;
    internal const int Starg = 0xfe0b;
    internal const int Ldloc = 0xfe0c;
    internal const int Ldloca = 0xfe0d;
    internal const int Stloc = 0xfe0e;
    internal const int Initobj = 0xfe15;
}
