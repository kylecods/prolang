namespace ProLang.Portable;

// Data records shared by every portable class: structs only, with fields and no methods. They are in dependency
// order (Value before HeapObject, DecodedOp and ColdOp before MethodBody) because this file is the first argument
// to cs-to-c, so the generated C declares each type before anything uses it. DesktopHost.cs follows it and is
// only type-checked, never translated.

/// <summary>One guest value: a kind tag and one 32-bit payload, eight bytes in all.</summary>
/// <remarks>
/// <para>Generated MIPS indexes a <c>Value</c> as two int32 words, <c>Kind</c> at byte 0 and <c>Data</c> at byte 4
///   (<c>JitFrame.KindOffset</c>, <c>DataOffset</c>, <c>SlotSize</c>; <c>AbiTests</c> pins them), so the field
///   order and sizes are part of the ABI. The C glue sees the same layout as the generated <c>Value</c> struct.</para>
/// <para>A null reference and a stack slot cleared by <c>Interpreter.Leave</c> are both <c>default(Value)</c>: kind
///   <c>ValueKind.Null</c>, data 0.</para>
/// </remarks>
public struct Value
{
    /// <summary>A <c>ValueKind</c> constant: what <c>Data</c> means.</summary>
    public int Kind;
    /// <summary>The payload. Its meaning depends on <c>Kind</c>:</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>ValueKind.Null</c>: unused, 0.</description></item>
    /// <item><description><c>ValueKind.Int32</c>: the number itself. Booleans, chars and every narrower integer
    ///   type are <c>Int32</c> values too, and an unsigned value holds the same bit pattern as the signed one.</description></item>
    /// <item><description><c>ValueKind.String</c>, <c>Array</c>, <c>Record</c>, <c>Box</c> and <c>Address</c>: a
    ///   handle, an index into <c>Heap.HandleTable</c>. A live handle is always greater than 0; slot 0 is never
    ///   allocated, so 0 can mean none.</description></item>
    /// <item><description><c>ValueKind.FunctionPointer</c> and <c>Delegate</c>: a method index into
    ///   <c>Loader.Bodies</c>. It is not a handle, so the collector does not trace it.</description></item>
    /// </list>
    /// </remarks>
    public int Data;
}

/// <summary>
/// One entry of <c>Heap.HandleTable</c>: a string, array, struct record, box or managed address, or a free slot.
/// </summary>
/// <remarks>
/// <para>A guest reference is the index of its entry, and an entry never moves, so a handle stays valid for as long
///   as the object is reachable. Which fields are meaningful depends on <c>Kind</c>; <c>Heap.AllocateObject</c>
///   hands out entries zeroed, so every unused field is 0 or null.</para>
/// <list type="bullet">
/// <item><description><c>Type</c>: the type code of the contents. A record holds <c>TypeEncoding.Struct | TypeDef
///   row</c>; an array holds its element type code; a string holds <c>ElementType.String</c>; a box holds the
///   declared type of the boxed value. Unused by addresses.</description></item>
/// <item><description><c>Count</c>: the number of entries: the length of <c>Values</c> for a record, array or box
///   (a box has one), or the number of characters of a string. A string's <c>Chars</c> array holds at least
///   <c>Count + 1</c> units: <c>Objects.NewText</c> allocates exactly that many, and <c>Objects.FormatValue</c>
///   allocates <c>NumberBufferChars + 1</c> and then lowers <c>Count</c> to the digits written.</description></item>
/// <item><description><c>Kind</c>: a <c>ValueKind</c> constant. <c>ValueKind.Null</c> marks a free entry.</description></item>
/// <item><description><c>Marked</c>: the collector's mark bit: 1 while <c>Heap.Collect</c> has reached the object,
///   0 otherwise. The sweep resets it, so it is 0 between collections.</description></item>
/// <item><description><c>Permanent</c>: nonzero for an object the collector treats as a root and never frees: the
///   string literals the decoder creates for <c>ldstr</c>, and the cached zero records of struct types
///   (<c>Objects.ZeroCache</c>).</description></item>
/// <item><description><c>ArrayHandle</c>: used only by an address of an array element: the handle of that array, or
///   0. The collector marks it, which keeps the array alive while the address is.</description></item>
/// </list>
/// <para><c>Parent</c> is overloaded, and which meaning applies depends on <c>Kind</c>. In a free entry (<c>Kind ==
///   ValueKind.Null</c>) it is the handle of the next free entry, and 0 ends the free list: <c>Heap.Reset</c> links
///   every entry into the list, <c>Heap.AllocateObject</c> pops the head and clears the entry, and
///   <c>Heap.Collect</c> pushes each swept entry. In a live address (<c>Kind == ValueKind.Address</c>) it is the
///   handle of the parent address when this address names a field of a struct that is itself reached through an
///   address, and 0 when it is a root address (a stack slot, or an array element when <c>ArrayHandle</c> is not 0).
///   In every other live object it is 0. The two uses cannot be confused: the collector skips <c>Kind ==
///   ValueKind.Null</c> entries and follows <c>Parent</c> only for addresses.</para>
/// <para><c>ValueIndex</c> is used only by an address, and is the index of the value the address points at; which
///   array it indexes depends on <c>Parent</c> and <c>ArrayHandle</c>. When <c>Parent == 0</c> and <c>ArrayHandle ==
///   0</c> it is an absolute index into <c>Interpreter.Memory</c>, the value stack or the globals area. When
///   <c>Parent == 0</c> and <c>ArrayHandle != 0</c> it is the element index within that array. When <c>Parent !=
///   0</c> it is the field slot, an index into the <c>Values</c> of the record that the parent address reads.</para>
/// </remarks>
public struct HeapObject
{
    public int Type, Count, Kind, Marked, Permanent, Parent, ValueIndex, ArrayHandle;
    /// <summary>The entries of a record, array or box. Null for the other kinds.</summary>
    public Value[] Values;
    /// <summary>
    /// The UTF-16 units of a string: the first <c>Count</c> are the text, and at least one spare unit follows
    /// (<c>Chars.Length &gt;= Count + 1</c>). Null for the other kinds.
    /// </summary>
    public ushort[] Chars;
}

