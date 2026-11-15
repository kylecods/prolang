namespace ProLang.Portable;

/// <summary>
/// MIPS32 SPECIAL function codes (bits 5..0) of the register-register instructions the JIT emits.
/// <c>MipsAssembler.EncodeR</c> takes one. The names are the instruction mnemonics.
/// </summary>
internal static class MipsFunct
{
    internal const int Srl = 2;
    internal const int Sra = 3;
    internal const int Sllv = 4;
    internal const int Srlv = 6;
    internal const int Srav = 7;
    internal const int Jr = 8;
    internal const int Jalr = 9;
    internal const int Mfhi = 16;
    internal const int Mflo = 18;
    internal const int Mult = 24;
    internal const int Div = 26;
    internal const int Divu = 27;
    internal const int Addu = 33;
    internal const int Subu = 35;
    internal const int And = 36;
    internal const int Or = 37;
    internal const int Xor = 38;
    internal const int Slt = 42;
    internal const int Sltu = 43;
}
