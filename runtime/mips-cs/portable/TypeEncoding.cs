namespace ProLang.Portable;

/// <summary>
/// The composite type codes <c>TypeSystem.ReadType</c> builds from a signature, beyond the plain <c>ElementType</c>
/// values.
/// </summary>
/// <remarks>
/// <para>Every composite code is far above the largest <c>ElementType</c>, so the two ranges never collide.</para>
/// </remarks>
internal static class TypeEncoding
{
    // A struct is Struct | TypeDef row. StructTagMask isolates the tag and StructRowMask the row.
    internal const int Struct = 0x100000;
    internal const int StructRowMask = 0xfffff;
    // uint, like the literal it replaces: `int & uint` is a long comparison in C# and in the generated C.
    internal const uint StructTagMask = 0xfff00000;

    // A generic parameter is GenericParam | index (the index fits GenericParamIndexMask).
    internal const int GenericParam = 0x1000;
    internal const int GenericParamIndexMask = 4095;
    internal const uint GenericParamTagMask = 0xfffff000;

    // An array of a primitive element (code below PrimitiveArray) is PrimitiveArray | element.
    internal const int PrimitiveArray = 128;
    // An array of a composite element (struct or generic parameter) is CompositeArray | element.
    internal const int CompositeArray = 0x40000000;
    // Either array tag. ReadType rejects an element that carries one, so arrays do not nest.
    internal const int ArrayMask = 0x40000080;
}
