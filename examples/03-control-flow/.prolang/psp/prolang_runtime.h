#ifndef PROLANG_RUNTIME_H
#define PROLANG_RUNTIME_H

#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef __PSP__
#  include <pspkernel.h>
#  include <pspdebug.h>
#  include <pspdisplay.h>
#  include <pspctrl.h>
#  include <pspgu.h>
#  include <pspgum.h>
#endif

/* ── String type ── */
typedef struct { char* data; int32_t len; } PrlString;

#ifdef __PSP__
/* Forward declarations for GU text used by prl_print / prl_console_write (defined later). */
static unsigned int prl_psp_text_x;
static unsigned int prl_psp_text_y;
static unsigned int prl_psp_text_color;
static void prl_psp_start_frame(void);
static void prl_psp_draw_text(int32_t x, int32_t y, PrlString s, int32_t color);
static void prl_psp_swap_buffers(void);
#endif

/* ── Typed array structs for every built-in element type ── */
typedef struct { bool*      data; int32_t len; } PrlArray_bool;
typedef struct { int8_t*    data; int32_t len; } PrlArray_int8_t;
typedef struct { int16_t*   data; int32_t len; } PrlArray_int16_t;
typedef struct { int32_t*   data; int32_t len; } PrlArray_int32_t;
typedef struct { int64_t*   data; int32_t len; } PrlArray_int64_t;
typedef struct { uint8_t*   data; int32_t len; } PrlArray_uint8_t;
typedef struct { uint16_t*  data; int32_t len; } PrlArray_uint16_t;
typedef struct { uint32_t*  data; int32_t len; } PrlArray_uint32_t;
typedef struct { uint64_t*  data; int32_t len; } PrlArray_uint64_t;
typedef struct { float*     data; int32_t len; } PrlArray_float;
typedef struct { double*    data; int32_t len; } PrlArray_double;
typedef struct { PrlString* data; int32_t len; } PrlArray_PrlString;

/* ── Array constructors ── */
static inline PrlArray_bool     prl_array_new_bool    (int32_t n){ PrlArray_bool     a={(bool*)    calloc((size_t)n,sizeof(bool)),    n}; return a; }
static inline PrlArray_int8_t   prl_array_new_int8_t  (int32_t n){ PrlArray_int8_t   a={(int8_t*)  calloc((size_t)n,sizeof(int8_t)),  n}; return a; }
static inline PrlArray_int16_t  prl_array_new_int16_t (int32_t n){ PrlArray_int16_t  a={(int16_t*) calloc((size_t)n,sizeof(int16_t)), n}; return a; }
static inline PrlArray_int32_t  prl_array_new_int32_t (int32_t n){ PrlArray_int32_t  a={(int32_t*) calloc((size_t)n,sizeof(int32_t)), n}; return a; }
static inline PrlArray_int64_t  prl_array_new_int64_t (int32_t n){ PrlArray_int64_t  a={(int64_t*) calloc((size_t)n,sizeof(int64_t)), n}; return a; }
static inline PrlArray_uint8_t  prl_array_new_uint8_t (int32_t n){ PrlArray_uint8_t  a={(uint8_t*) calloc((size_t)n,sizeof(uint8_t)), n}; return a; }
static inline PrlArray_uint16_t prl_array_new_uint16_t(int32_t n){ PrlArray_uint16_t a={(uint16_t*)calloc((size_t)n,sizeof(uint16_t)),n}; return a; }
static inline PrlArray_uint32_t prl_array_new_uint32_t(int32_t n){ PrlArray_uint32_t a={(uint32_t*)calloc((size_t)n,sizeof(uint32_t)),n}; return a; }
static inline PrlArray_uint64_t prl_array_new_uint64_t(int32_t n){ PrlArray_uint64_t a={(uint64_t*)calloc((size_t)n,sizeof(uint64_t)),n}; return a; }
static inline PrlArray_float    prl_array_new_float   (int32_t n){ PrlArray_float    a={(float*)   calloc((size_t)n,sizeof(float)),   n}; return a; }
static inline PrlArray_double   prl_array_new_double  (int32_t n){ PrlArray_double   a={(double*)  calloc((size_t)n,sizeof(double)),  n}; return a; }
static inline PrlArray_PrlString prl_array_new_PrlString(int32_t n){ PrlArray_PrlString a={(PrlString*)calloc((size_t)n,sizeof(PrlString)),n}; return a; }

