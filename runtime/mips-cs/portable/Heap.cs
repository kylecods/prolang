namespace ProLang.Portable;

// Guest references are stable integer handles. Collection runs only between guest
// instructions, after the JIT has flushed its register cache into the value stack.
public static partial class VM
{
    static Obj[] objects = null!;
    static int[] markQueue = null!;
    static int freeHead, allocationsSinceCollection, allocationBytes, collectionCount;

    static void ResetHeap()
    {
        freeHead = 0;
        objects = new Obj[16384];
        markQueue = new int[16384];
        for (int i = objects.Length - 1; i >= 1; i--)
        {
            objects[i].parent = freeHead;
            freeHead = i;
        }
        allocationsSinceCollection = 0;
        allocationBytes = 0;
        collectionCount = 0;
    }

    static int AllocateObject(int kind, int permanent)
    {
        if (freeHead == 0)
        {
            return Bad("managed heap handle limit");
        }
        int handle = freeHead;
        freeHead = objects[handle].parent;
        objects[handle] = default(Obj);
        objects[handle].kind = kind;
        objects[handle].permanent = permanent;
        allocationsSinceCollection++;
        return handle;
    }

    static void MarkHandle(int handle, ref int tail)
    {
        if (handle <= 0 || handle >= objects.Length || objects[handle].kind == 0 || objects[handle].mark != 0)
        {
            return;
        }
        objects[handle].mark = 1;
        markQueue[tail] = handle;
        tail++;
    }

    static void MarkValue(Val value, ref int tail)
    {
        if (value.kind == 2 || value.kind == 3 || value.kind == 4 || value.kind == 5 || value.kind == 8)
        {
            MarkHandle(value.data, ref tail);
        }
    }

    static void CollectIfNeeded()
    {
        if (allocationsSinceCollection < 1024 && allocationBytes < 16 * 1024 && freeHead != 0)
        {
            return;
        }
        Collect();
    }

    static void Collect()
    {
        int tail = 0;
        for (int i = 0; i < memory.Length; i++)
        {
            MarkValue(memory[i], ref tail);
        }
        for (int i = 1; i < objects.Length; i++)
        {
            if (objects[i].kind != 0 && objects[i].permanent != 0)
            {
                MarkHandle(i, ref tail);
            }
        }
        MarkHandle(output, ref tail);
        for (int head = 0; head < tail; head++)
        {
            int handle = markQueue[head];
            if (objects[handle].kind == 5)
            {
                MarkHandle(objects[handle].parent, ref tail);
                MarkHandle(objects[handle].array, ref tail);
            }
            else if (objects[handle].kind != 2)
            {
                for (int i = 0; i < objects[handle].count; i++)
                {
                    MarkValue(objects[handle].values[i], ref tail);
                }
            }
        }
        for (int i = 1; i < objects.Length; i++)
        {
            if (objects[i].kind == 0)
            {
                continue;
            }
            if (objects[i].mark != 0)
            {
                objects[i].mark = 0;
                continue;
            }
            if (objects[i].kind == 3)
            {
                totalElements -= objects[i].count;
            }
            if (objects[i].values != null)
            {
                Host.ReleaseVals(objects[i].values);
            }
            if (objects[i].chars != null)
            {
                Host.ReleaseChars(objects[i].chars);
            }
            objects[i] = default(Obj);
            objects[i].parent = freeHead;
            freeHead = i;
        }
        allocationsSinceCollection = 0;
        allocationBytes = 0;
        collectionCount++;
        Host.Collected(collectionCount);
    }
}
