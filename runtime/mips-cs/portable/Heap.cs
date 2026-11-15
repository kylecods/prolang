namespace ProLang.Portable;

/// <summary>The handle table and the garbage collector.</summary>
/// <remarks>
/// <para>Guest references are stable integer handles: an index into <c>HandleTable</c>, which is allocated once at
///   its full size and never moves its entries. A handle stays valid for exactly as long as the object is
///   reachable, and slot 0 is never allocated, so 0 means no object.</para>
/// <para>Collection runs only between guest instructions. <c>Interpreter.Invoke</c> calls <c>CollectIfNeeded</c>
///   before each instruction it hands to <c>ExecuteResolved</c> (<c>ret</c> and the branches skip it, and allocate
///   nothing), and <c>Interpreter.Slow</c> does so before it runs an instruction on behalf of JIT code, after the
///   JIT has flushed its register cache into the value stack. So at a collection the roots are exactly
///   <c>Interpreter.Memory</c>, the permanent objects and <c>Natives.OutputHandle</c>, and no register or native
///   stack slot holds a handle that the collector cannot see. The collector is a stop-the-world, non-moving mark
///   and sweep.</para>
/// </remarks>
internal static class Heap
{
    // A collection is due once this many objects, or this many bytes (as Objects.NewObject and
    // Objects.NewText count them), have been allocated since the last one.
    internal const int CollectionAllocations = 1024;
    internal const int CollectionBytes = 16384;

    /// <summary>
    /// The objects, indexed by handle. A free entry has <c>Kind == ValueKind.Null</c> and is linked into the free
    /// list through <c>Parent</c>; the C glue reads this table as <c>Heap_HandleTable</c>.
    /// </summary>
    internal static HeapObject[] HandleTable = null!;
    private static int[] _markQueue = null!;
    private static int _freeHead, _allocationsSinceCollection;
    /// <summary>
    /// Bytes allocated since the last collection, as <c>Objects.NewObject</c> and <c>Objects.NewText</c> count
    /// them. It paces the collector together with the allocation count.
    /// </summary>
    internal static int AllocationBytes;
    private static int _collectionCount;

    /// <summary>
    /// Allocates an empty <c>HandleTable</c> and mark queue, links every entry from 1 up into the free list, and
    /// zeroes the counters.
    /// </summary>
    internal static void Reset()
    {
        _freeHead = 0;
        HandleTable = new HeapObject[Limits.HandleTableSize];
        _markQueue = new int[Limits.HandleTableSize];
        for (int i = HandleTable.Length - 1; i >= 1; i--)
        {
            HandleTable[i].Parent = _freeHead;
            _freeHead = i;
        }
        _allocationsSinceCollection = 0;
        AllocationBytes = 0;
        _collectionCount = 0;
    }

    /// <summary>
    /// Takes a handle from the free list, clears the entry, and sets its kind and <c>Permanent</c> flag. Fails with
    /// 'managed heap handle limit' when none is free. It does not collect: <c>CollectIfNeeded</c> does, between
    /// instructions.
    /// </summary>
    internal static int AllocateObject(int kind, int permanent)
    {
        if (_freeHead == 0)
        {
            return Runtime.Fail("managed heap handle limit");
        }
        int handle = _freeHead;
        _freeHead = HandleTable[handle].Parent;
        HandleTable[handle] = default(HeapObject);
        HandleTable[handle].Kind = kind;
        HandleTable[handle].Permanent = permanent;
        _allocationsSinceCollection++;
        return handle;
    }

    /// <summary>
    /// Marks the object at <c>handle</c> if it is live and not yet marked, and queues it so that <c>Collect</c>
    /// will mark what it refers to. A handle that is 0, out of range or free is ignored.
    /// </summary>
    private static void MarkHandle(int handle, ref int tail)
    {
        if (handle <= 0 || handle >= HandleTable.Length || HandleTable[handle].Kind == ValueKind.Null || HandleTable[handle].Marked != 0)
        {
            return;
        }
        HandleTable[handle].Marked = 1;
        _markQueue[tail] = handle;
        tail++;
    }

