#ifndef PRL_STRING_H
#define PRL_STRING_H

#include <stdio.h>
#include "prl_types.h"
#include "prl_memory.h"

/* ── PrlString helpers (backed by String8 + arena) ── */
static inline PrlString prl_string_from_lit(const char* s) {
    PrlString r; r.data=(char*)s; r.len=(INT32)strlen(s); return r;
}

static inline PrlString prl_string_concat(PrlString a, PrlString b) {
    String8 sa = String8_From_PrlString(a);
    String8 sb = String8_From_PrlString(b);
    UINT8* buf = (UINT8*)prl_alloc(sa.size + sb.size);
    if (!buf) return (PrlString){0};
    if (sa.size) memcpy(buf, sa.str, (size_t)sa.size);
    if (sb.size) memcpy(buf + sa.size, sb.str, (size_t)sb.size);
    return PrlString_From_String8(Str_8(buf, sa.size + sb.size));
}

static inline bool prl_string_equals(PrlString a, PrlString b) {
    return Str_8_Equals(String8_From_PrlString(a), String8_From_PrlString(b)) ? true : false;
}

static inline bool prl_string_not_equals(PrlString a, PrlString b) {
    return !prl_string_equals(a, b);
}

static inline INT32 prl_string_length(PrlString s) { return s.len; }

static inline PrlString prl_string_char_at(PrlString s, INT32 i) {
    if (i < 0 || i >= s.len) return prl_string_from_lit("");
    char* buf = (char*)prl_alloc(1);
    if (!buf) return prl_string_from_lit("");
    buf[0] = s.data[i];
    return (PrlString){buf, 1};
}

/*
 * The character at `i` as an integer, or 0 when out of range.
 *
 * Cast through unsigned char first: `char` is signed on most targets, so a byte above 127 would
 * otherwise arrive negative and index a width table out of bounds. The .NET side returns a UTF-16
 * code unit; for the ASCII range the two agree, which is what generated code relies on.
 */
static inline INT32 prl_string_char_code(PrlString s, INT32 i) {
    if (i < 0 || i >= s.len) return 0;
    return (INT32)(unsigned char)s.data[i];
}

static inline PrlString prl_string_substring(PrlString s, INT32 start, INT32 end) {
    if (start < 0) start = 0;
    if (end > s.len) end = s.len;
    INT32 len = end - start;
    if (len < 0) len = 0;
    char* buf = (char*)prl_alloc((UINT64)len);
    if (!buf) return prl_string_from_lit("");
    if (len) memcpy(buf, s.data + start, (size_t)len);
    return (PrlString){buf, len};
}

static inline INT32 prl_string_index_of(PrlString s, PrlString needle) {
    if (needle.len == 0) return 0;
    if (needle.len > s.len) return -1;
    for (INT32 i = 0; i <= s.len - needle.len; i++)
        if (memcmp(s.data + i, needle.data, (size_t)needle.len) == 0) return i;
    return -1;
}

/* ── Conversions to string ── */
static inline PrlString prl_int_to_string(INT64 v) {
    char buf[32]; int n = snprintf(buf, sizeof(buf), "%lld", (long long)v);
    char* d = (char*)prl_alloc((UINT64)(n + 1));
    if (!d) return prl_string_from_lit("");
    memcpy(d, buf, (size_t)(n + 1));
    return (PrlString){d, n};
}

static inline PrlString prl_bool_to_string(bool v) {
    return prl_string_from_lit(v ? "true" : "false");
}

static inline PrlString prl_float_to_string(F64 v) {
    char buf[64]; int n = snprintf(buf, sizeof(buf), "%g", v);
    char* d = (char*)prl_alloc((UINT64)(n + 1));
    if (!d) return prl_string_from_lit("");
    memcpy(d, buf, (size_t)(n + 1));
    return (PrlString){d, n};
}

static inline PrlString prl_any_to_string(void* v) {
    (void)v; return prl_string_from_lit("<object>");
}

#endif /* PRL_STRING_H */