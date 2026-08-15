#ifndef PRL_PSP_H
#define PRL_PSP_H

#if defined(__PSP__)

#include <stdbool.h>
#include <string.h>
#include "prl_types.h"
#include "prl_string.h"

#include <pspkernel.h>
#include <pspdisplay.h>
#include <pspctrl.h>
#include <pspge.h>
#include <pspgu.h>

#define PRL_PSP_SCR_W 480
#define PRL_PSP_SCR_H 272
#define PRL_PSP_BUF_W 512

/* Text console geometry (8x8 font on a 10px line pitch). */
#define PRL_PSP_TEXT_COLS (PRL_PSP_SCR_W / 8)   /* 60 */
#define PRL_PSP_TEXT_ROWS (PRL_PSP_SCR_H / 10)  /* 27 */

/* Per-frame vertex scratch reserved out of the display list (see prl_psp_valloc). */
#define PRL_PSP_VPOOL_VERTS 8192

/* GU_COLOR_8888 | GU_VERTEX_32BITF | GU_TRANSFORM_2D.
   Field order must follow the GE's vertex layout: colour before position. */
typedef struct { UINT32 color; float x, y, z; } PrlPspVertex;
#define PRL_PSP_VTYPE (GU_COLOR_8888 | GU_VERTEX_32BITF | GU_TRANSFORM_2D)

static unsigned int __attribute__((aligned(16))) prl_psp_dlist[262144];
static unsigned int prl_psp_vram_offset = 0;
static int prl_psp_gu_ready = 0;
static int prl_psp_frame_active = 0;

static PrlPspVertex *prl_psp_vpool = 0; /* per-frame vertex pool (display-list memory) */
static int prl_psp_vpool_used = 0;      /* vertices consumed from the pool */

/* GU framebuffer/depthbuffer parameters are offsets *relative to the start of VRAM*
   (sceGuSwapBuffers adds sceGeEdramGetAddr() itself). Returning an absolute
   0x44000000-based pointer here makes sceDisplaySetFrameBuf latch a bogus address,
   which is displayed as random VRAM garbage. */
static void *prl_psp_vram_alloc(int size) {
    void *p = (void *)(unsigned int)prl_psp_vram_offset;
    prl_psp_vram_offset = (prl_psp_vram_offset + (unsigned int)size + 15u) & ~15u;
    return p;
}

/* Vertex data handed to the GE must stay alive until the display list actually
   executes (at sceGuFinish/sceGuSync) and must be visible to the GE's DMA - stack
   locals are neither, they are dead and still sitting in the CPU data cache. All
   geometry therefore comes out of display-list memory via sceGuGetMemory.
   One pool allocation per frame keeps the per-primitive stall-address updates
   (a kernel call each) down to one instead of one per rectangle. */
static PrlPspVertex *prl_psp_valloc(int nverts) {
    if (prl_psp_vpool && prl_psp_vpool_used + nverts <= PRL_PSP_VPOOL_VERTS) {
        PrlPspVertex *p = prl_psp_vpool + prl_psp_vpool_used;
        prl_psp_vpool_used += nverts;
        return p;
    }
    return (PrlPspVertex *)sceGuGetMemory(nverts * (int)sizeof(PrlPspVertex));
}

/* The psp_* built-ins take colours as 0xRRGGBB (the usual hex-colour ordering); the GE
   wants 0xAABBGGRR, so swap the red and blue channels. Alpha is forced opaque so nothing
   depends on the caller supplying one. */
static inline UINT32 prl_psp_color(INT32 color) {
    UINT32 c = (UINT32)color;
    return 0xFF000000u
         | ((c & 0x000000FFu) << 16)   /* B -> high byte */
         |  (c & 0x0000FF00u)          /* G stays */
         | ((c & 0x00FF0000u) >> 16);  /* R -> low byte */
}

/* 8x8 bitmap font (ASCII 32..126), each glyph 8 bytes, one byte per row,
   leftmost pixel = most significant bit. */