/// <summary>A bounds-checked cursor over a window of the loaded image.</summary>
/// <remarks>
/// <para>Every read through a reader goes through <c>Metadata.Consume</c>, which checks the window and the position
///   against the image, so a bad offset fails with 'binary read outside image' instead of reading somewhere else.
///   The few comparisons that index <c>Metadata.Image</c> directly are described in <c>Metadata</c>.</para>
/// <list type="bullet">
/// <item><description><c>Start</c>: the file offset in the image at which the window begins.</description></item>
/// <item><description><c>Length</c>: the size of the window in bytes.</description></item>
/// <item><description><c>Position</c>: the next byte to read, as an offset from <c>Start</c>.</description></item>
/// </list>
/// </remarks>
public struct ByteReader
{
    public int Start, Length, Position;
}

/// <summary>One row of the PE section table: where a section sits in memory and in the file.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>Rva</c>: the relative virtual address at which the section is mapped.</description></item>
/// <item><description><c>RawOffset</c>: the file offset of the section's data.</description></item>
/// <item><description><c>RawSize</c>: the size of the section's data in the file.</description></item>
/// </list>
/// </remarks>
public struct PeSection
{
    public uint Rva, RawOffset, RawSize;
}

/// <summary>A decoded method signature (ECMA-335 II.23.2.1). The type fields are type codes, not flags.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>ParamCount</c>: the number of declared parameters, not counting <c>this</c>.</description></item>
/// <item><description><c>ReturnType</c>: the return type code, <c>ElementType.Void</c> for no value. A type code,
///   not a 0/1 flag; compare <c>DecodedOp.ReturnsValue</c>.</description></item>
/// <item><description><c>HasThis</c>: 1 for an instance method (the signature has the HASTHIS bit), else 0.</description></item>
/// </list>
/// </remarks>
public struct MethodSignature
{
    public int ParamCount, ReturnType, HasThis;
    /// <summary>The type code of each declared parameter, <c>ParamCount</c> entries.</summary>
    public int[] ParamTypes;
}

