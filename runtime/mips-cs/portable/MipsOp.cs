namespace ProLang.Portable;

/// <summary>
/// MIPS32 primary opcodes (bits 31..26) of the instructions the JIT emits. <c>MipsAssembler.EncodeI</c> takes one.
/// </summary>
/// <remarks>
/// <para>Register-register instructions have opcode 0 and are selected by <c>MipsFunct</c> instead
///   (<c>MipsAssembler.EncodeR</c>).</para>
/// </remarks>
internal static class MipsOp
{
    internal const int Beq = 4;
    internal const int Bne = 5;
    internal const int Addiu = 9;
    internal const int Sltiu = 11;
    internal const int Andi = 12;
    internal const int Ori = 13;
    internal const int Lui = 15;
    internal const int Lw = 35;
    internal const int Sw = 43;
}
