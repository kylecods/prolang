#ifndef PRL_TYPES_H
#define PRL_TYPES_H

#include "base.h"
#include "strings.h"

/* ── CEmitter-facing string type ── */
typedef struct { char* data; INT32 len; } PrlString;

/* Convert between PrlString (CEmitter contract) and String8 (native data structure). */
static inline PrlString PrlString_From_String8(String8 s) {
    PrlString r;
    r.data = (char*)s.str;
    r.len  = (INT32)s.size;
    return r;
}
static inline String8 String8_From_PrlString(PrlString s) {
    String8 r;
    r.str  = (UINT8*)s.data;
    r.size = (UINT64)s.len;
    return r;
}

/* ── Typed array structs for every built-in element type ── */
typedef struct { B32*       data; INT32 len; } PrlArray_bool;
typedef struct { INT8*      data; INT32 len; } PrlArray_int8_t;
typedef struct { INT16*     data; INT32 len; } PrlArray_int16_t;
typedef struct { INT32*     data; INT32 len; } PrlArray_int32_t;
typedef struct { INT64*     data; INT32 len; } PrlArray_int64_t;
typedef struct { UINT8*     data; INT32 len; } PrlArray_uint8_t;
typedef struct { UINT16*    data; INT32 len; } PrlArray_uint16_t;
typedef struct { UINT32*    data; INT32 len; } PrlArray_uint32_t;
typedef struct { UINT64*    data; INT32 len; } PrlArray_uint64_t;
typedef struct { F32*       data; INT32 len; } PrlArray_float;
typedef struct { F64*       data; INT32 len; } PrlArray_double;
typedef struct { PrlString* data; INT32 len; } PrlArray_PrlString;

#endif /* PRL_TYPES_H */