static const unsigned char prl_psp_font[95][8] = {
    {0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00}, /*   */
    {0x18,0x18,0x18,0x18,0x18,0x00,0x18,0x00}, /* ! */
    {0x6C,0x6C,0x6C,0x00,0x00,0x00,0x00,0x00}, /* " */
    {0x6C,0x6C,0xFE,0x6C,0xFE,0x6C,0x6C,0x00}, /* # */
    {0x18,0x7E,0xC0,0x7C,0x06,0xFC,0x18,0x00}, /* $ */
    {0x00,0xC6,0xCC,0x18,0x30,0x66,0xC6,0x00}, /* % */
    {0x38,0x6C,0x38,0x76,0xDC,0xCC,0x76,0x00}, /* & */
    {0x18,0x18,0x30,0x00,0x00,0x00,0x00,0x00}, /* ' */
    {0x0C,0x18,0x30,0x30,0x30,0x18,0x0C,0x00}, /* ( */
    {0x30,0x18,0x0C,0x0C,0x0C,0x18,0x30,0x00}, /* ) */
    {0x00,0x66,0x3C,0xFF,0x3C,0x66,0x00,0x00}, /* * */
    {0x00,0x18,0x18,0x7E,0x18,0x18,0x00,0x00}, /* + */
    {0x00,0x00,0x00,0x00,0x00,0x18,0x18,0x30}, /* , */
    {0x00,0x00,0x00,0x7E,0x00,0x00,0x00,0x00}, /* - */
    {0x00,0x00,0x00,0x00,0x00,0x18,0x18,0x00}, /* . */
    {0x06,0x0C,0x18,0x30,0x60,0xC0,0x80,0x00}, /* / */
    {0x7C,0xCE,0xDE,0xF6,0xE6,0xC6,0x7C,0x00}, /* 0 */
    {0x30,0x70,0x30,0x30,0x30,0x30,0xFC,0x00}, /* 1 */
    {0x7C,0xC6,0x06,0x3C,0x60,0xC0,0xFE,0x00}, /* 2 */
    {0xFC,0x0C,0x18,0x3C,0x06,0x06,0xFC,0x00}, /* 3 */
    {0x1C,0x3C,0x6C,0xCC,0xFE,0x0C,0x1E,0x00}, /* 4 */
    {0xFE,0xC0,0xFC,0x06,0x06,0xC6,0x7C,0x00}, /* 5 */
    {0x3C,0x60,0xC0,0xFC,0xC6,0xC6,0x7C,0x00}, /* 6 */
    {0xFE,0x06,0x0C,0x18,0x30,0x30,0x30,0x00}, /* 7 */
    {0x7C,0xC6,0xC6,0x7C,0xC6,0xC6,0x7C,0x00}, /* 8 */
    {0x7C,0xC6,0xC6,0x7E,0x06,0x0C,0x78,0x00}, /* 9 */
    {0x00,0x18,0x18,0x00,0x00,0x18,0x18,0x00}, /* : */
    {0x00,0x18,0x18,0x00,0x00,0x18,0x18,0x30}, /* ; */
    {0x0C,0x18,0x30,0x60,0x30,0x18,0x0C,0x00}, /* < */
    {0x00,0x00,0x7E,0x00,0x00,0x7E,0x00,0x00}, /* = */
    {0x30,0x18,0x0C,0x06,0x0C,0x18,0x30,0x00}, /* > */
    {0x7C,0xC6,0x06,0x1C,0x18,0x00,0x18,0x00}, /* ? */
    {0x7C,0xC6,0xDE,0xDE,0xDE,0xC0,0x7C,0x00}, /* @ */
    {0x38,0x6C,0xC6,0xFE,0xC6,0xC6,0xC6,0x00}, /* A */
    {0xFC,0x66,0x66,0x7C,0x66,0x66,0xFC,0x00}, /* B */
    {0x3C,0x66,0xC0,0xC0,0xC0,0x66,0x3C,0x00}, /* C */
    {0xF8,0x6C,0x66,0x66,0x66,0x6C,0xF8,0x00}, /* D */
    {0xFE,0x62,0x68,0x78,0x68,0x62,0xFE,0x00}, /* E */
    {0xFE,0x62,0x68,0x78,0x68,0x60,0xF0,0x00}, /* F */
    {0x3C,0x66,0xC0,0xC0,0xCE,0x66,0x3E,0x00}, /* G */
    {0xC6,0xC6,0xC6,0xFE,0xC6,0xC6,0xC6,0x00}, /* H */
    {0x3C,0x18,0x18,0x18,0x18,0x18,0x3C,0x00}, /* I */
    {0x1E,0x0C,0x0C,0x0C,0xCC,0xCC,0x78,0x00}, /* J */
    {0xE6,0x66,0x6C,0x78,0x6C,0x66,0xE6,0x00}, /* K */
    {0xF0,0x60,0x60,0x60,0x62,0x66,0xFE,0x00}, /* L */
    {0xC6,0xEE,0xFE,0xFE,0xD6,0xC6,0xC6,0x00}, /* M */
    {0xC6,0xE6,0xF6,0xDE,0xCE,0xC6,0xC6,0x00}, /* N */
    {0x38,0x6C,0xC6,0xC6,0xC6,0x6C,0x38,0x00}, /* O */
    {0xFC,0x66,0x66,0x7C,0x60,0x60,0xF0,0x00}, /* P */
    {0x7C,0xC6,0xC6,0xC6,0xD6,0x6C,0x36,0x00}, /* Q */
    {0xFC,0x66,0x66,0x7C,0x6C,0x66,0xE6,0x00}, /* R */
    {0x7C,0xC6,0x60,0x38,0x0C,0xC6,0x7C,0x00}, /* S */
    {0xFE,0xB6,0x30,0x30,0x30,0x30,0x78,0x00}, /* T */
    {0xC6,0xC6,0xC6,0xC6,0xC6,0xC6,0x7C,0x00}, /* U */
    {0xC6,0xC6,0xC6,0xC6,0xC6,0x6C,0x38,0x00}, /* V */
    {0xC6,0xC6,0xC6,0xD6,0xFE,0xEE,0xC6,0x00}, /* W */
    {0xC6,0xC6,0x6C,0x38,0x6C,0xC6,0xC6,0x00}, /* X */
    {0xC6,0xC6,0xC6,0x7C,0x18,0x30,0x70,0x00}, /* Y */
    {0xFE,0x86,0x0C,0x18,0x30,0x62,0xFE,0x00}, /* Z */
    {0x3C,0x30,0x30,0x30,0x30,0x30,0x3C,0x00}, /* [ */
    {0xC0,0x60,0x30,0x18,0x0C,0x06,0x02,0x00}, /* \ */
    {0x3C,0x0C,0x0C,0x0C,0x0C,0x0C,0x3C,0x00}, /* ] */
    {0x10,0x38,0x6C,0xC6,0x00,0x00,0x00,0x00}, /* ^ */
    {0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xFF}, /* _ */
    {0x30,0x18,0x0C,0x00,0x00,0x00,0x00,0x00}, /* ` */
    {0x00,0x00,0x78,0x0C,0x7C,0xCC,0x76,0x00}, /* a */
    {0xE0,0x60,0x60,0x7C,0x66,0x66,0xDC,0x00}, /* b */
    {0x00,0x00,0x7C,0xC0,0xC0,0xC0,0x7C,0x00}, /* c */
    {0x1C,0x0C,0x0C,0x7C,0xCC,0xCC,0x76,0x00}, /* d */
    {0x00,0x00,0x7C,0xC6,0xFE,0xC0,0x7C,0x00}, /* e */
    {0x3C,0x66,0x60,0xF0,0x60,0x60,0xF0,0x00}, /* f */
    {0x00,0x00,0x76,0xCC,0xCC,0x7C,0x0C,0xF8}, /* g */
    {0xE0,0x60,0x6C,0x76,0x66,0x66,0xE6,0x00}, /* h */
    {0x18,0x00,0x38,0x18,0x18,0x18,0x3C,0x00}, /* i */
    {0x06,0x00,0x0E,0x06,0x06,0x06,0xC6,0x7C}, /* j */
    {0xE0,0x60,0x66,0x6C,0x78,0x6C,0xE6,0x00}, /* k */
    {0x38,0x18,0x18,0x18,0x18,0x18,0x3C,0x00}, /* l */
    {0x00,0x00,0xEC,0xFE,0xD6,0xD6,0xD6,0x00}, /* m */
    {0x00,0x00,0xDC,0x66,0x66,0x66,0x66,0x00}, /* n */
    {0x00,0x00,0x7C,0xC6,0xC6,0xC6,0x7C,0x00}, /* o */
    {0x00,0x00,0xDC,0x66,0x66,0x7C,0x60,0xF0}, /* p */
    {0x00,0x00,0x76,0xCC,0xCC,0x7C,0x0C,0x1E}, /* q */
    {0x00,0x00,0xDC,0x76,0x66,0x60,0xF0,0x00}, /* r */
    {0x00,0x00,0x7C,0xC0,0x7C,0x06,0xFC,0x00}, /* s */
    {0x30,0x30,0xFC,0x30,0x30,0x36,0x1C,0x00}, /* t */
    {0x00,0x00,0xCC,0xCC,0xCC,0xCC,0x76,0x00}, /* u */
    {0x00,0x00,0xC6,0xC6,0xC6,0x6C,0x38,0x00}, /* v */
    {0x00,0x00,0xC6,0xD6,0xD6,0xFE,0x6C,0x00}, /* w */
    {0x00,0x00,0xC6,0x6C,0x38,0x6C,0xC6,0x00}, /* x */
    {0x00,0x00,0xC6,0xC6,0xC6,0x7E,0x06,0xFC}, /* y */
    {0x00,0x00,0xFE,0x8C,0x18,0x32,0xFE,0x00}, /* z */
    {0x0C,0x18,0x18,0x70,0x18,0x18,0x0C,0x00}, /* { */
    {0x18,0x18,0x18,0x18,0x18,0x18,0x18,0x00}, /* | */
    {0x30,0x18,0x18,0x0E,0x18,0x18,0x30,0x00}, /* } */
    {0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00}  /* ~ */
};