/// <summary>One decoded IL instruction, as <c>Decoder</c> and <c>Verifier</c> see it.</summary>
/// <remarks>
/// <para>These records are temporary: <c>Planner.BuildPlan</c> turns them into <c>PlanOp</c> and <c>ColdOp</c>
///   records and <c>MethodBody.Ops</c> is released.</para>
/// <list type="bullet">
/// <item><description><c>Code</c>: the opcode, an <c>Il</c> constant. A two-byte opcode is stored as 0xfe00 | its
///   second byte.</description></item>
/// <item><description><c>Operand</c>: the inline operand. A branch holds its target as an absolute byte offset
///   while decoding and, once <c>Decoder.ResolveBranchTargets</c> has run, as the index of the target instruction.
///   A field instruction holds the field index (<c>TypeSystem.ResolveFieldIndex</c>). A call, <c>ldftn</c> or type
///   instruction keeps its raw metadata token, and <c>ldstr</c> its <c>#US</c> token; what they resolve to is in
///   the fields below.</description></item>
/// <item><description><c>Offset</c>: the byte offset of the instruction within the method's IL.</description></item>
/// <item><description><c>StackDepth</c>: the evaluation stack depth before the instruction executes, set by
///   <c>Verifier.Verify</c>; -1 while the instruction has not been reached, which also marks unreachable code.</description></item>
/// <item><description><c>MethodIndex</c>: for <c>call</c>, <c>newobj</c> and <c>ldftn</c> of a method in this
///   program, its index in <c>Loader.Bodies</c>. -1 otherwise, including for a call to a native.</description></item>
/// <item><description><c>NativeId</c>: for a call or <c>newobj</c> of a method outside the program, the
///   <c>Natives</c> id that <c>Natives.ResolveNative</c> chose. 0 otherwise.</description></item>
/// <item><description><c>ArgCount</c>: for a call, the number of values it pops, counting <c>this</c> for an
///   instance method.</description></item>
/// <item><description><c>ReturnsValue</c>: for a call, 1 if the callee returns a value and 0 if it returns void. A
///   0/1 flag, not a type code; the callee's type code is <c>ResultType</c>. Compare <c>MethodBody.ReturnType</c>
///   and <c>MethodSignature.ReturnType</c>, which are type codes.</description></item>
/// <item><description><c>Type</c>: the type code the instruction operates on, from <c>TypeSystem.TypeFromToken</c>,
///   for <c>box</c>, <c>unbox.any</c>, <c>newarr</c>, <c>initobj</c>, <c>ldobj</c>, <c>stobj</c>, <c>ldelem</c>,
///   <c>stelem</c> and <c>ldelema</c>. For a call of a native, the type code of its first parameter, which
///   <c>Natives</c> uses to format printed values.</description></item>
/// <item><description><c>ResultType</c>: for a call, the callee's return type code (<c>ElementType.Void</c> for
///   none).</description></item>
/// </list>
/// </remarks>
public struct DecodedOp
{
    public int Code, Operand, Offset, StackDepth, MethodIndex, NativeId, ArgCount, ReturnsValue, Type, ResultType;
    /// <summary>
    /// For <c>ldstr</c>: the string, as a <c>ValueKind.String</c> value that refers to a permanent object.
    /// </summary>
    public Value Literal;
}

/// <summary>
/// The hot, fixed-size execution record of one instruction. One exists per instruction of a verified method, in
/// <c>MethodBody.Plan</c>, and both engines run from it.
/// </summary>
/// <remarks>
/// <para>Whatever an instruction needs beyond these fields (a callee, a type, a literal) lives in the sparse
///   <c>ColdOp</c> table, reached through <c>ColdIndex</c>.</para>
/// <list type="bullet">
/// <item><description><c>Code</c>: the opcode, an <c>Il</c> constant, copied from <c>DecodedOp</c>.</description></item>
/// <item><description><c>Operand</c>: the operand, copied from <c>DecodedOp</c>. A branch's operand is the index of
///   its target instruction.</description></item>
/// <item><description><c>StackSlot</c>: the slot at which the evaluation stack stands before the instruction
///   executes, counted from the start of the frame: <c>ArgCount + LocalCount + stack depth</c>. It is an absolute
///   frame index, not a depth, so the top operand is at <c>frame + StackSlot - 1</c> and results go back to the
///   same area. -1 for an instruction the verifier never reached.</description></item>
/// <item><description><c>Offset</c>: the byte offset of the instruction in the method's original IL.</description></item>
/// <item><description><c>Flags</c>: packed bits, laid out by the <c>Plan*</c> constants of <c>Planner</c>; see
///   below.</description></item>
/// <item><description><c>ColdIndex</c>: the index of this instruction's entry in <c>MethodBody.Cold</c>, or 0 if it
///   has none (entry 0 is an empty record).</description></item>
/// </list>
/// <para>The bits of <c>Flags</c> are: bits 0 to 6, the number of stack entries the instruction pops
///   (<c>Planner.PlanPopMask</c>); bits 7 and 8, the number it pushes (<c>Planner.PlanPushShift</c>,
///   <c>PlanPushMask</c>; <c>dup</c> pops 1 and pushes 2); bit 9, <c>Planner.PlanBlockStart</c>, set on the first
///   instruction of every basic block, where the JIT flushes its register cache and charges the block; and bits 10
///   to 12, the operation category (<c>Planner.PlanCategoryShift</c>, <c>PlanCategoryMask</c>), one of
///   <c>Planner.PlanOther</c> to <c>PlanCompareBranch</c>.</para>
/// </remarks>
public struct PlanOp
{
    public int Code, Operand, StackSlot, Offset, Flags, ColdIndex;
}

