#ifndef PRL_TEST_H
#define PRL_TEST_H

#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include "prl_types.h"
#include "prl_string.h"

/* ── Assertions ──
 *
 * Backs the `assert` builtin from `import "test"`.
 *
 * Named prl_assert rather than assert: <assert.h> defines `assert` as a macro, so a translation
 * unit that includes it — directly or through another header — would expand the generated call
 * and fail to compile.
 *
 * On the PSP there is no stderr worth writing to and exit() does not return control to a shell,
 * so the message goes through the GU font renderer that print() already uses, and the program is
 * left in a stopped state rather than exiting.
 */
#if defined(__PSP__)
static inline void prl_assert(bool condition, PrlString message) {
    if (condition) { return; }
    prl_psp_print(prl_string_from_lit("assertion failed:"));
    prl_psp_print(message);
    for (;;) { prl_psp_vsync(); }
}
#else
static inline void prl_assert(bool condition, PrlString message) {
    if (condition) { return; }
    fflush(stdout);
    fputs("assertion failed: ", stderr);
    fwrite(message.data, 1, (size_t)message.len, stderr);
    fputc('\n', stderr);
    fflush(stderr);
    exit(1);
}
#endif

#endif /* PRL_TEST_H */
