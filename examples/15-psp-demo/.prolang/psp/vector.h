#ifndef BASE_VECTOR_H
#define BASE_VECTOR_H

#include <string.h>
#include "arena.h"

#define VEC_INIT_T(vec,arena,T) Vec_Init((vec),(arena),sizeof(T),alignof(T))

typedef struct _vector {
    void *items;
    UINT64 count;
    UINT64 capacity;
    UINT64 itemSize;
    UINT64 itemAlign;
    Arena *arena;
} Vector;

static inline B32 Ensure_Capacity_Vec(Vector *vec) {
    if (vec->capacity == vec->count) {
        UINT64 newCapacity = vec->capacity ? vec->capacity * 2 : 16;
        void *newItems = push_array(vec->arena, void*, newCapacity * vec->itemSize);
        if (!newItems) return False;
        if (vec->count > 0)
            __builtin_memcpy(newItems, vec->items, vec->count * vec->itemSize);
        vec->capacity = newCapacity;
        vec->items = newItems;
        return True;
    }
    return True;
}

static inline B32 Vec_Push(Vector *vec, const void *item) {
    if (Ensure_Capacity_Vec(vec)) {
        UINT8 *dst = (UINT8*)(vec->items) + (vec->itemSize * vec->count);
        __builtin_memcpy(dst, item, vec->itemSize);
        vec->count++;
        return True;
    }
    return False;
}

static inline void *Vec_Pop(Vector *vec) {
    if (vec->count == 0) return NULL;
    vec->count--;
    return (UINT8*)vec->items + (vec->itemSize * vec->count);
}

static inline void Vec_Init(Vector *vec, Arena *arena, UINT64 itemSize, UINT64 itemAlign) {
    vec->arena = arena;
    vec->capacity = 16;
    vec->count = 0;
    vec->itemSize = itemSize;
    vec->itemAlign = itemAlign;
    vec->items = push_array(arena, void*, itemSize * vec->capacity);
}

#endif /* BASE_VECTOR_H */