/// <summary>
/// The call, type and literal data of one instruction, kept out of <c>PlanOp</c> so the hot record stays small.
/// </summary>
/// <remarks>
/// <para>Only the opcodes <c>Planner.HasColdMetadata</c> names have an entry. The fields are copied from the
///   <c>DecodedOp</c> fields of the same name; see there.</para>
/// <list type="bullet">
/// <item><description><c>MethodIndex</c>: the callee's index in <c>Loader.Bodies</c> for a call within the program,
///   else -1.</description></item>
/// <item><description><c>NativeId</c>: the <c>Natives</c> id of a call outside the program, else 0.</description></item>
/// <item><description><c>ArgCount</c>: the number of values a call pops, counting <c>this</c>.</description></item>
/// <item><description><c>ReturnsValue</c>: 1 if a call's callee returns a value, else 0. A 0/1 flag, not a type
///   code; the type code is <c>ResultType</c>.</description></item>
/// <item><description><c>Type</c>: the type code the instruction operates on.</description></item>
/// <item><description><c>ResultType</c>: a call's return type code.</description></item>
/// </list>
/// </remarks>
public struct ColdOp
{
    public int MethodIndex, NativeId, ArgCount, ReturnsValue, Type, ResultType;
    /// <summary>The string value that <c>ldstr</c> pushes.</summary>
    public Value Literal;
}

/// <summary>
/// Everything the runtime knows about one method of the loaded program: its signature, its decoded and verified
/// code and its execution plan.
/// </summary>
/// <remarks>
/// <para><c>Loader.Bodies</c> holds one per MethodDef row, in row order. <c>Loader.LoadMethods</c> fills the
///   signature fields, <c>Decoder.Decode</c> the header and local fields, and <c>Planner.BuildPlan</c> replaces
///   <c>Ops</c> with <c>Plan</c> and <c>Cold</c>.</para>
/// <list type="bullet">
/// <item><description><c>Rva</c>: the relative virtual address of the method's IL header.</description></item>
/// <item><description><c>SignatureIndex</c>: the index of the method's signature blob in the <c>#Blob</c> heap.</description></item>
/// <item><description><c>ArgCount</c>: the number of parameters. Only static methods load, so there is no
///   <c>this</c>.</description></item>
/// <item><description><c>ReturnType</c>: the return type code, <c>ElementType.Void</c> if the method returns
///   nothing. A type code, not a 0/1 flag; compare <c>DecodedOp.ReturnsValue</c>.</description></item>
/// <item><description><c>LocalCount</c>: the number of local variables.</description></item>
/// <item><description><c>MaxStack</c>: the most evaluation stack slots the method uses, from its header.
///   <c>Jit.Compile</c> raises it to make room for the scratch slots of inlined callees.</description></item>
/// <item><description><c>OpCount</c>: the number of instructions.</description></item>
/// </list>
/// </remarks>
public struct MethodBody
{
    /// <summary>The file offset of the method's NUL-terminated name in the <c>#Strings</c> heap.</summary>
    public int NameOffset;
    public int Rva, SignatureIndex, ArgCount, ReturnType, LocalCount, MaxStack, OpCount;
    /// <summary>The type code of each local, <c>LocalCount</c> entries.</summary>
    public int[] LocalTypes;
    /// <summary>The type code of each parameter, <c>ArgCount</c> entries.</summary>
    public int[] ArgTypes;
    /// <summary>
    /// The zero value of each local, which <c>Interpreter.Enter</c> writes at the start of every call.
    /// </summary>
    public Value[] LocalDefaults;
    /// <summary>The decoded instructions. Null once the plan is built.</summary>
    public DecodedOp[] Ops;
    /// <summary>The execution plan, one record per instruction.</summary>
    public PlanOp[] Plan;
    /// <summary>The cold records that <c>PlanOp.ColdIndex</c> indexes; entry 0 is empty.</summary>
    public ColdOp[] Cold;
}
