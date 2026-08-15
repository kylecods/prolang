#ifndef PROLANG_RUNTIME_H
#define PROLANG_RUNTIME_H

/* Ensure POSIX extensions (usleep, nanosleep, etc.) are visible on glibc with -std=c99 */
#if !defined(_DEFAULT_SOURCE)
#  define _DEFAULT_SOURCE
#endif

#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* PSP (newlib) sometimes needs explicit declarations even after <string.h>. */
#ifdef __PSP__
extern void *memcpy(void *, const void *, size_t);
extern int   memcmp(const void *, const void *, size_t);
#endif

#ifdef __PSP__
#  include <pspkernel.h>
#  include <pspdebug.h>
#  include <pspdisplay.h>
#  include <pspctrl.h>
#  include <pspgu.h>
#  include <pspgum.h>
#endif

/* Native data structures (arena-backed memory, strings, vectors) */
#include "base.h"
#include "arena.h"
#include "strings.h"
#include "vector.h"

/* ProLang runtime layers built on the native data structures */
#include "prl_memory.h"
#include "prl_types.h"
#include "prl_array.h"
#include "prl_string.h"
#include "prl_psp.h"
#include "prl_io.h"
#include "prl_console.h"

#endif /* PROLANG_RUNTIME_H */