static void prl_psp_init(void);

static void prl_psp_start_frame(void) {
    if (!prl_psp_gu_ready) prl_psp_init();
    if (prl_psp_frame_active) return;
    sceGuStart(GU_DIRECT, prl_psp_dlist);
    prl_psp_vpool = (PrlPspVertex *)sceGuGetMemory(PRL_PSP_VPOOL_VERTS * (int)sizeof(PrlPspVertex));
    prl_psp_vpool_used = 0;
    prl_psp_frame_active = 1;
}

static void prl_psp_init(void) {
    if (prl_psp_gu_ready) return;
    void *fbp0 = prl_psp_vram_alloc(PRL_PSP_BUF_W * PRL_PSP_SCR_H * 4);
    void *fbp1 = prl_psp_vram_alloc(PRL_PSP_BUF_W * PRL_PSP_SCR_H * 4);
    void *zbp  = prl_psp_vram_alloc(PRL_PSP_BUF_W * PRL_PSP_SCR_H * 2);

    sceGuInit();
    sceGuStart(GU_DIRECT, prl_psp_dlist);
    sceGuDrawBuffer(GU_PSM_8888, fbp0, PRL_PSP_BUF_W);
    sceGuDispBuffer(PRL_PSP_SCR_W, PRL_PSP_SCR_H, fbp1, PRL_PSP_BUF_W);
    sceGuDepthBuffer(zbp, PRL_PSP_BUF_W);
    sceGuOffset(2048 - PRL_PSP_SCR_W/2, 2048 - PRL_PSP_SCR_H/2);
    sceGuViewport(2048, 2048, PRL_PSP_SCR_W, PRL_PSP_SCR_H);
    sceGuDepthRange(65535, 0);
    sceGuScissor(0, 0, PRL_PSP_SCR_W, PRL_PSP_SCR_H);
    sceGuEnable(GU_SCISSOR_TEST);
    sceGuShadeModel(GU_FLAT);
    sceGuDisable(GU_DEPTH_TEST);
    sceGuDisable(GU_CULL_FACE);
    sceGuDisable(GU_TEXTURE_2D);
    sceGuDisable(GU_BLEND);
    sceGuDisable(GU_LIGHTING);
    sceGuFinish();
    sceGuSync(GU_SYNC_FINISH, GU_SYNC_WHAT_DONE);
    sceDisplayWaitVblankStart();
    sceGuDisplay(GU_TRUE);

    sceCtrlSetSamplingMode(PSP_CTRL_MODE_ANALOG);

    prl_psp_gu_ready = 1;

    /* Pre-clear BOTH framebuffers so every sceGuSwapBuffers alternates between two
       clean buffers instead of showing uninitialized VRAM garbage. */
    int pre;
    for (pre = 0; pre < 2; pre++) {
        sceGuStart(GU_DIRECT, prl_psp_dlist);
        sceGuClearColor(0xFF000000u);
        sceGuClear(GU_COLOR_BUFFER_BIT);
        sceGuFinish();
        sceGuSync(GU_SYNC_FINISH, GU_SYNC_WHAT_DONE);
        sceDisplayWaitVblankStart();
        sceGuSwapBuffers();
    }
}

