#ifndef PROLANG_CSHARP_PSP_HOST
#define PROLANG_CSHARP_PSP_HOST
#include <stdlib.h>
#include <stdint.h>
#include <stdbool.h>
#include <setjmp.h>
#include "../../psp-v/host.h"
static jmp_buf pvm_failure;
static int pvm_code_bytes;
static int32_t VM_steps;
static void Host_Fail(const char *s) {
    prl_host_write(s, (int)strlen(s)); prl_host_write("\n", 1);
    longjmp(pvm_failure, 1);
}
#include "arrays.h"
/* Executable code is owned by the native host. Slots never move: generated MIPS
 * calls indirect through their addresses, including forward references. */
#define PVM_METHOD_LIMIT 65536
static void *pvm_methods[PVM_METHOD_LIMIT];
static int32_t Host_MethodSlot(int mid) {
    if ((unsigned)mid >= PVM_METHOD_LIMIT) { Host_Fail("method index out of bounds"); }
    return (int32_t)(intptr_t)&pvm_methods[mid];
}
static int32_t Host_Counter(void) { return (int32_t)(intptr_t)&VM_steps; }
static void pvm_code_ready(int mid, int name, uint32_t *words, int count, const uint8_t *image) {
    if ((unsigned)mid >= PVM_METHOD_LIMIT || count < 0 || count > pvm_length(words) ||
        name < 0 || name >= pvm_length(image)) { Host_Fail("invalid generated code"); }
    size_t bytes = (size_t)count * sizeof(uint32_t);
    if (bytes > INT32_MAX) { Host_Fail("generated code too large"); }
    void *code = prl_host_jit_alloc((int)bytes);
    if (!code) { Host_Fail("code allocation failed"); }
    memcpy(code, words, bytes);
    pvm_methods[mid] = code;
    prl_host_jit_flush(code, (int)bytes);
    pvm_code_bytes += (int)bytes;
    const char *method_name = (const char *)image + name;
    if (!memchr(method_name, 0, (size_t)(pvm_length(image) - name))) { Host_Fail("invalid method name"); }
    if (prl_log) { fprintf(prl_log, "METHOD %d %s code_bytes=%d\n", mid, method_name, (int)bytes); }
    FILE *f = prl_test_frames ? fopen("jit-mips.bin", mid == 0 ? "wb" : "ab") : NULL;
    if (f) { fwrite(code, 1, bytes, f); fclose(f); }
}
#define Host_CodeReady(mid, name, words, count) pvm_code_ready((mid), (name), (words), (count), VM_image)
static void Host_ReleaseVals(void *values) { pvm_array_free(values); }
static void Host_ReleaseChars(void *chars) { pvm_array_free(chars); }
static void Host_ReleaseInts(void *values) { pvm_array_free(values); }
static void Host_ReleaseWords(void *values) { pvm_array_free(values); }
static void Host_Collected(int count) { (void)count; pvm_collections++; }
static void Host_Reset(void) {
    pvm_array_header *node = pvm_array_first;
    while (node) { pvm_array_header *next = node->info.next; if ((void *)(node + 1) != pvm_input) { pvm_array_free(node + 1); } node = next; }
    pvm_collections = 0; pvm_array_peak = pvm_array_bytes;
    for (int i = 0; i < PVM_METHOD_LIMIT; i++) { free(pvm_methods[i]); pvm_methods[i] = NULL; }
    pvm_code_bytes = 0;
}
static void pvm_trap(int reason) {
    Host_Fail(reason == 1 ? "expected an int32 value" : reason == 3 ? "expected non-null array" : reason == 10 ? "instruction budget exceeded" : reason == 11 ? "array index out of bounds" : reason == 12 ? "division by zero" : reason == 13 ? "int32 division overflow" : "JIT value kind check failed");
}
/* Implemented after generated definitions, where Val and VM_memory are known. */
static int32_t Host_Helper(int number);
static int32_t Host_GlobalSlot(int index);
static void Host_Call(int mid, int frame, void *result);
static int Host_Length(const char *s) { if (!s) { Host_Fail("null string"); } return (int)strlen(s); }
static uint16_t pvm_string_at(const char *s, int index) { if (index < 0 || index >= Host_Length(s)) { Host_Fail("string index out of bounds"); } return (unsigned char)s[index]; }
static void Host_Ascii(const char *s, uint16_t *p, int n) { if (n < 0 || n > pvm_length(p) || (size_t)n > strlen(s)) { Host_Fail("invalid ASCII buffer"); } for (int i = 0; i < n; i++) { p[i] = (unsigned char)s[i]; } }
static int Host_Number(int n, bool uns, uint16_t *p) { char s[16]; int length = uns ? snprintf(s, sizeof(s), "%u", (unsigned)n) : snprintf(s, sizeof(s), "%d", n); if (length > pvm_length(p)) { Host_Fail("invalid number buffer"); } for (int i = 0; i < length; i++) { p[i] = (unsigned char)s[i]; } return length; }
static char *pvm_utf8(const uint16_t *p, int n, int *size) {
    char *s = malloc((size_t)n * 3 + 1); if (!s) { Host_Fail("UTF-8 allocation failed"); } int at = 0;
    for (int i = 0; i < n; i++) {
        unsigned c = p[i];
        if (c >= 0xd800 && c <= 0xdbff && i + 1 < n && p[i+1] >= 0xdc00 && p[i+1] <= 0xdfff) { c = 0x10000 + ((c - 0xd800) << 10) + p[++i] - 0xdc00; }
        else if (c >= 0xd800 && c <= 0xdfff) { c = 0xfffd; }
        if (c < 128) { s[at++] = (char)c; }
        else if (c < 2048) { s[at++] = (char)(0xc0 | (c >> 6)); s[at++] = (char)(0x80 | (c & 63)); }
        else if (c < 65536) { s[at++] = (char)(0xe0 | (c >> 12)); s[at++] = (char)(0x80 | ((c >> 6) & 63)); s[at++] = (char)(0x80 | (c & 63)); }
        else { s[at++] = (char)(0xf0 | (c >> 18)); s[at++] = (char)(0x80 | ((c >> 12) & 63)); s[at++] = (char)(0x80 | ((c >> 6) & 63)); s[at++] = (char)(0x80 | (c & 63)); }
    }
    s[at] = 0; *size = at; return s;
}
static void Host_Write(const uint16_t *p, int n) { if (n < 0 || n > pvm_length(p)) { Host_Fail("invalid text buffer"); } int size; char *s = pvm_utf8(p, n, &size); prl_host_write(s, size); free(s); }
static void Host_Init(void) { prl_host_psp_init(); }
static void Host_Clear(int c) { prl_host_clear(c); }
static void Host_Rect(int x,int y,int w,int h,int c) { prl_host_rect(x,y,w,h,c); }
static void Host_Line(int x,int y,int xx,int yy,int c) { prl_host_line(x,y,xx,yy,c); }
static void Host_Text(int x,int y,const uint16_t *p,int n,int c) { if (n < 0 || n > pvm_length(p)) { Host_Fail("invalid text buffer"); } int size; char *s = pvm_utf8(p,n,&size); prl_host_text(x,y,s,size,c); free(s); }
static void Host_Present(int steps,int calls) { prl_host_perf(steps,calls); prl_host_swap(); }
static void Host_CollectStats(void) {
    if (prl_log && prl_test_frames) {
        fprintf(prl_log, "HEAP bytes=%u blocks=%u collections=%u peak=%u\n", (unsigned)pvm_array_bytes, (unsigned)pvm_array_blocks, pvm_collections, (unsigned)pvm_array_peak);
        fflush(prl_log);
    }
}
static int Host_Millis(void) { return prl_host_millis(); }
static void Host_Sleep(int n) { prl_host_sleep(n); }
static void Host_Vsync(void) { prl_host_vsync(); }
static int Host_Buttons(void) { return prl_host_buttons(); }
#endif
