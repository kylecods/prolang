namespace ProLang.Portable;

/// <summary>
/// The layout the JIT gives generated code: native stack frame offsets and the shape of a <c>Value</c> in memory.
/// </summary>
/// <remarks>
/// <para>Register roles in generated code:</para>
/// <list type="bullet">
/// <item><description><c>$s0</c>: the method's frame, a native pointer to its first <c>Value</c> on the value stack</description></item>
/// <item><description><c>$s1</c>: the address of the <c>Value</c> that receives the return value</description></item>
/// <item><description><c>$s2</c> to <c>$s7</c>: the <c>RegisterCache</c> (<c>RegisterCache.Size</c> registers)</description></item>
/// <item><description><c>$t8</c> and <c>$t9</c>: scratch for checks and for the helper's address</description></item>
/// <item><description><c>$a0</c> to <c>$a2</c>: helper arguments, and <c>$v0</c> a helper's result</description></item>
/// </list>
/// </remarks>
internal static class JitFrame
{
    // Native stack frame: a prologue allocates Size bytes and saves $ra, $s0, $s1 and the cache registers.
    internal const int Size = 64;
    internal const int SaveRa = 60;
    internal const int SaveS0 = 56;
    internal const int SaveS1 = 52;
    // $s2 is saved at SaveCache and each following cache register SaveStride bytes below the previous one.
    internal const int SaveCache = 44;
    internal const int SaveStride = 4;

    // A Value is two int32 words: Kind at KindOffset and Data at DataOffset, SlotSize bytes in all.
    // A value stack slot n is at byte n * SlotSize from $s0; the same offsets address the Value temporary at $sp + 0.
    internal const int SlotSize = 8;
    internal const int KindOffset = 0;
    internal const int DataOffset = 4;
}