static void prl_psp_clear(INT32 color) {
    prl_psp_start_frame();
    sceGuClearColor(prl_psp_color(color));
    sceGuClear(GU_COLOR_BUFFER_BIT);
}

static void prl_psp_fill_rect(INT32 x, INT32 y, INT32 w, INT32 h, INT32 color) {
    if (w <= 0 || h <= 0) return;
    prl_psp_start_frame();
    /* GU_SPRITES: two vertices (top-left, bottom-right exclusive) per rectangle. */
    UINT32 c = prl_psp_color(color);
    PrlPspVertex *v = prl_psp_valloc(2);
    v[0].color = c; v[0].x = (float)x;       v[0].y = (float)y;       v[0].z = 0.0f;
    v[1].color = c; v[1].x = (float)(x + w); v[1].y = (float)(y + h); v[1].z = 0.0f;
    sceGuDrawArray(GU_SPRITES, PRL_PSP_VTYPE, 2, NULL, v);
}

/* Number of horizontal runs of set pixels in one glyph row (max 4). */
static int prl_psp_row_spans(unsigned char bits) {
    int n = 0, col = 0;
    while (col < 8) {
        if (bits & (0x80u >> col)) {
            while (col < 8 && (bits & (0x80u >> col))) col++;
            n++;
        } else {
            col++;
        }
    }
    return n;
}

