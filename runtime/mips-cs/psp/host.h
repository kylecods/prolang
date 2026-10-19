#ifndef PROLANG_CSHARP_PSP_HOST
#define PROLANG_CSHARP_PSP_HOST
#include <stdlib.h>
#include <stdint.h>
#include <stdbool.h>
#include <setjmp.h>
/* Immutable image/metadata/code are outside the managed collector. Only the explicitly
 * registered value stack and real managed objects are traced. This avoids scanning the
 * entire decoder and compilation working set at every collection. */
static void *pvm_permanent(int n) { return calloc(1, n > 0 ? (size_t)n : 1); }
static void pvm_release(void *p) { free(p); }
#include "../../psp-v/host.h"
static jmp_buf pvm_failure;
static int pvm_code_bytes;
static int pvm_natural_gc;
static int32_t VM_steps;
static void Host_Fail(const char *s) {
    prl_host_write(s, (int)strlen(s)); prl_host_write("\n", 1);
    longjmp(pvm_failure, 1);
}
static void *Host_Perm(int n) { void *p = pvm_permanent(n); if (!p) Host_Fail("permanent allocation failed"); return p; }
static void *Host_Alloc(int n) { void *p = calloc(1, n > 0 ? (size_t)n : 1); if (!p) Host_Fail("managed allocation failed"); return p; }
static void Host_FreePermanent(void *p) { pvm_release(p); }
static void Host_FreeCode(void *p) { pvm_release(p); }
static void Host_Roots(void *p, int n) { prl_gc_extra_begin = p; prl_gc_extra_end = (const unsigned char *)p + n; }
static void *Host_Counter(void) { return &VM_steps; }
static void *Host_Code(int n) { void *p = prl_host_jit_alloc(n); if (!p) Host_Fail("code allocation failed"); return p; }
static void Host_CodeReady(int method, const unsigned char *name, uint32_t *code, int count) {
    prl_host_jit_flush(code, count * 4); pvm_code_bytes += count * 4;
    if (prl_log) fprintf(prl_log, "METHOD %d %s code_bytes=%d\n", method, name, count * 4);
    FILE *f = prl_test_frames ? fopen("jit-mips.bin", method == 0 ? "wb" : "ab") : NULL;
    if (f) { fwrite(code, 4, (size_t)count, f); fclose(f); }
}
static void Host_Call(void *code, void *frame, void *result) { ((void (*)(void *, void *))code)(frame, result); }
static void pvm_trap(int reason) {
    Host_Fail(reason == 1 ? "expected an int32 value" : reason == 3 ? "expected non-null array" : reason == 10 ? "instruction budget exceeded" : reason == 11 ? "array index out of bounds" : reason == 12 ? "division by zero" : reason == 13 ? "int32 division overflow" : "JIT value kind check failed");
}
/* Definitions occur in generated C; these expand only inside core function bodies. */
#define Host_Helper(n) ((n)==1 ? (void *)VM_Enter : (n)==2 ? (void *)VM_Leave : (n)==3 ? (void *)VM_Slow : (void *)pvm_trap)
static bool Host_Equal(const unsigned char *p, const char *s) { return strcmp((const char *)p, s) == 0; }
static bool Host_Starts(const unsigned char *p, const char *s) { return strncmp((const char *)p, s, strlen(s)) == 0; }
static bool Host_Same(const unsigned char *a, const unsigned char *b) { return strcmp((const char *)a, (const char *)b) == 0; }
static bool Host_BytesEqual(const unsigned char *a, const unsigned char *b, int n) { return memcmp(a, b, (size_t)n) == 0; }
static int Host_Length(const char *s) { return (int)strlen(s); }
static void Host_Ascii(const char *s, uint16_t *p, int n) { for (int i = 0; i < n; i++) p[i] = (unsigned char)s[i]; }
static int Host_Number(int n, bool uns, uint16_t *p) { char s[16]; int length = uns ? snprintf(s, sizeof(s), "%u", (unsigned)n) : snprintf(s, sizeof(s), "%d", n); for (int i = 0; i < length; i++) p[i] = (unsigned char)s[i]; return length; }
static char *pvm_utf8(const uint16_t *p, int n, int *size) {
    char *s = malloc((size_t)n * 3 + 1); if (!s) Host_Fail("UTF-8 allocation failed"); int at = 0;
    for (int i = 0; i < n; i++) {
        unsigned c = p[i];
        if (c >= 0xd800 && c <= 0xdbff && i + 1 < n && p[i+1] >= 0xdc00 && p[i+1] <= 0xdfff) { c = 0x10000 + ((c - 0xd800) << 10) + p[++i] - 0xdc00; }
        else if (c >= 0xd800 && c <= 0xdfff) c = 0xfffd;
        if (c < 128) s[at++] = (char)c;
        else if (c < 2048) { s[at++] = (char)(0xc0 | (c >> 6)); s[at++] = (char)(0x80 | (c & 63)); }
        else if (c < 65536) { s[at++] = (char)(0xe0 | (c >> 12)); s[at++] = (char)(0x80 | ((c >> 6) & 63)); s[at++] = (char)(0x80 | (c & 63)); }
        else { s[at++] = (char)(0xf0 | (c >> 18)); s[at++] = (char)(0x80 | ((c >> 12) & 63)); s[at++] = (char)(0x80 | ((c >> 6) & 63)); s[at++] = (char)(0x80 | (c & 63)); }
    }
    s[at] = 0; *size = at; return s;
}
static void Host_Write(const uint16_t *p, int n) { int size; char *s = pvm_utf8(p, n, &size); prl_host_write(s, size); free(s); }
static void Host_Init(void) { prl_host_psp_init(); }
static void Host_Clear(int c) { prl_host_clear(c); }
static void Host_Rect(int x,int y,int w,int h,int c) { prl_host_rect(x,y,w,h,c); }
static void Host_Line(int x,int y,int xx,int yy,int c) { prl_host_line(x,y,xx,yy,c); }
static void Host_Text(int x,int y,const uint16_t *p,int n,int c) { int size; char *s = pvm_utf8(p,n,&size); prl_host_text(x,y,s,size,c); free(s); }
static void Host_Present(int steps,int calls) { prl_host_perf(steps,calls); prl_host_swap(); }
static void Host_CollectStats(void) {
    if (!pvm_natural_gc) { prl_host_gc_stats(); return; }
    if (prl_log && prl_test_frames) { fprintf(prl_log,"HEAP bytes=%u blocks=%u collections=%u\n",(unsigned)prl_gc_bytes,(unsigned)prl_gc_count,prl_gc_cycles); fflush(prl_log); }
}
static int Host_Millis(void) { return prl_host_millis(); }
static void Host_Sleep(int n) { prl_host_sleep(n); }
static void Host_Vsync(void) { prl_host_vsync(); }
static int Host_Buttons(void) { return prl_host_buttons(); }
#endif
