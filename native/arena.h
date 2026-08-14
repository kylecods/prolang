#ifndef ARENA_H
#define ARENA_H

#include "base.h"

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

#define DEFAULT_RESERVE GB(1)
#define COMMIT_BLOCK_SIZE MB(64)

static void Change_Memory_Noop(void *ctx, void *ptr, UINT64 size);


static Arena Make_Arena_Reserve(BaseMemory *base, UINT64 reserve_size);

static Arena Make_Arena(BaseMemory *base);

static void Arena_Release(Arena* arena);

static void *Arena_Push(Arena *arena, UINT64 size);

static void Arena_Pop_To(Arena *arena, UINT64 pos);

static void* Arena_Push_Zero(Arena *arena,UINT64 size);
static void Arena_Align(Arena *arena, UINT64 pow2Align);

#define push_array(a,T,c) (T*)Arena_Push((a), sizeof(T) * (c))

#endif