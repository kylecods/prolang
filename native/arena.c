#include "arena.h"

static Arena
    Make_Arena_Reserve(BaseMemory *base, UINT64 reserve_size)
{
    Arena result = {0};

    result.base = base;
    result.memory = base->reserve(base->ctx,reserve_size);
    result.capacity = reserve_size;

    return(result);
}

static Arena Make_Arena(BaseMemory *base)
{
    Arena result = Make_Arena_Reserve(base, DEFAULT_RESERVE);
    return(result);
}

static void Arena_Release(Arena *arena)
{
    BaseMemory *base = arena->base;
    base->release(base->ctx,arena->memory,arena->capacity);
}

void *Arena_Push(Arena *arena, UINT64 size)
{
    void *result = 0;
    if(arena->pos + size <= arena->capacity){
        result = arena->memory + arena->pos;
        arena->pos += size;

        UINT64 p = arena->pos;
        UINT64 commit_p = arena->commit_pos;

        if(p > commit_p){
           UINT64 p_aligned = AlignUpPow2(p, COMMIT_BLOCK_SIZE - 1);
           UINT64 next_commit_pos_clamped = ClampTop(p_aligned,arena->capacity);
           UINT64 commit_size = next_commit_pos_clamped - commit_p;

           BaseMemory *base = arena->base;
           base->commit(base->ctx,arena->memory + commit_p, commit_size);

           arena->commit_pos = next_commit_pos_clamped;
        }
    }

    return (result);
}

static void Arena_Pop_To(Arena *arena, UINT64 pos)
{
    if(pos < arena->pos){
        arena->pos = pos;

        UINT64 p = arena->pos;
        UINT64 p_aligned = AlignUpPow2(p, COMMIT_BLOCK_SIZE - 1);
        UINT64 next_commit_pos_clamped = ClampTop(p_aligned,arena->capacity);

        UINT64 commit_p = arena->commit_pos;
        if(next_commit_pos_clamped < commit_p){
            UINT64 decommit_size = commit_p - next_commit_pos_clamped;

            BaseMemory *base = arena->base;
            base->decommit(base->ctx, arena->memory + commit_p, decommit_size);

            arena->commit_pos = next_commit_pos_clamped;
        }
    }
}

static void *Arena_Push_Zero(Arena *arena, UINT64 size)
{
}

static void Arena_Align(Arena *arena, UINT64 pow2Align)
{
    UINT64 p = arena->pos;
    UINT64 p_aligned = AlignUpPow2(p,pow2Align);
    UINT64 z = p_aligned - p;
    if(z > 0) {
        Arena_Push(arena,z);
    }
}
