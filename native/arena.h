#ifndef ARENA_H
#define ARENA_H

#include "base.h"
#include <stdlib.h>
#include <stdio.h>

typedef void* Reserve_Func(void *ctx, UINT64 size);
typedef void Change_Memory_Func(void  *ctx, void *ptr, UINT64 size);

typedef struct
{
    Reserve_Func *reserve;
    Change_Memory_Func *commit;
    Change_Memory_Func *decommit;
    Change_Memory_Func *release;
    void *ctx;
} BaseMemory;

typedef struct {
    BaseMemory *base;
    UINT8 *memory;
    UINT64 capacity;
    UINT64 pos;
    UINT64 commit_pos;
}Arena;

/* The whole ProLang heap. Nothing is ever freed — see prl_memory.h — so this is the total a
   program may allocate across every array, every string concatenation, and every class it creates.
   1MB was survivable while only string building and `new array` reached the heap; reference types
   make allocation ordinary. The PSP keeps a smaller figure because its entire heap is 8MB. */
#if defined(__PSP__)
#  define DEFAULT_RESERVE MB(4)
#else
#  define DEFAULT_RESERVE MB(16)
#endif

#define COMMIT_BLOCK_SIZE KB(64)

/* Every allocation is aligned to this. Arena_Push used to hand back memory + pos with no alignment
   at all, so a one-byte allocation — prl_string_char_at makes exactly those — left every following
   block misaligned. On x86 that costs a little speed; on the PSP's MIPS core an unaligned pointer
   load faults. It survived because the heap held mostly 4-byte array elements and byte strings;
   pointer-carrying records make it routine. */
#define ARENA_ALIGNMENT ((UINT64)sizeof(void*))

static void Change_Memory_Noop(void *ctx, void *ptr, UINT64 size) {
    (void)ctx; (void)ptr; (void)size;
}

/* Malloc-based memory backend for platforms without virtual memory (PSP, embedded). */
static void* Malloc_Reserve(void *ctx, UINT64 size) {
    (void)ctx; return malloc((size_t)size);
}
static void Malloc_Release(void *ctx, void *ptr, UINT64 size) {
    (void)ctx; (void)size; free(ptr);
}
static BaseMemory MallocBaseMemory = {
    .reserve   = Malloc_Reserve,
    .commit    = Change_Memory_Noop,
    .decommit  = Change_Memory_Noop,
    .release   = Malloc_Release,
    .ctx       = NULL,
};

static inline Arena Make_Arena_Reserve(BaseMemory *base, UINT64 reserve_size) {
    Arena result = {0};
    result.base = base;
    result.memory = (UINT8*)base->reserve(base->ctx, reserve_size);
    result.capacity = reserve_size;

    if (!result.memory) {
        fprintf(stderr, "prolang: could not reserve %llu bytes for the runtime arena.\n",
                (unsigned long long)reserve_size);
        abort();
    }

    return result;
}

static inline Arena Make_Arena(BaseMemory *base) {
    return Make_Arena_Reserve(base, DEFAULT_RESERVE);
}

static inline Arena Make_Malloc_Arena(void) {
    return Make_Arena_Reserve(&MallocBaseMemory, DEFAULT_RESERVE);
}

static inline void Arena_Release(Arena *arena) {
    BaseMemory *base = arena->base;
    base->release(base->ctx, arena->memory, arena->capacity);
}

/* Aborts rather than returning NULL on exhaustion.

   Not one caller checked the old NULL return: prl_array_new_* stored it straight into an array's
   data pointer and prl_string_concat into a string's, so the program carried on and died later at
   an unrelated read, with nothing connecting the crash to the allocation that failed. A message
   naming the arena is the difference between a five-minute fix and an afternoon. */
static inline void Arena_Fail(UINT64 requested, UINT64 pos, UINT64 capacity) {
    fprintf(stderr,
            "prolang: out of arena memory (requested %llu bytes, %llu of %llu used).\n"
            "ProLang programs never free: every array, string and class allocation lives until the\n"
            "process exits. Raise DEFAULT_RESERVE in native/arena.h if the program genuinely needs\n"
            "this much.\n",
            (unsigned long long)requested,
            (unsigned long long)pos,
            (unsigned long long)capacity);
    abort();
}

static inline void *Arena_Push(Arena *arena, UINT64 size) {
    /* Align the cursor before handing anything out, so every block is suitably aligned for a
       pointer regardless of what odd-sized allocation came before it. */
    UINT64 aligned_pos = AlignUpPow2(arena->pos, ARENA_ALIGNMENT);

    if (aligned_pos + size > arena->capacity) {
        Arena_Fail(size, arena->pos, arena->capacity);
        return 0;
    }

    void *result = arena->memory + aligned_pos;
    arena->pos = aligned_pos + size;

    UINT64 p = arena->pos;
    UINT64 commit_p = arena->commit_pos;

    if (p > commit_p) {
        UINT64 p_aligned = AlignUpPow2(p, COMMIT_BLOCK_SIZE);
        UINT64 next_commit_pos_clamped = ClampTop(p_aligned, arena->capacity);
        UINT64 commit_size = next_commit_pos_clamped - commit_p;

        BaseMemory *base = arena->base;
        base->commit(base->ctx, arena->memory + commit_p, commit_size);
        arena->commit_pos = next_commit_pos_clamped;
    }

    return result;
}

static inline void Arena_Pop_To(Arena *arena, UINT64 pos) {
    if (pos < arena->pos) {
        arena->pos = pos;
        UINT64 p = arena->pos;
        /* AlignUpPow2 takes the alignment, not alignment-1; passing KB(64)-1 rounded to a mask that
           is not a power of two. Harmless while the only backend's commit is a no-op, wrong all the
           same. */
        UINT64 p_aligned = AlignUpPow2(p, COMMIT_BLOCK_SIZE);
        UINT64 next_commit_pos_clamped = ClampTop(p_aligned, arena->capacity);
        UINT64 commit_p = arena->commit_pos;
        if (next_commit_pos_clamped < commit_p) {
            UINT64 decommit_size = commit_p - next_commit_pos_clamped;
            BaseMemory *base = arena->base;
            base->decommit(base->ctx, arena->memory + commit_p, decommit_size);
            arena->commit_pos = next_commit_pos_clamped;
        }
    }
}

static inline void *Arena_Push_Zero(Arena *arena, UINT64 size) {
    UINT8 *p = (UINT8*)Arena_Push(arena, size);
    if (p) {
        UINT64 i;
        for (i = 0; i < size; i++) p[i] = 0;
    }
    return p;
}

static inline void Arena_Align(Arena *arena, UINT64 pow2Align) {
    UINT64 p = arena->pos;
    UINT64 p_aligned = AlignUpPow2(p, pow2Align);
    UINT64 z = p_aligned - p;
    if (z > 0) {
        Arena_Push(arena, z);
    }
}

#define push_array(a,T,c) (T*)Arena_Push((a), sizeof(T) * (c))

#endif /* ARENA_H */