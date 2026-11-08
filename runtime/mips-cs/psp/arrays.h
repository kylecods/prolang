#ifndef PVM_ARRAYS_H
#define PVM_ARRAYS_H
#include <stdint.h>
#include <stddef.h>
#include <stdlib.h>
/* Translated C# arrays point just past a length header. The union keeps element
 * data aligned, including on hosts where pointers are wider than on the PSP. */
typedef union pvm_array_header pvm_array_header;
union pvm_array_header { struct { int length; size_t bytes; pvm_array_header *prev, *next; } info; uint64_t align; void *pointer_align; };
static pvm_array_header *pvm_array_first;
static void *pvm_input;
static size_t pvm_array_bytes, pvm_array_peak, pvm_array_blocks;
static unsigned pvm_collections;
static void *pvm_array(int32_t length, size_t element_size) {
    if (length < 0 || (size_t)length > (SIZE_MAX - sizeof(pvm_array_header)) / element_size) {
        Host_Fail("invalid array length");
    }
    pvm_array_header *header = calloc(1, sizeof(*header) + (size_t)length * element_size);
    if (!header) { Host_Fail("array allocation failed"); }
    header->info.next = pvm_array_first;
    if (pvm_array_first) { pvm_array_first->info.prev = header; }
    pvm_array_first = header;
    header->info.length = length;
    header->info.bytes = (size_t)length * element_size;
    pvm_array_bytes += header->info.bytes;
    pvm_array_blocks++;
    if (pvm_array_bytes > pvm_array_peak) { pvm_array_peak = pvm_array_bytes; }
    return header + 1;
}
static int32_t pvm_length(const void *array) {
    if (!array) { Host_Fail("null array"); }
    return ((const pvm_array_header *)array - 1)->info.length;
}
static void *pvm_at(void *array, int32_t index, size_t element_size) {
    if ((uint32_t)index >= (uint32_t)pvm_length(array)) { Host_Fail("array index out of bounds"); }
    return (unsigned char *)array + (size_t)index * element_size;
}
static void pvm_array_free(void *array) {
    if (array) {
        pvm_array_header *header = (pvm_array_header *)array - 1;
        if (header->info.prev) { header->info.prev->info.next = header->info.next; } else { pvm_array_first = header->info.next; }
        if (header->info.next) { header->info.next->info.prev = header->info.prev; }
        pvm_array_bytes -= header->info.bytes; pvm_array_blocks--; free(header);
    }
}
#endif
