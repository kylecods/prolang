#ifndef PRL_CONSOLE_H
#define PRL_CONSOLE_H

#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "prl_types.h"
#include "prl_string.h"

/* ── Console & platform ── */
#if defined(__PSP__)

#include "prl_psp.h"

static inline void prl_thread_sleep(INT32 ms) { sceKernelDelayThread(ms * 1000); }

/*
 * Milliseconds from the system clock, which counts microseconds since boot in a 32-bit register.
 *
 * That register wraps every 71 minutes on its own, and dividing by 1000 first keeps the
 * millisecond value inside a signed int for the full period between wraps. Callers subtract two
 * readings, and two's-complement subtraction survives the wrap.
 */
static inline INT32 prl_time_millis(void) {
    return (INT32)(sceKernelGetSystemTimeLow() / 1000u);
}
static inline bool prl_console_key_available(void) { return false; }
static inline INT32 prl_console_read_key(void) { return -1; }
static inline void prl_console_hide_cursor(void) {}
static inline void prl_console_set_cursor(INT32 x, INT32 y) {
    prl_psp_set_text_cursor(x, y);
}
static inline void prl_console_set_color(INT32 color) {
    prl_psp_set_text_color(color);
}
static inline void prl_console_reset_color(void) {
    prl_psp_reset_text_color();
}

static inline void prl_console_write(PrlString s) {
    prl_psp_console_write(s);
}

#elif defined(_WIN32)
#  ifndef WIN32_LEAN_AND_MEAN
#    define WIN32_LEAN_AND_MEAN
#  endif
#  include <windows.h>
#  include <conio.h>

static inline void prl_thread_sleep(INT32 ms) { Sleep((DWORD)ms); }

/*
 * Milliseconds from the performance counter rather than from GetTickCount.
 *
 * GetTickCount advances in steps of about fifteen milliseconds, which is most of a frame at sixty
 * per second: a frame-rate counter built on it reports a handful of fixed values instead of a
 * measurement. The performance counter is monotonic and fine-grained.
 */
static inline INT32 prl_time_millis(void) {
    LARGE_INTEGER freq, now;
    if (!QueryPerformanceFrequency(&freq) || freq.QuadPart == 0) return (INT32)GetTickCount();
    QueryPerformanceCounter(&now);
    return (INT32)((now.QuadPart * 1000LL) / freq.QuadPart);
}
static inline bool prl_console_key_available(void) { return _kbhit()!=0; }

static inline INT32 prl_console_read_key(void) {
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
static inline void prl_console_set_cursor(INT32 x, INT32 y) {
    HANDLE h=GetStdHandle(STD_OUTPUT_HANDLE);
    COORD c; c.X=(SHORT)x; c.Y=(SHORT)y;
    SetConsoleCursorPosition(h,c);
}
static inline void prl_console_set_color(INT32 color) {
    HANDLE h=GetStdHandle(STD_OUTPUT_HANDLE);
    SetConsoleTextAttribute(h,(WORD)color);
}
static inline void prl_console_reset_color(void) {
    HANDLE h=GetStdHandle(STD_OUTPUT_HANDLE);
    SetConsoleTextAttribute(h,7);
}
static inline void prl_console_write(PrlString s) {
    fwrite(s.data,1,(size_t)s.len,stdout); fflush(stdout);
}

#else /* POSIX */
#  if !defined(_DEFAULT_SOURCE)
#    define _DEFAULT_SOURCE
#  endif
#  ifndef _BSD_SOURCE
#    define _BSD_SOURCE
#  endif
#  include <unistd.h>
#  include <termios.h>
#  include <sys/select.h>
#  include <sys/time.h>
#  include <time.h>

static inline void prl_thread_sleep(INT32 ms) { usleep((suseconds_t)ms*1000u); }

/*
 * Milliseconds from the monotonic clock, so that an interval can never come out negative because
 * the wall clock was adjusted underneath it.
 */
static inline INT32 prl_time_millis(void) {
    struct timespec ts;
    if (clock_gettime(CLOCK_MONOTONIC, &ts) != 0) return 0;
    return (INT32)(((INT64)ts.tv_sec * 1000) + (ts.tv_nsec / 1000000));
}

static inline bool prl_console_key_available(void) {
    if (!isatty(STDIN_FILENO)) return false;
    struct timeval tv; tv.tv_sec=0; tv.tv_usec=0;
    fd_set fds; FD_ZERO(&fds); FD_SET(STDIN_FILENO,&fds);
    return select(STDIN_FILENO+1,&fds,NULL,NULL,&tv)>0;
}

/* Uses read() directly — never getchar() — so select() and read() operate
   at the same fd level and the stdio buffer cannot desync from the kernel buffer. */
static inline INT32 prl_console_read_key(void) {
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
                        return (INT32)c3;
                    }
                }
            }
        }
        tcsetattr(STDIN_FILENO,TCSANOW,&old);
        return 27;
    }
    tcsetattr(STDIN_FILENO,TCSANOW,&old);
    return (INT32)c;
}

static inline void prl_console_hide_cursor(void) { printf("\033[?25l"); fflush(stdout); }
static inline void prl_console_set_cursor(INT32 x, INT32 y) {
    printf("\033[%d;%dH",y+1,x+1); fflush(stdout);
}
static inline void prl_console_set_color(INT32 color) {
    static const char* const ansi[16]={
        "\033[30m","\033[34m","\033[32m","\033[36m",
        "\033[31m","\033[35m","\033[33m","\033[37m",
        "\033[90m","\033[94m","\033[92m","\033[96m",
        "\033[91m","\033[95m","\033[93m","\033[97m"
    };
    if(color>=0&&color<16){ printf("%s",ansi[color]); fflush(stdout); }
}
static inline void prl_console_reset_color(void) { printf("\033[0m"); fflush(stdout); }
static inline void prl_console_write(PrlString s) {
    fwrite(s.data,1,(size_t)s.len,stdout); fflush(stdout);
}

#endif /* _WIN32 / PSP / POSIX */

#endif /* PRL_CONSOLE_H */
