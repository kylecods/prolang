namespace ProLang.Portable;

/// <summary>How a 32-bit metadata token and a coded index are laid out (ECMA-335 II.22 and II.24.2.6).</summary>
internal static class MetadataToken
{
    // A token is (table id << TableShift) | row. RowMask isolates the row, 1-based.
    internal const int TableShift = 24;
    internal const int RowMask = 0xffffff;

    // The table id of a ldstr token: it indexes the #US heap rather than a table.
    internal const int UserString = 0x70;

    // TypeDefOrRef coded index: TypeDefOrRefTagBits low bits pick the table, the rest is the row.
    internal const int TypeDefOrRefTagBits = 2;
    internal const int TypeDefOrRefTagMask = 3;
    internal const int TypeDefOrRefTypeDef = 0;
    internal const int TypeDefOrRefTypeRef = 1;

    // ResolutionScope coded index: the scope of a TypeRef row. ResolutionScopeTagBits low bits pick the table.
    internal const int ResolutionScopeTagBits = 2;

    // MemberRefParent coded index: the parent of a MemberRef row.
    internal const int MemberRefParentTagBits = 3;
    internal const int MemberRefParentTagMask = 7;
    internal const int MemberRefParentTypeDef = 0;
    internal const int MemberRefParentTypeRef = 1;
    internal const int MemberRefParentTypeSpec = 4;
}
