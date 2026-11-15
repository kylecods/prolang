namespace ProLang.Portable;

/// <summary>
/// What a <c>Value</c> or a heap object holds: <c>Value.Kind</c>, <c>HeapObject.Kind</c>, and the first argument of
/// <c>Objects.MakeReference</c>.
/// </summary>
/// <remarks>
/// <para><c>Value.Data</c> is documented per kind on <c>Value</c>. Every initializer is one literal because cs-to-c
///   emits a const as <c>static int32_t Class_Name = &lt;literal&gt;;</c>.</para>
/// </remarks>
internal static class ValueKind
{
    // An empty Value; as a HeapObject.Kind it marks a free handle-table entry.
    internal const int Null = 0;
    internal const int Int32 = 1;
    internal const int String = 2;
    internal const int Array = 3;
    internal const int Record = 4;
    // A managed address: a HeapObject of this kind names a stack slot, an array element or a record field.
    internal const int Address = 5;
    // A function pointer made by ldftn; Value.Data is the method index.
    internal const int FunctionPointer = 6;
    internal const int Delegate = 7;
    internal const int Box = 8;
}