/* Draw a run of characters as a single batched GU_SPRITES call: each glyph row is
   emitted as horizontal spans rather than one draw call per lit pixel. */
static void prl_psp_draw_chars(INT32 x, INT32 y, const char *data, INT32 len, UINT32 color) {
    if (len <= 0) return;
    prl_psp_start_frame();

    int i, row, col, spans = 0;
    for (i = 0; i < len; i++) {
        unsigned char c = (unsigned char)data[i];
        if (c < 32 || c > 126) c = '?';
        const unsigned char *g = prl_psp_font[c - 32];
        for (row = 0; row < 8; row++) spans += prl_psp_row_spans(g[row]);
    }
    if (spans == 0) return;

    PrlPspVertex *v = prl_psp_valloc(spans * 2);
    int n = 0;
    for (i = 0; i < len; i++) {
        unsigned char c = (unsigned char)data[i];
        if (c < 32 || c > 126) c = '?';
        const unsigned char *g = prl_psp_font[c - 32];
        int gx = x + i * 8;
        for (row = 0; row < 8; row++) {
            unsigned char bits = g[row];
            col = 0;
            while (col < 8) {
                if (bits & (0x80u >> col)) {
                    int start = col;
                    while (col < 8 && (bits & (0x80u >> col))) col++;
                    v[n].color = color; v[n].x = (float)(gx + start); v[n].y = (float)(y + row);     v[n].z = 0.0f; n++;
                    v[n].color = color; v[n].x = (float)(gx + col);   v[n].y = (float)(y + row + 1); v[n].z = 0.0f; n++;
                } else {
                    col++;
                }
            }
        }
    }
    sceGuDrawArray(GU_SPRITES, PRL_PSP_VTYPE, spans * 2, NULL, v);
}

