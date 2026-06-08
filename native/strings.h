#ifndef BASE_STRINGS_H
#define BASE_STRINGS_H

#include "arena.h"


typedef struct 
{
    UINT8 *str;
    UINT64 size;
}String8;

typedef struct  
{
    UINT16 *str;
    UINT64 size;
}String16;

typedef struct {
    struct String8Node *next;
    String8 string;
}String8Node;

typedef struct 
{
    String8Node *first;
    String8Node *last;
    UINT64 nodeCount;
    UINT64 totalSize; 
}String8List;

typedef struct _stringJoin
{
    String8 pre;
    String8 mid;
    String8 post;
}StringJoin;


static UINT64 Cstring_8_Length(UINT8 *cstr);

static String8 Str_8(UINT8 *str, UINT64 size);
static String8 Str_8_Range(UINT8 *first, UINT8 *opl);
static String8 Str_8_Cstring(UINT8 *cstr);

#define str8_lit(s) Str_8((UINT8*)(s), sizeof(s) - 1)

static String8 Str_8_Prefix(String8 str, UINT64 size);
static String8 Str_8_Chop(String8 str, UINT64 amount);
static String8 Str_8_Postfix(String8 str, UINT64 size);
static String8 Str_8_Skip(String8 str, UINT64 amount);
static String8 Str8_Substr(String8 str, UINT64 first, UINT64 opl);

#define str8_expand(s) (INT32)((s).size), ((s).str)

static void Str_8_List_Push_Explicit(String8List *list, String8 string, String8Node *node_memory);
static void Str_8_List_Push(Arena *arena, String8List *list,String8 string);
static String8 Str_8_Join(Arena *arena, String8List *list, StringJoin *opt_join);
static String8List Str_8_Split(Arena *arena, String8 string, UINT8 *split_characters, UINT32 count);


#endif