/* ── PrlString helpers ── */
static inline PrlString prl_string_from_lit(const char* s) {
    PrlString r; r.data=(char*)s; r.len=(int32_t)strlen(s); return r;
}
static inline PrlString prl_string_concat(PrlString a, PrlString b) {
    int32_t total=a.len+b.len;
    char* buf=(char*)malloc((size_t)(total+1));
    memcpy(buf,a.data,(size_t)a.len);
    memcpy(buf+a.len,b.data,(size_t)b.len);
    buf[total]='\0';
    PrlString r; r.data=buf; r.len=total; return r;
}
static inline bool prl_string_equals(PrlString a, PrlString b) {
    return a.len==b.len && memcmp(a.data,b.data,(size_t)a.len)==0;
}
static inline bool prl_string_not_equals(PrlString a, PrlString b) {
    return !prl_string_equals(a,b);
}
static inline int32_t prl_string_length(PrlString s) { return s.len; }
static inline PrlString prl_string_char_at(PrlString s, int32_t i) {
    if(i<0||i>=s.len){ PrlString e; e.data=(char*)""; e.len=0; return e; }
    char* buf=(char*)malloc(2); buf[0]=s.data[i]; buf[1]='\0';
    PrlString r; r.data=buf; r.len=1; return r;
}
static inline PrlString prl_string_substring(PrlString s, int32_t start, int32_t end) {
    if(start<0)start=0; if(end>s.len)end=s.len;
    int32_t len=end-start; if(len<0)len=0;
    char* buf=(char*)malloc((size_t)(len+1));
    memcpy(buf,s.data+start,(size_t)len); buf[len]='\0';
    PrlString r; r.data=buf; r.len=len; return r;
}
static inline int32_t prl_string_index_of(PrlString s, PrlString needle) {
    if(needle.len==0) return 0;
    if(needle.len>s.len) return -1;
    for(int32_t i=0;i<=s.len-needle.len;i++)
        if(memcmp(s.data+i,needle.data,(size_t)needle.len)==0) return i;
    return -1;
}
static inline PrlString prl_int_to_string(int64_t v) {
    char buf[32]; int n=snprintf(buf,sizeof(buf),"%lld",(long long)v);
    char* d=(char*)malloc((size_t)(n+1)); memcpy(d,buf,(size_t)(n+1));
    PrlString r; r.data=d; r.len=n; return r;
}
static inline PrlString prl_bool_to_string(bool v) {
    return prl_string_from_lit(v?"true":"false");
}
static inline PrlString prl_float_to_string(double v) {
    char buf[64]; int n=snprintf(buf,sizeof(buf),"%g",v);
    char* d=(char*)malloc((size_t)(n+1)); memcpy(d,buf,(size_t)(n+1));
    PrlString r; r.data=d; r.len=n; return r;
}
static inline PrlString prl_any_to_string(void* v) {
    (void)v; return prl_string_from_lit("<object>");
}

/* ── IO ── */
#ifdef __PSP__
static inline void prl_print(PrlString s) {
    prl_psp_start_frame();
    prl_psp_draw_text(prl_psp_text_x, prl_psp_text_y, s, prl_psp_text_color);
    prl_psp_text_y += 10;
    prl_psp_swap_buffers();
}
static inline PrlString prl_read_input(void) {
    return prl_string_from_lit("");
}
#else
static inline void prl_print(PrlString s) {
    fwrite(s.data,1,(size_t)s.len,stdout); putchar('\n'); fflush(stdout);
}
static inline PrlString prl_read_input(void) {
    char buf[4096];
    if(!fgets(buf,sizeof(buf),stdin)){ return prl_string_from_lit(""); }
    int32_t len=(int32_t)strlen(buf);
    if(len>0&&buf[len-1]=='\n') buf[--len]='\0';
    char* d=(char*)malloc((size_t)(len+1)); memcpy(d,buf,(size_t)(len+1));
    PrlString r; r.data=d; r.len=len; return r;
}
#endif

/* ── Math ── */
static inline int32_t prl_random(int32_t max) { return max<=0?0:rand()%max; }
static inline int32_t prl_min_i(int32_t a, int32_t b) { return a<b?a:b; }
static inline int32_t prl_max_i(int32_t a, int32_t b) { return a>b?a:b; }

