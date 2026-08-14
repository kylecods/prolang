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
#endif

/* ── String type ── */
typedef struct { char* data; int32_t len; } PrlString;

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
    char* buf=(char*)malloc((size_t)(s.len+1));
    if(!buf)return;
    memcpy(buf,s.data,(size_t)s.len);
    buf[s.len]='\0';
    pspDebugScreenPrintf("%s\n",buf);
    free(buf);
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

static inline void prl_thread_sleep(int32_t ms) { sceKernelDelayThread(ms * 1000); }

static inline bool prl_console_key_available(void) { return false; }

static inline int32_t prl_console_read_key(void) { return -1; }

static inline void prl_console_hide_cursor(void) {}

static inline void prl_console_set_cursor(int32_t x, int32_t y) {
    pspDebugScreenSetXY(x, y);
}

static inline void prl_console_set_color(int32_t color) {
    /* PSP: 0x00BBGGRR format, color param is index 0-15, map to greyscale intensity */
    uint32_t c = 0x00FFFFFF;
    if (color >= 0 && color <= 7) c = 0x00AAAAAA; /* bright */
    if (color >= 8) c = 0x00FFFFFF;                /* bright white */
    pspDebugScreenSetTextColor(c);
}

static inline void prl_console_reset_color(void) {
    pspDebugScreenSetTextColor(0x00FFFFFF);
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
    char* buf=(char*)malloc((size_t)(s.len+1));
    if(!buf)return;
    memcpy(buf,s.data,(size_t)s.len);
    buf[s.len]='\0';
    pspDebugScreenPrintf("%s",buf);
    free(buf);
#else
    fwrite(s.data,1,(size_t)s.len,stdout); fflush(stdout);
#endif
}

#endif /* PROLANG_RUNTIME_H */