#ifndef PROLANG_MSIL_GC_H
#define PROLANG_MSIL_GC_H
/* Single-threaded conservative collector for V's generated C on PSP. Only
 * allocations made by the interpreter are tracked. The descriptor table is
 * deliberately outside that heap so it cannot keep its own objects alive.
 * Interior pointers (array slices and managed field addresses) are roots too.
 * Collection runs with the GE synchronized, and never from the HOME callback. */
#include <stdlib.h>
#include <stdint.h>
#include <setjmp.h>
#include <string.h>
#if defined(__PSP__) || defined(PRL_GC_TEST)
typedef struct { void *p; size_t size; unsigned marked; } PrlGcBlock;
typedef struct { void *p; size_t index; } PrlGcEntry;
static PrlGcBlock *prl_gc_blocks;
/* This table, like the block descriptors, is allocated outside the managed
 * heap. Index updates cover both swap removal and collector sorting. */
static PrlGcEntry *prl_gc_lookup;
static size_t prl_gc_lookup_capacity, prl_gc_deleted;
static size_t prl_gc_count, prl_gc_capacity, prl_gc_bytes;
static size_t prl_gc_threshold = 512 * 1024;
static unsigned prl_gc_cycles;
static unsigned char *prl_gc_stack_end;
extern unsigned char _fdata[], _end[];
static size_t prl_gc_hash(void *p) {
    return ((uintptr_t)p >> 3) * (uintptr_t)2654435761u;
}
static size_t prl_gc_lookup_slot(void *p) {
    size_t slot = prl_gc_hash(p) & (prl_gc_lookup_capacity - 1);
    while (prl_gc_lookup[slot].p && prl_gc_lookup[slot].p != p)
        slot = (slot + 1) & (prl_gc_lookup_capacity - 1);
    return slot;
}
static void prl_gc_insert(void *p, size_t index) {
    size_t slot = prl_gc_hash(p) & (prl_gc_lookup_capacity - 1);
    while (prl_gc_lookup[slot].p && prl_gc_lookup[slot].p != (void *)1)
        slot = (slot + 1) & (prl_gc_lookup_capacity - 1);
    if (prl_gc_lookup[slot].p == (void *)1) prl_gc_deleted--;
    prl_gc_lookup[slot] = (PrlGcEntry){p, index};
}
static void prl_gc_reindex(void) {
    memset(prl_gc_lookup, 0, prl_gc_lookup_capacity * sizeof(*prl_gc_lookup));
    prl_gc_deleted = 0;
    for (size_t i = 0; i < prl_gc_count; i++) prl_gc_insert(prl_gc_blocks[i].p, i);
}
static int prl_gc_reserve_lookup(size_t capacity) {
    if (capacity > prl_gc_lookup_capacity) {
        PrlGcEntry *next = calloc(capacity, sizeof(*next));
        if (!next) return 0;
        free(prl_gc_lookup);
        prl_gc_lookup = next;
        prl_gc_lookup_capacity = capacity;
    }
    prl_gc_reindex();
    return 1;
}
static int prl_gc_compare(const void *a, const void *b) {
    uintptr_t x = (uintptr_t)((const PrlGcBlock *)a)->p;
    uintptr_t y = (uintptr_t)((const PrlGcBlock *)b)->p;
    return x < y ? -1 : x > y;
}
static void prl_gc_mark(uintptr_t candidate) {
    /* Most scanned words are integers, code addresses or empty GU command
     * storage. Reject them before searching thousands of block descriptors. */
    if (!prl_gc_count || candidate < (uintptr_t)prl_gc_blocks[0].p) return;
    PrlGcBlock *last = &prl_gc_blocks[prl_gc_count - 1];
    if (candidate >= (uintptr_t)last->p + last->size) return;
    size_t lo = 0, hi = prl_gc_count;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if ((uintptr_t)prl_gc_blocks[mid].p <= candidate) lo = mid + 1;
        else hi = mid;
    }
    if (lo) {
        PrlGcBlock *b = &prl_gc_blocks[lo - 1];
        if (candidate - (uintptr_t)b->p < b->size && !b->marked) b->marked = 1;
    }
}
static void prl_gc_scan(const void *begin, const void *end) {
    uintptr_t p = ((uintptr_t)begin + sizeof(uintptr_t) - 1) & ~(sizeof(uintptr_t) - 1);
    uintptr_t stop = (uintptr_t)end;
    for (; p + sizeof(uintptr_t) <= stop; p += sizeof(uintptr_t))
        prl_gc_mark(*(const uintptr_t *)p);
}
static __attribute__((noinline)) void prl_gc_collect(void) {
    jmp_buf registers;
    volatile uintptr_t stack_anchor = 0;
    if (!prl_gc_count || !prl_gc_stack_end) return;
    /* setjmp materializes the callee-saved registers in a scanned stack object. */
    if (setjmp(registers)) return;
    qsort(prl_gc_blocks, prl_gc_count, sizeof(*prl_gc_blocks), prl_gc_compare);
    for (size_t i = 0; i < prl_gc_count; i++) prl_gc_blocks[i].marked = 0;
    prl_gc_scan((const void *)&stack_anchor, prl_gc_stack_end);
    prl_gc_scan(registers, (unsigned char *)registers + sizeof(registers));
    prl_gc_scan(_fdata, _end);
    /* Mark to a fixed point. A marked block is scanned exactly once. */
    int pending;
    do {
        pending = 0;
        for (size_t i = 0; i < prl_gc_count; i++) {
            PrlGcBlock *b = &prl_gc_blocks[i];
            if (b->marked == 1) {
                b->marked = 2;
                prl_gc_scan(b->p, (unsigned char *)b->p + b->size);
                pending = 1;
            }
        }
    } while (pending);
    size_t kept = 0;
    prl_gc_bytes = 0;
    for (size_t i = 0; i < prl_gc_count; i++) {
        PrlGcBlock b = prl_gc_blocks[i];
        if (b.marked) { prl_gc_blocks[kept++] = b; prl_gc_bytes += b.size; }
        else free(b.p);
    }
    prl_gc_count = kept;
    prl_gc_reindex();
    prl_gc_threshold = prl_gc_bytes + 512 * 1024;
    prl_gc_cycles++;
}
static void *prl_gc_malloc(size_t size) {
    if (prl_gc_bytes + size > prl_gc_threshold) prl_gc_collect();
    void *p = malloc(size ? size : 1);
    if (!p) { prl_gc_collect(); p = malloc(size ? size : 1); }
    if (!p) return NULL;
    if (prl_gc_count == prl_gc_capacity) {
        size_t cap = prl_gc_capacity ? prl_gc_capacity * 2 : 1024;
        void *next = realloc(prl_gc_blocks, cap * sizeof(*prl_gc_blocks));
        if (!next) { free(p); return NULL; }
        prl_gc_blocks = next;
        prl_gc_capacity = cap;
    }
    prl_gc_blocks[prl_gc_count++] = (PrlGcBlock){p, size ? size : 1, 0};
    if (prl_gc_count * 2 >= prl_gc_lookup_capacity) {
        size_t cap = prl_gc_lookup_capacity ? prl_gc_lookup_capacity * 2 : 2048;
        if (!prl_gc_reserve_lookup(cap)) { prl_gc_count--; free(p); return NULL; }
    } else if (prl_gc_deleted > prl_gc_lookup_capacity / 4) {
        prl_gc_reindex();
    } else prl_gc_insert(p, prl_gc_count - 1);
    prl_gc_bytes += size ? size : 1;
    return p;
}
static void prl_gc_free(void *p) {
    if (!p) return;
    if (prl_gc_lookup_capacity) {
        size_t slot = prl_gc_lookup_slot(p);
        if (prl_gc_lookup[slot].p == p) {
            size_t i = prl_gc_lookup[slot].index;
            prl_gc_bytes -= prl_gc_blocks[i].size;
            prl_gc_lookup[slot].p = (void *)1;
            prl_gc_deleted++;
            prl_gc_count--;
            if (i != prl_gc_count) {
                prl_gc_blocks[i] = prl_gc_blocks[prl_gc_count];
                prl_gc_lookup[prl_gc_lookup_slot(prl_gc_blocks[i].p)].index = i;
            }
            free(p); return;
        }
    }
    /* V also frees C-owned buffers, which were allocated before these macros. */
    free(p);
}
static void *prl_gc_calloc(size_t count, size_t size) {
    if (size && count > SIZE_MAX / size) return NULL;
    void *p = prl_gc_malloc(count * size);
    if (p) memset(p, 0, count * size);
    return p;
}
static void *prl_gc_realloc(void *p, size_t size) {
    if (!p) return prl_gc_malloc(size);
    if (prl_gc_lookup_capacity) {
        size_t slot = prl_gc_lookup_slot(p);
        if (prl_gc_lookup[slot].p == p) {
            size_t i = prl_gc_lookup[slot].index;
            size_t old = prl_gc_blocks[i].size;
            /* Keep p on this stack while malloc may collect and reorder blocks. */
            void *next = prl_gc_malloc(size);
            if (!next) return NULL;
            memcpy(next, p, old < size ? old : size);
            prl_gc_free(p);
            return next;
        }
    }
    return realloc(p, size);
}
#define malloc prl_gc_malloc
#define calloc prl_gc_calloc
#define realloc prl_gc_realloc
#define free prl_gc_free
#endif
#endif
