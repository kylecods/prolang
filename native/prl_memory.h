#ifndef PRL_MEMORY_H
#define PRL_MEMORY_H

#include "arena.h"

/* Global arena for all ProLang runtime allocations.
   Programs have no free/GC — arena reset happens at process exit.
   The arena starts zero-initialized; prl_arena_ensure() lazily allocates
   the backing buffer on first use (works for both desktop and PSP). */
static Arena prl_arena;
static int prl_arena_inited = 0;

static inline void prl_arena_init(void) {
    if (!prl_arena_inited) {
        prl_arena = Make_Malloc_Arena();
        prl_arena_inited = 1;
    }
}

/* Allocate from the global arena, initializing it on first use. */
static inline void *prl_alloc(UINT64 size) {
    prl_arena_init();
    return Arena_Push(&prl_arena, size);
}
static inline void *prl_alloc_zero(UINT64 size) {
    prl_arena_init();
    return Arena_Push_Zero(&prl_arena, size);
}

#endif /* PRL_MEMORY_H */