/* ── File system ── */
static inline bool prl_file_exists(PrlString path) {
    FILE* f=fopen(path.data,"r"); if(f){fclose(f);return true;} return false;
}
static inline PrlString prl_read_file(PrlString path) {
    FILE* f=fopen(path.data,"rb");
    if(!f) return prl_string_from_lit("");
    fseek(f,0,SEEK_END); long sz=ftell(f); fseek(f,0,SEEK_SET);
    char* d=(char*)malloc((size_t)(sz+1)); fread(d,1,(size_t)sz,f); fclose(f); d[sz]='\0';
    PrlString r; r.data=d; r.len=(int32_t)sz; return r;
}
static inline PrlArray_uint8_t prl_read_file_bytes(PrlString path) {
    FILE* f=fopen(path.data,"rb");
    PrlArray_uint8_t empty; empty.data=NULL; empty.len=0;
    if(!f) return empty;
    fseek(f,0,SEEK_END); long sz=ftell(f); fseek(f,0,SEEK_SET);
    uint8_t* d=(uint8_t*)malloc((size_t)sz); fread(d,1,(size_t)sz,f); fclose(f);
    PrlArray_uint8_t r; r.data=d; r.len=(int32_t)sz; return r;
}
static inline void prl_write_file(PrlString path, PrlString contents) {
    FILE* f=fopen(path.data,"wb"); if(!f) return;
    fwrite(contents.data,1,(size_t)contents.len,f); fclose(f);
}

/* ── Console & platform ── */
#if defined(__PSP__)

/* ── PSP GU graphics + input ──────────────────────────────────────────────── */

#include <pspgu.h>
#include <pspgum.h>

#define PRL_PSP_SCR_W 480
#define PRL_PSP_SCR_H 272
#define PRL_PSP_BUF_W 512

static unsigned int __attribute__((aligned(16))) prl_psp_dlist[262144];
static unsigned int prl_psp_vram_offset = 0;
static int prl_psp_gu_ready = 0;
static int prl_psp_frame_active = 0;
static unsigned int prl_psp_text_x = 0;        /* pixel cursor for print() */
static unsigned int prl_psp_text_y = 0;
static unsigned int prl_psp_text_color = 0xFFFFFFFF;

static void *prl_psp_vram_alloc(int size) {
    void *p = (void *)(0x44000000u + prl_psp_vram_offset);
    prl_psp_vram_offset = (prl_psp_vram_offset + size + 15u) & ~15u;
    return p;
}

/* 8x8 bitmap font (ASCII 32..126), each glyph 8 bytes, top row = LSB. */
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

static void prl_psp_draw_glyph(int32_t x, int32_t y, unsigned char c, uint32_t color) {
    if (c < 32 || c > 126) c = '?';
    const unsigned char *g = prl_psp_font[c - 32];
    int row, col;
    for (row = 0; row < 8; row++) {
        unsigned char bits = g[row];
        for (col = 0; col < 8; col++) {
            if (bits & (1u << col)) {
                float v[3];
                v[0] = (float)(x + col);
                v[1] = (float)(y + row);
                v[2] = 0.0f;
                sceGuColor(color);
                sceGumDrawArray(GU_POINTS, GU_VERTEX_32BITF | GU_TRANSFORM_2D, 1, NULL, v);
            }
        }
    }
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
    sceGuFinish();
    sceGuSync(GU_SYNC_FINISH, GU_SYNC_WHAT_DONE);
    sceDisplayWaitVblankStart();
    sceGuDisplay(GU_TRUE);

    sceCtrlSetSamplingMode(PSP_CTRL_MODE_ANALOG);
    prl_psp_gu_ready = 1;
}

static void prl_psp_start_frame(void) {
    if (!prl_psp_gu_ready) prl_psp_init();
    if (prl_psp_frame_active) return;
    sceGuStart(GU_DIRECT, prl_psp_dlist);
    prl_psp_frame_active = 1;
}

static void prl_psp_clear(int32_t color) {
    prl_psp_start_frame();
    sceGuClearColor((uint32_t)color);
    sceGuClear(GU_COLOR_BUFFER_BIT);
}