    /// <summary>
    /// Marks the object that <c>value</c> refers to, if its kind holds a handle. Integers, nulls, function pointers
    /// and delegates hold none.
    /// </summary>
    private static void MarkValue(Value value, ref int tail)
    {
        if (value.Kind == ValueKind.String || value.Kind == ValueKind.Array || value.Kind == ValueKind.Record || value.Kind == ValueKind.Address || value.Kind == ValueKind.Box)
        {
            MarkHandle(value.Data, ref tail);
        }
    }

    /// <summary>
    /// Collects if 1024 objects or 16 KiB have been allocated since the last collection, or if no handle is free.
    /// It is called only at instruction boundaries.
    /// </summary>
    internal static void CollectIfNeeded()
    {
        if (_allocationsSinceCollection < CollectionAllocations && AllocationBytes < CollectionBytes && _freeHead != 0)
        {
            return;
        }
        Collect();
    }

    /// <summary>Runs a full mark and sweep.</summary>
    /// <remarks>
    /// <para>The roots are every slot of <c>Interpreter.Memory</c> (the value stack and the globals), every
    ///   permanent object, and <c>Natives.OutputHandle</c>. Marking uses an explicit queue, not recursion: it
    ///   follows the <c>Values</c> of a record, array or box, and the <c>Parent</c> and <c>ArrayHandle</c> of an
    ///   address; a string refers to nothing.</para>
    /// <para>The sweep frees every live object left unmarked: for an array it takes the elements off
    ///   <c>Objects.TotalArrayElements</c>, hands its <c>Values</c> and <c>Chars</c> arrays back to the host,
    ///   clears the entry and pushes it onto the free list. It clears the mark of every survivor, whose handle does
    ///   not change. Finally it zeroes the allocation counters and tells the host through <c>Host.Collected</c>.</para>
    /// </remarks>
    internal static void Collect()
    {
        int tail = 0;
        for (int i = 0; i < Interpreter.Memory.Length; i++)
        {
            MarkValue(Interpreter.Memory[i], ref tail);
        }
        for (int i = 1; i < HandleTable.Length; i++)
        {
            if (HandleTable[i].Kind != ValueKind.Null && HandleTable[i].Permanent != 0)
            {
                MarkHandle(i, ref tail);
            }
        }
        MarkHandle(Natives.OutputHandle, ref tail);
        for (int head = 0; head < tail; head++)
        {
            int handle = _markQueue[head];
            if (HandleTable[handle].Kind == ValueKind.Address)
            {
                MarkHandle(HandleTable[handle].Parent, ref tail);
                MarkHandle(HandleTable[handle].ArrayHandle, ref tail);
            }
            else if (HandleTable[handle].Kind != ValueKind.String)
            {
                for (int i = 0; i < HandleTable[handle].Count; i++)
                {
                    MarkValue(HandleTable[handle].Values[i], ref tail);
                }
            }
        }
        for (int i = 1; i < HandleTable.Length; i++)
        {
            if (HandleTable[i].Kind == ValueKind.Null)
            {
                continue;
            }
            if (HandleTable[i].Marked != 0)
            {
                HandleTable[i].Marked = 0;
                continue;
            }
            if (HandleTable[i].Kind == ValueKind.Array)
            {
                Objects.TotalArrayElements -= HandleTable[i].Count;
            }
            if (HandleTable[i].Values != null)
            {
                Host.ReleaseVals(HandleTable[i].Values);
            }
            if (HandleTable[i].Chars != null)
            {
                Host.ReleaseChars(HandleTable[i].Chars);
            }
            HandleTable[i] = default(HeapObject);
            HandleTable[i].Parent = _freeHead;
            _freeHead = i;
        }
        _allocationsSinceCollection = 0;
        AllocationBytes = 0;
        _collectionCount++;
        Host.Collected(_collectionCount);
    }
}
