#include "vector.h"

static B32 Ensure_Capacity_Vec(Vector *vec)
{
    if(vec->capacity == vec->count) {
        UINT64 newCapacity = vec->capacity * 2;
        void *newItems = push_array(vec->arena,void*,newCapacity * vec->itemSize);

        if(!newItems){
            return False;
        }

        if(vec->count > 0)
        {
            memcpy(newItems,vec->items, vec->count * vec->itemSize);
        }
        vec->capacity = newCapacity;
        vec->items = newItems;

        return True;
    }

    return True;
}

static B32 Vec_Push(Vector *vec, const void *item)
{
   if(Ensure_Capacity_Vec(vec)){
     UINT64 count = vec->count;

     UINT8 *dst = (UINT8*)(vec->items) + (vec->itemSize * count);
     memcpy(dst,item,vec->itemSize);

     vec->count++;

     return True;
   }
   return False;
}

static void *Vec_Pop(Vector *vec)
{
    if(vec->count == 0) {
        return NULL;
    }

    vec->count--;

    UINT8 *base=(UINT8*)vec->items;

    return base + (vec->itemSize * vec->count);
}

static void Vec_Init(Vector *vec, Arena *arena, UINT64 itemSize, UINT64 itemAlign)
{
    vec->arena = arena;
    vec->capacity = 16;
    vec->count = 0;

    vec->itemSize = itemSize;
    vec->itemAlign = itemAlign;

    vec->items = push_array(arena, void*, itemSize * vec->capacity);
}
