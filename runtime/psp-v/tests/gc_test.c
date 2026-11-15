#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
static void *foreign_alloc(size_t size) { return malloc(size); }
#define PRL_GC_TEST
#include "../gc.h"
/* Collection is disabled in this isolated index test (no PSP stack bounds).
 * The emulator tests exercise actual stack/register marking and sweeping. */
unsigned char _fdata[8], _end[8];

static void verify_index(void) {
    for (size_t i = 0; i < prl_gc_count; i++) {
        size_t slot = prl_gc_lookup_slot(prl_gc_blocks[i].p);
        assert(prl_gc_lookup[slot].p == prl_gc_blocks[i].p);
        assert(prl_gc_lookup[slot].index == i);
    }
}
static void verify_mark_range(void) {
    qsort(prl_gc_blocks, prl_gc_count, sizeof(*prl_gc_blocks), prl_gc_compare);
    prl_gc_reindex();
    for (size_t i = 0; i < prl_gc_count; i++) prl_gc_blocks[i].marked = 0;
    prl_gc_mark(0);
    prl_gc_mark(UINTPTR_MAX);
    PrlGcBlock *last = &prl_gc_blocks[prl_gc_count - 1];
    prl_gc_mark((uintptr_t)last->p + last->size);
    for (size_t i = 0; i < prl_gc_count; i++) {
        assert(!prl_gc_blocks[i].marked);
        prl_gc_mark((uintptr_t)prl_gc_blocks[i].p + prl_gc_blocks[i].size - 1);
        assert(prl_gc_blocks[i].marked);
    }
}
int main(void) {
    unsigned *pointers[4096] = {0};
    for (unsigned i = 0; i < 4096; i++) {
        pointers[i] = malloc(32);
        assert(pointers[i]);
        pointers[i][0] = i;
    }
    assert(prl_gc_count == 4096);
    verify_index();
    verify_mark_range();
    for (unsigned i = 0; i < 4096; i += 7) {
        pointers[i] = realloc(pointers[i], 64);
        assert(pointers[i] && pointers[i][0] == i);
    }
    for (unsigned i = 0; i < 4096; i += 2) { free(pointers[i]); pointers[i] = NULL; }
    verify_index();
    assert(prl_gc_count == 2048);
    for (unsigned pass = 0; pass < 8; pass++) {
        for (unsigned i = 0; i < 4096; i += 2) {
            pointers[i] = calloc(8, sizeof(unsigned));
            assert(pointers[i] && pointers[i][0] == 0);
            pointers[i][0] = i;
        }
        /* The collector sorts descriptors before marking and compacts them on
         * sweep. Neither is allowed to leave stale indices in the hash table. */
        qsort(prl_gc_blocks, prl_gc_count, sizeof(*prl_gc_blocks), prl_gc_compare);
        prl_gc_reindex();
        verify_index();
        for (unsigned i = 0; i < 4096; i += 2) { free(pointers[i]); pointers[i] = NULL; }
        verify_index();
    }
    for (unsigned i = 1; i < 4096; i += 2) {
        assert(pointers[i][0] == i);
        free(pointers[i]);
    }
    assert(prl_gc_count == 0 && prl_gc_bytes == 0);
    void *foreign = foreign_alloc(32);
    foreign = realloc(foreign, 64);
    assert(foreign);
    free(foreign);
#undef malloc
#undef calloc
#undef realloc
#undef free
    free(prl_gc_blocks);
    free(prl_gc_lookup);
    puts("GC INDEX PASS");
}
