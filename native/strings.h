#ifndef BASE_STRINGS_H
#define BASE_STRINGS_H

#include <string.h>
#include "arena.h"

typedef struct {
    UINT8 *str;
    UINT64 size;
} String8;

typedef struct {
    UINT16 *str;
    UINT64 size;
} String16;

typedef struct String8Node {
    struct String8Node *next;
    String8 string;
} String8Node;

typedef struct {
    String8Node *first;
    String8Node *last;
    UINT64 nodeCount;
    UINT64 totalSize;
} String8List;

typedef struct {
    String8 pre;
    String8 mid;
    String8 post;
} StringJoin;

static inline UINT64 Cstring_8_Length(UINT8 *cstr) {
    UINT64 length = 0;
    if (cstr) {
        UINT8 *p = cstr;
        for (; *p != 0; p += 1);
        length = (UINT64)(p - cstr);
    }
    return length;
}

static inline String8 Str_8(UINT8 *str, UINT64 size) {
    String8 result = {str, size};
    return result;
}

static inline String8 Str_8_Range(UINT8 *first, UINT8 *opl) {
    String8 result = {first, (UINT64)(opl - first)};
    return result;
}

static inline String8 Str_8_Cstring(UINT8 *cstr) {
    String8 result = {
        (UINT8*)cstr,
        (UINT64)Cstring_8_Length(cstr)
    };
    return result;
}

#define str8_lit(s) Str_8((UINT8*)(s), sizeof(s) - 1)

static inline String8 Str_8_Prefix(String8 str, UINT64 size) {
    UINT64 size_clamped = ClampTop(size, str.size);
    String8 result = {str.str, size_clamped};
    return result;
}

static inline String8 Str_8_Chop(String8 str, UINT64 amount) {
    UINT64 amount_clamped = ClampTop(amount, str.size);
    String8 result = {str.str, str.size - amount_clamped};
    return result;
}

static inline String8 Str_8_Postfix(String8 str, UINT64 size) {
    UINT64 size_clamped = ClampTop(size, str.size);
    String8 result = {str.str + (str.size - size_clamped), size_clamped};
    return result;
}

static inline String8 Str_8_Skip(String8 str, UINT64 amount) {
    UINT64 amount_clamped = ClampTop(amount, str.size);
    String8 result = {str.str + amount_clamped, str.size - amount_clamped};
    return result;
}

static inline String8 Str8_Substr(String8 str, UINT64 first, UINT64 opl) {
    if (first > str.size) first = str.size;
    if (opl > str.size) opl = str.size;
    if (first > opl) first = opl;
    String8 result = {str.str + first, opl - first};
    return result;
}

#define str8_expand(s) (INT32)((s).size), ((s).str)

static inline void Str_8_List_Push_Explicit(String8List *list, String8 string, String8Node *node) {
    node->string = string;
    node->next = NULL;
    if (list->last) {
        list->last->next = node;
    } else {
        list->first = node;
    }
    list->last = node;
    list->nodeCount++;
    list->totalSize += string.size;
}

static inline void Str_8_List_Push(Arena *arena, String8List *list, String8 string) {
    String8Node *node = (String8Node*)Arena_Push(arena, sizeof(String8Node));
    if (node) Str_8_List_Push_Explicit(list, string, node);
}

static inline String8 Str_8_Join(Arena *arena, String8List *list, StringJoin *opt_join) {
    static StringJoin dummy_join = {0};
    StringJoin *join = opt_join ? opt_join : &dummy_join;

    UINT64 total = join->pre.size + join->post.size;
    if (list->nodeCount > 0) total += list->totalSize;
    if (list->nodeCount > 1) total += join->mid.size * (list->nodeCount - 1);

    UINT8 *buf = (UINT8*)Arena_Push(arena, total);
    if (!buf) return (String8){0};

    UINT64 pos = 0;
    if (join->pre.size) { memcpy(buf + pos, join->pre.str, join->pre.size); pos += join->pre.size; }

    UINT64 first = 1;
    for (String8Node *node = list->first; node; node = node->next) {
        if (!first && join->mid.size) { memcpy(buf + pos, join->mid.str, join->mid.size); pos += join->mid.size; }
        first = 0;
        memcpy(buf + pos, node->string.str, node->string.size);
        pos += node->string.size;
    }
    if (join->post.size) { memcpy(buf + pos, join->post.str, join->post.size); pos += join->post.size; }

    return (String8){buf, total};
}

static inline String8List Str_8_Split(Arena *arena, String8 string, UINT8 *split_characters, UINT32 count) {
    String8List result = {0};
    UINT64 start = 0;
    for (UINT64 i = 0; i <= string.size; i++) {
        B32 is_split = 0;
        if (i < string.size) {
            for (UINT32 si = 0; si < count; si++) {
                if (string.str[i] == split_characters[si]) { is_split = 1; break; }
            }
        } else {
            is_split = 1; /* end of string acts as split */
        }
        if (is_split) {
            if (i > start) {
                Str_8_List_Push(arena, &result, Str_8(string.str + start, i - start));
            }
            start = i + 1;
        }
    }
    return result;
}

/* ── String comparison helpers ── */
static inline B32 Str_8_Equals(String8 a, String8 b) {
    return a.size == b.size && memcmp(a.str, b.str, (size_t)a.size) == 0;
}

static inline B32 Str_8_Not_Equals(String8 a, String8 b) {
    return !Str_8_Equals(a, b);
}

#endif /* BASE_STRINGS_H */