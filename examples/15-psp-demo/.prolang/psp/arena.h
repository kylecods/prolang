#ifndef ARENA_H
#define ARENA_H

#include "base.h"
#include <stdlib.h>

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

#define DEFAULT_RESERVE MB(1)
#define COMMIT_BLOCK_SIZE KB(64)

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
    result.memory = base->reserve(base->ctx, reserve_size);
    result.capacity = reserve_size;
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

static inline void *Arena_Push(Arena *arena, UINT64 size) {
    void *result = 0;
    if (arena->pos + size <= arena->capacity) {
        result = arena->memory + arena->pos;
        arena->pos += size;

        UINT64 p = arena->pos;
        UINT64 commit_p = arena->commit_pos;

        if (p > commit_p) {
            UINT64 p_aligned = AlignUpPow2(p, COMMIT_BLOCK_SIZE - 1);
            UINT64 next_commit_pos_clamped = ClampTop(p_aligned, arena->capacity);
            UINT64 commit_size = next_commit_pos_clamped - commit_p;

            BaseMemory *base = arena->base;
            base->commit(base->ctx, arena->memory + commit_p, commit_size);
            arena->commit_pos = next_commit_pos_clamped;
        }
    }
    return result;
}

static inline void Arena_Pop_To(Arena *arena, UINT64 pos) {
    if (pos < arena->pos) {
        arena->pos = pos;
        UINT64 p = arena->pos;
        UINT64 p_aligned = AlignUpPow2(p, COMMIT_BLOCK_SIZE - 1);
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