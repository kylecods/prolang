#ifndef PROLANG_V_PSP_COMPAT_H
#define PROLANG_V_PSP_COMPAT_H
/* Newlib's ctype.h defines _S, which collides with V's string literal macro.
 * Load it before V defines its macros; subsequent includes are guarded. */
#include <ctype.h>
#undef _S
#endif