static void prl_psp_draw_text(INT32 x, INT32 y, PrlString s, INT32 color) {
    prl_psp_draw_chars(x, y, s.data, s.len, prl_psp_color(color));
}

/* Present the current frame. wait_vblank pauses for the retrace first (frame pacing
   for game loops); the console path skips it since sceDisplaySetFrameBuf latches on
   the next vblank anyway. */
static void prl_psp_present(int wait_vblank) {
    if (!prl_psp_frame_active) return;
    sceGuFinish();
    sceGuSync(GU_SYNC_FINISH, GU_SYNC_WHAT_DONE);
    if (wait_vblank) sceDisplayWaitVblankStart();
    sceGuSwapBuffers();
    prl_psp_frame_active = 0;
    prl_psp_vpool = 0;
    prl_psp_vpool_used = 0;
}

static void prl_psp_swap_buffers(void) {
    prl_psp_present(1);
}

static void prl_psp_vsync(void) {
    sceDisplayWaitVblankStart();
}

static INT32 prl_psp_buttons_held(void) {
    SceCtrlData pad;
    sceCtrlPeekBufferPositive(&pad, 1);
    return (INT32)pad.Buttons;
}

static bool prl_psp_button_pressed(INT32 button) {
    SceCtrlData pad;
    sceCtrlPeekBufferPositive(&pad, 1);
    return (pad.Buttons & (unsigned int)button) != 0;
}

/* ── Console text helpers (used by prl_console.h on PSP) ──
   The console keeps a character/colour grid and repaints all of it on every flush.
   Drawing only the new text and swapping would alternate between two half-written
   buffers, which is what made console output flicker. */
static char   prl_psp_text_ch[PRL_PSP_TEXT_ROWS][PRL_PSP_TEXT_COLS];
static UINT32 prl_psp_text_fg[PRL_PSP_TEXT_ROWS][PRL_PSP_TEXT_COLS];
static int    prl_psp_text_init_done = 0;
static int    prl_psp_cur_row = 0;
static int    prl_psp_cur_col = 0;
static UINT32 prl_psp_text_color = 0xFFFFFFFF;

static void prl_psp_text_reset(void) {
    int r, c;
    for (r = 0; r < PRL_PSP_TEXT_ROWS; r++)
        for (c = 0; c < PRL_PSP_TEXT_COLS; c++) {
            prl_psp_text_ch[r][c] = ' ';
            prl_psp_text_fg[r][c] = 0xFFFFFFFF;
        }
    prl_psp_text_init_done = 1;
}

