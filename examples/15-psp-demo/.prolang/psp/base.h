#ifndef BASE_H
#define BASE_H

#include <stdint.h>
#include <string.h>

#define True 1
#define False 2

//Base types
typedef uint8_t UINT8;
typedef uint16_t UINT16;
typedef uint32_t UINT32;
typedef uint64_t UINT64;
typedef int8_t INT8;
typedef int16_t INT16;
typedef int32_t INT32;
typedef int64_t INT64;
typedef float F32;
typedef double F64;

//booleans
typedef INT8 B8;
typedef INT16 B16;
typedef INT32 B32;
typedef INT64 B64;


static const UINT64 MAX_U64_VALUE = 0xffffffffffffffffull;
static const UINT32 MAX_U32_VALUE = 0xffffffff;
static const UINT16 MAX_U16_VALUE = 0xffff;
static const UINT8 MAX_U8_VALUE = 0xff;

static const INT64 MAX_I64_VALUE = (INT64)0x7fffffffffffffffll;
static const INT32 MAX_I32_VALUE = (INT32)0x7fffffff;
static const INT16 MAX_I16_VALUE = (INT16)0x7fff;
static const INT8  MAX_I8_VALUE  =  (INT8)0x7f;

static const INT64 MIN_I64_VALUE = (INT64)0x8000000000000000ll;
static const INT32 MIN_I32_VALUE = (INT32)0x80000000;
static const INT16 MIN_I16_VALUE = (INT16)0x8000;
static const INT8  MIN_I8_VALUE  =  (INT8)0x80;


#define MIN(A,B) (((A)<(B)) ? (A):(B))
#define MAX(A,B) (((A)>(B)) ? (A):(B))

#define ClampTop(a,b) MIN(a,b)
#define ClampBottom(a,b) MAX(a,b)

#define AlignUpPow2(x,p) (((x) + (p) - 1)&~((p) - 1))
#define AlignDownPow2(x,p) ((x)&~((p) - 1))

#define KB(x) ((x) << 10)
#define MB(x) ((x) << 20)
#define GB(x) ((x) << 30)
#define TB(x) ((x) << 40)

#endif //BASE_H