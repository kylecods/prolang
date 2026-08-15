#ifndef PRL_ARRAY_H
#define PRL_ARRAY_H

#include "prl_types.h"
#include "prl_memory.h"

/* ── Array constructors (allocated from the global arena) ── */
static inline PrlArray_bool     prl_array_new_bool    (INT32 n){ PrlArray_bool     a={(B32*)    prl_alloc_zero((UINT64)n*sizeof(B32)),     n}; return a; }
static inline PrlArray_int8_t   prl_array_new_int8_t  (INT32 n){ PrlArray_int8_t   a={(INT8*)   prl_alloc_zero((UINT64)n*sizeof(INT8)),    n}; return a; }
static inline PrlArray_int16_t  prl_array_new_int16_t (INT32 n){ PrlArray_int16_t  a={(INT16*)  prl_alloc_zero((UINT64)n*sizeof(INT16)),   n}; return a; }
static inline PrlArray_int32_t  prl_array_new_int32_t (INT32 n){ PrlArray_int32_t  a={(INT32*)  prl_alloc_zero((UINT64)n*sizeof(INT32)),   n}; return a; }
static inline PrlArray_int64_t  prl_array_new_int64_t (INT32 n){ PrlArray_int64_t  a={(INT64*)  prl_alloc_zero((UINT64)n*sizeof(INT64)),   n}; return a; }
static inline PrlArray_uint8_t  prl_array_new_uint8_t (INT32 n){ PrlArray_uint8_t  a={(UINT8*)  prl_alloc_zero((UINT64)n*sizeof(UINT8)),   n}; return a; }
static inline PrlArray_uint16_t prl_array_new_uint16_t(INT32 n){ PrlArray_uint16_t a={(UINT16*) prl_alloc_zero((UINT64)n*sizeof(UINT16)),  n}; return a; }
static inline PrlArray_uint32_t prl_array_new_uint32_t(INT32 n){ PrlArray_uint32_t a={(UINT32*) prl_alloc_zero((UINT64)n*sizeof(UINT32)),  n}; return a; }
static inline PrlArray_uint64_t prl_array_new_uint64_t(INT32 n){ PrlArray_uint64_t a={(UINT64*) prl_alloc_zero((UINT64)n*sizeof(UINT64)),  n}; return a; }
static inline PrlArray_float    prl_array_new_float   (INT32 n){ PrlArray_float    a={(F32*)    prl_alloc_zero((UINT64)n*sizeof(F32)),     n}; return a; }
static inline PrlArray_double   prl_array_new_double  (INT32 n){ PrlArray_double   a={(F64*)    prl_alloc_zero((UINT64)n*sizeof(F64)),     n}; return a; }
static inline PrlArray_PrlString prl_array_new_PrlString(INT32 n){ PrlArray_PrlString a={(PrlString*)prl_alloc_zero((UINT64)n*sizeof(PrlString)),n}; return a; }

#endif /* PRL_ARRAY_H */