static void prl_psp_fill_rect(int32_t x, int32_t y, int32_t w, int32_t h, int32_t color) {
    prl_psp_start_frame();
    float verts[8];
    verts[0]=(float)x;       verts[1]=(float)y;
    verts[2]=(float)(x+w-1); verts[3]=(float)y;
    verts[4]=(float)(x+w-1); verts[5]=(float)(y+h-1);
    verts[6]=(float)x;       verts[7]=(float)(y+h-1);
    sceGuColor((uint32_t)color);
    sceGumDrawArray(GU_TRIANGLE_FAN, GU_VERTEX_32BITF | GU_TRANSFORM_2D, 4, NULL, verts);
}

static void prl_psp_draw_text(int32_t x, int32_t y, PrlString s, int32_t color) {
    prl_psp_start_frame();
    int i;
    for (i = 0; i < s.len; i++)
        prl_psp_draw_glyph(x + i*8, y, (unsigned char)s.data[i], (uint32_t)color);
}

static void prl_psp_swap_buffers(void) {
    if (!prl_psp_frame_active) return;
    sceGuFinish();
    sceGuSync(GU_SYNC_FINISH, GU_SYNC_WHAT_DONE);
    sceDisplayWaitVblankStart();
    sceGuSwapBuffers();
    prl_psp_frame_active = 0;
}

static void prl_psp_vsync(void) {
    sceDisplayWaitVblankStart();
}

static int32_t prl_psp_buttons_held(void) {
    SceCtrlData pad;
    sceCtrlPeekBufferPositive(&pad, 1);
    return (int32_t)pad.Buttons;
}

static bool prl_psp_button_pressed(int32_t button) {
    SceCtrlData pad;
    sceCtrlPeekBufferPositive(&pad, 1);
    return (pad.Buttons & (unsigned int)button) != 0;
}

static inline void prl_thread_sleep(int32_t ms) { sceKernelDelayThread(ms * 1000); }

static inline bool prl_console_key_available(void) { return false; }

static inline int32_t prl_console_read_key(void) { return -1; }

static inline void prl_console_hide_cursor(void) {}

static inline void prl_console_set_cursor(int32_t x, int32_t y) {
    prl_psp_text_x = x * 8;
    prl_psp_text_y = y * 10;
}

static inline void prl_console_set_color(int32_t color) {
    /* PSP: 0x00BBGGRR. Map a 0-15 console index to a plain colour. */
    static const uint32_t palette[16] = {
        0x00000000,0x00000080,0x00008000,0x00008080,
        0x00800000,0x00800080,0x00808000,0x00C0C0C0,
        0x00808080,0x000000FF,0x0000FF00,0x0000FFFF,
        0x00FF0000,0x00FF00FF,0x00FFFF00,0x00FFFFFF
    };
    if (color >= 0 && color < 16) prl_psp_text_color = palette[color];
}

static inline void prl_console_reset_color(void) {
    prl_psp_text_color = 0xFFFFFFFF;
}

#elif defined(_WIN32)
#  ifndef WIN32_LEAN_AND_MEAN
#    define WIN32_LEAN_AND_MEAN
#  endif
#  include <windows.h>
#  include <conio.h>

static inline void prl_thread_sleep(int32_t ms) { Sleep((DWORD)ms); }

static inline bool prl_console_key_available(void) { return _kbhit()!=0; }

static inline int32_t prl_console_read_key(void) {
    int c=_getch();
    if(c==0||c==0xE0){
        c=_getch();
        switch(c){
            case 72: return 38; /* UP    */
            case 80: return 40; /* DOWN  */
            case 75: return 37; /* LEFT  */
            case 77: return 39; /* RIGHT */
        }
        return c;
    }
    return c;
}

static inline void prl_console_hide_cursor(void) {
    HANDLE h=GetStdHandle(STD_OUTPUT_HANDLE);
    CONSOLE_CURSOR_INFO ci; ci.dwSize=1; ci.bVisible=FALSE;
    SetConsoleCursorInfo(h,&ci);
}
static inline void prl_console_set_cursor(int32_t x, int32_t y) {
    HANDLE h=GetStdHandle(STD_OUTPUT_HANDLE);
    COORD c; c.X=(SHORT)x; c.Y=(SHORT)y;
    SetConsoleCursorPosition(h,c);
}
static inline void prl_console_set_color(int32_t color) {
    HANDLE h=GetStdHandle(STD_OUTPUT_HANDLE);
    SetConsoleTextAttribute(h,(WORD)color);
}
static inline void prl_console_reset_color(void) {
    HANDLE h=GetStdHandle(STD_OUTPUT_HANDLE);
    SetConsoleTextAttribute(h,7);
}

