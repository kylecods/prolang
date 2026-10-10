#ifndef PROLANG_MSIL_HOST_H
#define PROLANG_MSIL_HOST_H
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#ifdef __PSP__
#include <malloc.h>
#include <pspkernel.h>
#include "../../native/prl_psp.h"
PSP_MODULE_INFO("ProLang MSIL", 0, 0, 1);
PSP_MAIN_THREAD_ATTR(THREAD_ATTR_USER | THREAD_ATTR_VFPU);
PSP_MAIN_THREAD_STACK_SIZE_KB(512);
PSP_HEAP_SIZE_KB(12 * 1024);
static int prl_exit_callback(int a, int b, void *arg) {
    (void)a; (void)b; (void)arg;
    sceKernelExitGame();
    return 0;
}
static int prl_callback_thread(SceSize size, void *arg) {
    (void)size; (void)arg;
    int cb = sceKernelCreateCallback("Exit", prl_exit_callback, NULL);
    sceKernelRegisterExitCallback(cb);
    sceKernelSleepThreadCB();
    return 0;
}
static FILE *prl_log;
static int prl_frames, prl_test_frames, prl_lines, prl_rects, prl_texts;
static int prl_perf_last, prl_perf_instructions, prl_perf_calls;
static unsigned prl_geometry_hash = 2166136261u;
static void prl_host_init(void) {
    prl_log = fopen("runtime.log", "w");
    /* Optional, explicit test input. Normal homebrew has no frame limit. */
    FILE *test = fopen("test-frames.txt", "r");
    if (test) { fscanf(test, "%d", &prl_test_frames); fclose(test); }
    int th = sceKernelCreateThread("callbacks", prl_callback_thread, 0x11, 0x1000, 0, NULL);
    if (th >= 0) sceKernelStartThread(th, 0, NULL);
}
static void prl_host_write(const char *s, int length) {
    if (length) prl_psp_console_write((PrlString){(char *)s, length});
    if (prl_log) { fwrite(s, 1, (size_t)length, prl_log); fflush(prl_log); }
}
static void prl_host_sleep(int ms) { sceKernelDelayThread((unsigned int)ms * 1000); }
static int prl_host_millis(void) { return (int)(sceKernelGetSystemTimeWide() / 1000); }
static void prl_host_finish(int status) {
    const char *result = status ? "\nPROLANG FAIL\n" : "\nPROLANG OK\n";
    if (prl_frames && !status) {
        if (prl_log) { fwrite(result, 1, strlen(result), prl_log); fflush(prl_log); }
    } else prl_host_write(result, (int)strlen(result));
    if (prl_log) { fclose(prl_log); prl_log = NULL; }
    /* Keep the result visible. HOME exits through the callback. */
    sceKernelSleepThreadCB();
}
static void prl_host_psp_init(void) { prl_psp_init(); prl_perf_last = prl_host_millis(); }
static void prl_host_perf(int instructions, int calls) { prl_perf_instructions = instructions; prl_perf_calls = calls; }
static void prl_host_clear(int c) { prl_psp_clear(c); }
static void prl_host_rect(int x, int y, int w, int h, int c) {
    prl_rects++; prl_psp_fill_rect(x, y, w, h, c);
}
static void prl_host_line(int x, int y, int xx, int yy, int c) {
    prl_lines++;
    prl_geometry_hash = (prl_geometry_hash ^ (unsigned)x) * 16777619u;
    prl_geometry_hash = (prl_geometry_hash ^ (unsigned)y) * 16777619u;
    prl_geometry_hash = (prl_geometry_hash ^ (unsigned)xx) * 16777619u;
    prl_geometry_hash = (prl_geometry_hash ^ (unsigned)yy) * 16777619u;
    prl_psp_draw_line(x, y, xx, yy, c);
}
static void prl_host_text(int x, int y, const char *s, int len, int c) {
    prl_texts++; prl_psp_draw_text(x, y, (PrlString){(char *)s, len}, c);
}
static void prl_host_snapshot(const char *path) {
    void *buffer = NULL;
    int stride = 0, format = 0;
    if (sceDisplayGetFrameBuf(&buffer, &stride, &format, PSP_DISPLAY_SETBUF_NEXTFRAME) < 0 || !buffer || format != PSP_DISPLAY_PIXEL_FORMAT_8888) return;
    /* A GE transfer also makes buffered emulators read the rendered image back
     * to RAM. Direct VRAM loads alone can see an old image in PPSSPP. */
    uint32_t *copy = (uint32_t *)memalign(16, 512 * 272 * 4);
    if (!copy) return;
    sceKernelDcacheWritebackInvalidateRange(copy, 512 * 272 * 4);
    sceGuStart(GU_DIRECT, prl_psp_dlist);
    sceGuCopyImage(GU_PSM_8888, 0, 0, 480, 272, stride, buffer, 0, 0, 512, copy);
    sceGuFinish(); sceGuSync(GU_SYNC_FINISH, GU_SYNC_WHAT_DONE);
    sceKernelDcacheInvalidateRange(copy, 512 * 272 * 4);
    FILE *f = fopen(path, "wb");
    if (!f) { free(copy); return; }
    unsigned char header[54] = {'B','M'};
    uint32_t size = 54 + 480 * 272 * 3, offset = 54, dib = 40, w = 480, h = 272;
    memcpy(header+2, &size, 4); memcpy(header+10, &offset, 4); memcpy(header+14, &dib, 4);
    memcpy(header+18, &w, 4); memcpy(header+22, &h, 4); header[26] = 1; header[28] = 24;
    fwrite(header, 1, 54, f);
    const uint32_t *pixels = copy;
    unsigned char row[480 * 3];
    for (int y = 271; y >= 0; y--) {
        for (int x = 0; x < 480; x++) {
            uint32_t c = pixels[y * 512 + x];
            row[x*3] = (unsigned char)(c >> 16); row[x*3+1] = (unsigned char)(c >> 8); row[x*3+2] = (unsigned char)c;
        }
        fwrite(row, 1, sizeof(row), f);
    }
    fclose(f);
    free(copy);
}
static void prl_host_swap(void) {
    prl_psp_swap_buffers(); prl_frames++;
    if (prl_test_frames && prl_log) {
        fprintf(prl_log, "FRAME %d lines=%d rects=%d texts=%d geometry=%08x time_ms=%d instructions=%d calls=%d\n", prl_frames,
            prl_lines, prl_rects, prl_texts, prl_geometry_hash, prl_host_millis() - prl_perf_last,
            prl_perf_instructions, prl_perf_calls);
        fflush(prl_log);
        if (prl_frames == 1) prl_host_snapshot("first-frame.bmp");
        if (prl_frames == prl_test_frames) prl_host_snapshot("last-frame.bmp");
    }
    prl_lines = prl_rects = prl_texts = 0; prl_geometry_hash = 2166136261u;
    prl_perf_last = prl_host_millis();
}
static void prl_host_vsync(void) { prl_psp_vsync(); }
static int prl_host_buttons(void) {
    return prl_test_frames > 0 && prl_frames >= prl_test_frames ? 8 : prl_psp_buttons_held();
}
#else
#ifdef _WIN32
#include <windows.h>
#else
#include <time.h>
#endif
static void prl_host_init(void) {}
static void prl_host_write(const char *s, int length) { fwrite(s, 1, (size_t)length, stdout); fflush(stdout); }
static void prl_host_finish(int status) { (void)status; }
static void prl_host_sleep(int ms) {
#ifdef _WIN32
    Sleep((DWORD)ms);
#else
    struct timespec ts = {ms / 1000, (ms % 1000) * 1000000L};
    nanosleep(&ts, NULL);
#endif
}
static int prl_host_millis(void) {
#ifdef _WIN32
    return (int)GetTickCount64();
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int)((uint64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000);
#endif
}
/* Headless host for compiler/interpreter differential tests. */
static int prl_frames;
static int prl_headless_frames(void) {
    const char *s = getenv("PROLANG_TEST_FRAMES"); return s ? atoi(s) : 0;
}
static void prl_host_psp_init(void) {}
static void prl_host_perf(int instructions, int calls) { (void)instructions; (void)calls; }
static void prl_host_clear(int c) { (void)c; }
static void prl_host_rect(int x,int y,int w,int h,int c) { (void)x;(void)y;(void)w;(void)h;(void)c; }
static void prl_host_line(int x,int y,int xx,int yy,int c) { (void)x;(void)y;(void)xx;(void)yy;(void)c; }
static void prl_host_text(int x,int y,const char *s,int len,int c) { (void)x;(void)y;(void)s;(void)len;(void)c; }
static void prl_host_swap(void) { prl_frames++; }
static void prl_host_vsync(void) {}
static int prl_host_buttons(void) { return prl_frames >= prl_headless_frames() ? 8 : 0; }
#endif
static int prl_host_size(const char *path) {
    FILE *f = fopen(path, "rb");
    if (!f) return -1;
    if (fseek(f, 0, SEEK_END)) { fclose(f); return -1; }
    long n = ftell(f);
    fclose(f);
    return n < 0 || n > 8 * 1024 * 1024 ? -1 : (int)n;
}
static int prl_host_read(const char *path, unsigned char *data, int count) {
    FILE *f = fopen(path, "rb");
    if (!f) return -1;
    int n = (int)fread(data, 1, (size_t)count, f);
    fclose(f);
    return n;
}
#include "gc.h"
#ifdef __PSP__
static void prl_host_gc_init(void) {
    SceKernelThreadInfo info;
    memset(&info, 0, sizeof(info)); info.size = sizeof(info);
    if (sceKernelReferThreadStatus(sceKernelGetThreadId(), &info) >= 0)
        prl_gc_stack_end = (unsigned char *)info.stack + info.stackSize;
}
static void prl_host_gc_stats(void) {
    if (prl_log && prl_test_frames) {
        /* Stress live roots across frames as well as during loader allocation. */
        if (prl_frames % 30 == 0) prl_gc_collect();
        fprintf(prl_log, "HEAP bytes=%u blocks=%u collections=%u\n", (unsigned)prl_gc_bytes,
            (unsigned)prl_gc_count, prl_gc_cycles); fflush(prl_log);
    }
}
#else
static void prl_host_gc_init(void) {}
static void prl_host_gc_stats(void) {}
#endif
#endif
