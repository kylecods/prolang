namespace ProLang.Portable;

/// <summary>ECMA-335 II.23.1.16 ELEMENT_TYPE codes, as they appear in signature blobs.</summary>
/// <remarks>
/// <para>The runtime also uses them as type codes (<c>MethodBody.ReturnType</c>, <c>HeapObject.Type</c>, the field
///   type table); see <c>TypeEncoding</c> for the composite codes built on top of these. Not named <c>TypeCode</c>,
///   which would clash with <c>System.TypeCode</c>.</para>
/// </remarks>
internal static class ElementType
{
    internal const int Void = 0x01;
    internal const int Boolean = 0x02;
    internal const int Char = 0x03;
    internal const int I1 = 0x04;
    internal const int U1 = 0x05;
    internal const int I2 = 0x06;
    internal const int U2 = 0x07;
    internal const int I4 = 0x08;
    internal const int U4 = 0x09;
    internal const int I8 = 0x0a;
    internal const int U8 = 0x0b;
    internal const int R4 = 0x0c;
    internal const int R8 = 0x0d;
    internal const int String = 0x0e;
    internal const int ValueType = 0x11;
    internal const int Class = 0x12;
    // A generic type parameter, written `!n`.
    internal const int Var = 0x13;
    internal const int GenericInst = 0x15;
    // Native-sized integer. TypeSystem.ReadType accepts it wherever a signature names a type and returns it as a type
    // code of its own; the only place a signature is matched against it is the second parameter (the function
    // pointer) of the Func and Action constructors in Natives.ResolveDelegateNative.
    internal const int I = 0x18;
    internal const int Object = 0x1c;
    internal const int SzArray = 0x1d;
}