#else /* POSIX */
#  include <unistd.h>
#  include <termios.h>
#  include <sys/select.h>
#  include <sys/time.h>

static inline void prl_thread_sleep(int32_t ms) { usleep((suseconds_t)ms*1000u); }

static inline bool prl_console_key_available(void) {
    if (!isatty(STDIN_FILENO)) return false;
    struct timeval tv; tv.tv_sec=0; tv.tv_usec=0;
    fd_set fds; FD_ZERO(&fds); FD_SET(STDIN_FILENO,&fds);
    return select(STDIN_FILENO+1,&fds,NULL,NULL,&tv)>0;
}

static inline int32_t prl_console_read_key(void) {
    if (!isatty(STDIN_FILENO)) return -1;
    struct termios old,newt;
    tcgetattr(STDIN_FILENO,&old);
    newt=old;
    newt.c_lflag &= (tcflag_t)~(ICANON|ECHO);
    newt.c_cc[VMIN]=1; newt.c_cc[VTIME]=0;
    tcsetattr(STDIN_FILENO,TCSANOW,&newt);
    unsigned char c;
    if (read(STDIN_FILENO,&c,1)!=1) {
        tcsetattr(STDIN_FILENO,TCSANOW,&old);
        return -1;
    }
    if (c==27) {
        struct timeval tv; tv.tv_sec=0; tv.tv_usec=50000;
        fd_set fds; FD_ZERO(&fds); FD_SET(STDIN_FILENO,&fds);
        if (select(STDIN_FILENO+1,&fds,NULL,NULL,&tv)>0) {
            unsigned char c2;
            if (read(STDIN_FILENO,&c2,1)==1 && c2=='[') {
                tv.tv_sec=0; tv.tv_usec=50000;
                FD_ZERO(&fds); FD_SET(STDIN_FILENO,&fds);
                if (select(STDIN_FILENO+1,&fds,NULL,NULL,&tv)>0) {
                    unsigned char c3;
                    if (read(STDIN_FILENO,&c3,1)==1) {
                        tcsetattr(STDIN_FILENO,TCSANOW,&old);
                        switch(c3){
                            case 'A': return 38; /* UP    */
                            case 'B': return 40; /* DOWN  */
                            case 'C': return 39; /* RIGHT */
                            case 'D': return 37; /* LEFT  */
                        }
                        return (int32_t)c3;
                    }
                }
            }
        }
        tcsetattr(STDIN_FILENO,TCSANOW,&old);
        return 27;
    }
    tcsetattr(STDIN_FILENO,TCSANOW,&old);
    return (int32_t)c;
}

static inline void prl_console_hide_cursor(void) { printf("\033[?25l"); fflush(stdout); }
static inline void prl_console_set_cursor(int32_t x, int32_t y) {
    printf("\033[%d;%dH",y+1,x+1); fflush(stdout);
}
static inline void prl_console_set_color(int32_t color) {
    static const char* const ansi[16]={
        "\033[30m","\033[34m","\033[32m","\033[36m",
        "\033[31m","\033[35m","\033[33m","\033[37m",
        "\033[90m","\033[94m","\033[92m","\033[96m",
        "\033[91m","\033[95m","\033[93m","\033[97m"
    };
    if(color>=0&&color<16){ printf("%s",ansi[color]); fflush(stdout); }
}
static inline void prl_console_reset_color(void) { printf("\033[0m"); fflush(stdout); }

#endif /* _WIN32 / PSP / POSIX */

static inline void prl_console_write(PrlString s) {
#if defined(__PSP__)
    prl_psp_start_frame();
    prl_psp_draw_text(prl_psp_text_x, prl_psp_text_y, s, prl_psp_text_color);
    prl_psp_text_x += s.len * 8;
    prl_psp_swap_buffers();
#else
    fwrite(s.data,1,(size_t)s.len,stdout); fflush(stdout);
#endif
}

#endif /* PROLANG_RUNTIME_H */