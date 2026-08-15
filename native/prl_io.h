#ifndef PRL_IO_H
#define PRL_IO_H

#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#include "prl_types.h"
#include "prl_string.h"
#include "prl_array.h"
#include "prl_memory.h"

/* ── IO ── */
#if defined(__PSP__)
static inline void prl_print(PrlString s) { prl_psp_print(s); }
static inline PrlString prl_read_input(void) { return prl_string_from_lit(""); }
#else
static inline void prl_print(PrlString s) {
    fwrite(s.data,1,(size_t)s.len,stdout); putchar('\n'); fflush(stdout);
}
static inline PrlString prl_read_input(void) {
    char buf[4096];
    if(!fgets(buf,sizeof(buf),stdin)){ return prl_string_from_lit(""); }
    INT32 len=(INT32)strlen(buf);
    if(len>0&&buf[len-1]=='\n') buf[--len]='\0';
    char* d=(char*)malloc((size_t)(len+1)); memcpy(d,buf,(size_t)(len+1));
    PrlString r; r.data=d; r.len=len; return r;
}
#endif

/* ── Math ── */
static inline INT32 prl_random(INT32 max) { return max<=0?0:rand()%max; }
static inline INT32 prl_min_i(INT32 a, INT32 b) { return a<b?a:b; }
static inline INT32 prl_max_i(INT32 a, INT32 b) { return a>b?a:b; }

/* ── File system ── */
static inline bool prl_file_exists(PrlString path) {
    FILE* f=fopen(path.data,"r"); if(f){fclose(f);return true;} return false;
}
static inline PrlString prl_read_file(PrlString path) {
    FILE* f=fopen(path.data,"rb");
    if(!f) return prl_string_from_lit("");
    fseek(f,0,SEEK_END); long sz=ftell(f); fseek(f,0,SEEK_SET);
    char* d=(char*)prl_alloc((UINT64)(sz+1));
    if(!d){ fclose(f); return prl_string_from_lit(""); }
    fread(d,1,(size_t)sz,f); fclose(f); d[sz]='\0';
    PrlString r; r.data=d; r.len=(INT32)sz; return r;
}
static inline PrlArray_uint8_t prl_read_file_bytes(PrlString path) {
    FILE* f=fopen(path.data,"rb");
    PrlArray_uint8_t empty; empty.data=NULL; empty.len=0;
    if(!f) return empty;
    fseek(f,0,SEEK_END); long sz=ftell(f); fseek(f,0,SEEK_SET);
    UINT8* d=(UINT8*)prl_alloc((UINT64)sz);
    if(!d){ fclose(f); return empty; }
    fread(d,1,(size_t)sz,f); fclose(f);
    PrlArray_uint8_t r; r.data=d; r.len=(INT32)sz; return r;
}
static inline void prl_write_file(PrlString path, PrlString contents) {
    FILE* f=fopen(path.data,"wb"); if(!f) return;
    fwrite(contents.data,1,(size_t)contents.len,f); fclose(f);
}

#endif /* PRL_IO_H */
