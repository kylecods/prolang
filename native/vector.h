#ifndef BASE_VECTOR_H
#define BASE_VECTOR_H

#include "arena.h"

#define VEC_INIT_T(vec,arena,T) Vec_Init((vec),(arena),sizeof(T),alignof(T))

typedef struct _vector
{
    void *items;
    UINT64 count;
    UINT64 capacity;

    UINT64 itemSize;
    UINT64 itemAlign;
    Arena *arena;
}Vector;


static B32 Vec_Push(Vector *vec, const void *item);
static void* Vec_Pop(Vector *vec);
static void Vec_Init(Vector *vec, Arena *arena, UINT64 itemSize, UINT64 itemAlign);


#endif