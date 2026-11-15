namespace ProLang.Portable;

/// <summary>
/// ECMA-335 II.22 metadata table ids: the row index into <c>Metadata.TableRows</c>, <c>TableOffsets</c> and
/// <c>TableWidths</c>, and the high byte of a metadata token.
/// </summary>
/// <remarks>
/// <para><c>Metadata.RowSize</c> switches on these values as literals (a C case label cannot be a variable) and
///   names each table in a comment.</para>
/// </remarks>
internal static class MetadataTable
{
    internal const int Module = 0;
    internal const int TypeRef = 1;
    internal const int TypeDef = 2;
    internal const int FieldPtr = 3;
    internal const int Field = 4;
    internal const int MethodPtr = 5;
    internal const int MethodDef = 6;
    internal const int ParamPtr = 7;
    internal const int Param = 8;
    internal const int InterfaceImpl = 9;
    internal const int MemberRef = 10;
    internal const int Constant = 11;
    internal const int CustomAttribute = 12;
    internal const int FieldMarshal = 13;
    internal const int DeclSecurity = 14;
    internal const int ClassLayout = 15;
    internal const int FieldLayout = 16;
    internal const int StandAloneSig = 17;
    internal const int EventMap = 18;
    internal const int EventPtr = 19;
    internal const int Event = 20;
    internal const int PropertyMap = 21;
    internal const int PropertyPtr = 22;
    internal const int Property = 23;
    internal const int MethodSemantics = 24;
    internal const int MethodImpl = 25;
    internal const int ModuleRef = 26;
    internal const int TypeSpec = 27;
    internal const int ImplMap = 28;
    internal const int FieldRva = 29;
    internal const int EncLog = 30;
    internal const int EncMap = 31;
    internal const int Assembly = 32;
    internal const int AssemblyProcessor = 33;
    internal const int AssemblyOs = 34;
    internal const int AssemblyRef = 35;
    internal const int AssemblyRefProcessor = 36;
    internal const int AssemblyRefOs = 37;
    internal const int File = 38;
    internal const int ExportedType = 39;
    internal const int ManifestResource = 40;
    internal const int NestedClass = 41;
    internal const int GenericParam = 42;
    internal const int MethodSpec = 43;
    internal const int GenericParamConstraint = 44;

    // The number of defined tables (ids 0 through Count - 1).
    internal const int Count = 45;
}