static void prl_psp_text_scroll(void) {
    int r, c;
    for (r = 0; r < PRL_PSP_TEXT_ROWS - 1; r++)
        for (c = 0; c < PRL_PSP_TEXT_COLS; c++) {
            prl_psp_text_ch[r][c] = prl_psp_text_ch[r + 1][c];
            prl_psp_text_fg[r][c] = prl_psp_text_fg[r + 1][c];
        }
    for (c = 0; c < PRL_PSP_TEXT_COLS; c++) {
        prl_psp_text_ch[PRL_PSP_TEXT_ROWS - 1][c] = ' ';
        prl_psp_text_fg[PRL_PSP_TEXT_ROWS - 1][c] = 0xFFFFFFFF;
    }
}

static void prl_psp_text_newline(void) {
    prl_psp_cur_col = 0;
    if (++prl_psp_cur_row >= PRL_PSP_TEXT_ROWS) {
        prl_psp_text_scroll();
        prl_psp_cur_row = PRL_PSP_TEXT_ROWS - 1;
    }
}

static void prl_psp_text_put(PrlString s) {
    if (!prl_psp_text_init_done) prl_psp_text_reset();
    int i;
    for (i = 0; i < s.len; i++) {
        char c = s.data[i];
        if (c == '\r') { prl_psp_cur_col = 0; continue; }
        if (c == '\n') { prl_psp_text_newline(); continue; }
        if (prl_psp_cur_col >= PRL_PSP_TEXT_COLS) prl_psp_text_newline();
        prl_psp_text_ch[prl_psp_cur_row][prl_psp_cur_col] = c;
        prl_psp_text_fg[prl_psp_cur_row][prl_psp_cur_col] = prl_psp_text_color;
        prl_psp_cur_col++;
    }
}

static void prl_psp_text_flush(void) {
    if (!prl_psp_text_init_done) prl_psp_text_reset();
    prl_psp_start_frame();
    sceGuClearColor(0xFF000000u);
    sceGuClear(GU_COLOR_BUFFER_BIT);

    int r, c;
    for (r = 0; r < PRL_PSP_TEXT_ROWS; r++) {
        c = 0;
        while (c < PRL_PSP_TEXT_COLS) {
            /* Batch the longest run sharing one colour into a single draw call. */
            UINT32 fg = prl_psp_text_fg[r][c];
            int start = c;
            while (c < PRL_PSP_TEXT_COLS && prl_psp_text_fg[r][c] == fg) c++;
            prl_psp_draw_chars(start * 8, r * 10, &prl_psp_text_ch[r][start], c - start, fg);
        }
    }
    prl_psp_present(0);
}

static inline void prl_psp_set_text_cursor(INT32 x, INT32 y) {
    if (x < 0) x = 0;
    if (y < 0) y = 0;
    prl_psp_cur_col = (x < PRL_PSP_TEXT_COLS) ? (int)x : PRL_PSP_TEXT_COLS - 1;
    prl_psp_cur_row = (y < PRL_PSP_TEXT_ROWS) ? (int)y : PRL_PSP_TEXT_ROWS - 1;
}
static inline void prl_psp_set_text_color(INT32 color) {
    /* PSP: 0xAABBGGRR. Map a 0-15 console index to a plain colour. */
    static const UINT32 palette[16] = {
        0xFF000000,0xFF800000,0xFF008000,0xFF808000,
        0xFF000080,0xFF800080,0xFF008080,0xFFC0C0C0,
        0xFF808080,0xFFFF0000,0xFF00FF00,0xFFFFFF00,
        0xFF0000FF,0xFFFF00FF,0xFF00FFFF,0xFFFFFFFF
    };
    if (color >= 0 && color < 16) prl_psp_text_color = palette[color];
}
static inline void prl_psp_reset_text_color(void) {
    prl_psp_text_color = 0xFFFFFFFF;
}
static inline void prl_psp_console_write(PrlString s) {
    prl_psp_text_put(s);
    prl_psp_text_flush();
}
static inline void prl_psp_print(PrlString s) {
    prl_psp_text_put(s);
    prl_psp_text_newline();
    prl_psp_text_flush();
}

#endif /* __PSP__ */

#endif /* PRL_PSP_H */
