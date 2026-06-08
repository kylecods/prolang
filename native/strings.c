#include "strings.h"

UINT64 Cstring_8_Length(UINT8 *cstr)
{
    UINT64 length = 0;

    if(cstr) {
        UINT8 *p = cstr;
        for(;*p !=0; p+=1);
        length = (UINT64)(p - cstr);
    }
    return length;
}

String8 Str_8(UINT8 *str, UINT64 size)
{
    String8 result = {str, size};

    return(result);
}

String8 Str_8_Range(UINT8 *first, UINT8 *opl)
{
    String8 result = {first, (UINT64)(opl - first)};

    return(result);
}

String8 Str_8_Cstring(UINT8 *cstr)
{
    String8 result = {(UINT8*)cstr, (UINT64)Cstring_8_Length(cstr)};
    return (result);
}

String8 Str_8_Prefix(String8 str, UINT64 size)
{
    UINT64 size_clamped = ClampTop(size,str.size);
    String8 result = 
    {
        str.str,
        size_clamped
    };
    return (result);
}

String8 Str_8_Chop(String8 str, UINT64 amount)
{
    UINT64 amount_clamped = ClampTop(amount,str.size);
    UINT64 remaining_size = str.size - amount_clamped;

    String8 result = {str.str, remaining_size};

    return(result);
}

String8 Str_8_Postfix(String8 str, UINT64 size)
{
    UINT64 size_clamped = ClampTop(size,str.size);
    UINT64 skip_to = str.size - size_clamped;
    String8 result = {str.str + skip_to, size_clamped};
    return (result);
}

String8 Str_8_Skip(String8 str, UINT64 amount)
{
    UINT64 amount_clamped = ClampTop(amount, str.size);
    UINT64 remaing_size = str.size - amount_clamped;

    String8 result = {str.str + amount_clamped, remaing_size};

    return(result);
}

String8 Str8_Substr(String8 str, UINT64 first, UINT64 opl)
{
    String8 result = {0};
    return (result);
}

String8 Str_8_Join(Arena *arena, String8List *list, StringJoin *opt_join)
{
    static StringJoin dummy_join = {0};
    StringJoin *join = opt_join;

    if(join == 0)
    {
        join = &dummy_join;
    }

    UINT64 size = (join->pre.size + 
                    join->post.size +
                    join->mid.size*(list->nodeCount - 1) +
                    list->totalSize);

    String8 result = {0};
    

    return result;
}
