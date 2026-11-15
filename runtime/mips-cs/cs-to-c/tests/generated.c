/* Generated from the portable C# core; do not edit. */
#include "host.h"
typedef struct Cell Cell;
struct Cell {
int32_t value;
};
static int32_t Test_counter;
int32_t Test_Next(void);
int32_t Test_Pair(int32_t a,int32_t b);
int32_t Test_Ordered(void);
int32_t Test_ReadBad(void);
void Test_WriteBad(void);
void Test_RefBad(void);
int32_t Test_NullBad(void);
int32_t Test_Defaults(void);
#line 4 "C:/Users/Kyle/Documents/programs/prolang/runtime/mips-cs/cs-to-c/tests/core.txt"
int32_t Test_Next(void){
(Test_counter++);
return Test_counter;
}
#line 5 "C:/Users/Kyle/Documents/programs/prolang/runtime/mips-cs/cs-to-c/tests/core.txt"
int32_t Test_Pair(int32_t a,int32_t b){
return ({ __auto_type pvm_tmp_0 = ((int32_t)(({ __auto_type pvm_tmp_2 = ((int32_t)(a)); __auto_type pvm_tmp_3 = ((int32_t)(10)); pvm_tmp_2 * pvm_tmp_3; }))); __auto_type pvm_tmp_1 = ((int32_t)(b)); pvm_tmp_0 + pvm_tmp_1; });
}
#line 6 "C:/Users/Kyle/Documents/programs/prolang/runtime/mips-cs/cs-to-c/tests/core.txt"
int32_t Test_Ordered(void){
({ __auto_type pvm_tmp_4 = &(Test_counter); __auto_type pvm_tmp_5 = 0; *pvm_tmp_4 = pvm_tmp_5; });
int32_t* a = (int32_t*)pvm_array(4, sizeof(int32_t));
({ __auto_type pvm_tmp_6 = &((*({ __auto_type pvm_tmp_8 = a; __auto_type pvm_tmp_9 = ({ Test_Next(); }); (int32_t*)pvm_at(pvm_tmp_8, pvm_tmp_9, sizeof(int32_t)); }))); __auto_type pvm_tmp_7 = ({ Test_Next(); }); *pvm_tmp_6 = pvm_tmp_7; });
int32_t* r = (&(*({ __auto_type pvm_tmp_10 = a; __auto_type pvm_tmp_11 = 1; (int32_t*)pvm_at(pvm_tmp_10, pvm_tmp_11, sizeof(int32_t)); })));
({ __auto_type pvm_tmp_12 = &((*r)); __auto_type pvm_tmp_14 = *pvm_tmp_12; __auto_type pvm_tmp_13 = ({ Test_Next(); }); *pvm_tmp_12 = pvm_tmp_14 + pvm_tmp_13; });
Cell* cells = (Cell*)pvm_array(1, sizeof(Cell));
({ __auto_type pvm_tmp_15 = &((*({ __auto_type pvm_tmp_17 = cells; __auto_type pvm_tmp_18 = 0; (Cell*)pvm_at(pvm_tmp_17, pvm_tmp_18, sizeof(Cell)); })).value); __auto_type pvm_tmp_16 = (*r); *pvm_tmp_15 = pvm_tmp_16; });
return ({ __auto_type pvm_tmp_19 = (*({ __auto_type pvm_tmp_20 = cells; __auto_type pvm_tmp_21 = 0; (Cell*)pvm_at(pvm_tmp_20, pvm_tmp_21, sizeof(Cell)); })).value; __auto_type pvm_tmp_22 = ({ Test_Next(); }); Test_Pair(pvm_tmp_19, pvm_tmp_22); });
}
#line 16 "C:/Users/Kyle/Documents/programs/prolang/runtime/mips-cs/cs-to-c/tests/core.txt"
int32_t Test_ReadBad(void){
int32_t* a = (int32_t*)pvm_array(1, sizeof(int32_t));
return (*({ __auto_type pvm_tmp_23 = a; __auto_type pvm_tmp_24 = 1; (int32_t*)pvm_at(pvm_tmp_23, pvm_tmp_24, sizeof(int32_t)); }));
}
#line 17 "C:/Users/Kyle/Documents/programs/prolang/runtime/mips-cs/cs-to-c/tests/core.txt"
void Test_WriteBad(void){
int32_t* a = (int32_t*)pvm_array(1, sizeof(int32_t));
({ __auto_type pvm_tmp_25 = &((*({ __auto_type pvm_tmp_27 = a; __auto_type pvm_tmp_28 = (-1); (int32_t*)pvm_at(pvm_tmp_27, pvm_tmp_28, sizeof(int32_t)); }))); __auto_type pvm_tmp_26 = 3; *pvm_tmp_25 = pvm_tmp_26; });
}
#line 18 "C:/Users/Kyle/Documents/programs/prolang/runtime/mips-cs/cs-to-c/tests/core.txt"
void Test_RefBad(void){
int32_t* a = (int32_t*)pvm_array(0, sizeof(int32_t));
int32_t* r = (&(*({ __auto_type pvm_tmp_29 = a; __auto_type pvm_tmp_30 = 0; (int32_t*)pvm_at(pvm_tmp_29, pvm_tmp_30, sizeof(int32_t)); })));
({ __auto_type pvm_tmp_31 = &((*r)); __auto_type pvm_tmp_32 = 3; *pvm_tmp_31 = pvm_tmp_32; });
}
#line 19 "C:/Users/Kyle/Documents/programs/prolang/runtime/mips-cs/cs-to-c/tests/core.txt"
int32_t Test_NullBad(void){
int32_t* a = ((void*)0);
return (*({ __auto_type pvm_tmp_33 = a; __auto_type pvm_tmp_34 = 0; (int32_t*)pvm_at(pvm_tmp_33, pvm_tmp_34, sizeof(int32_t)); }));
}
#line 20 "C:/Users/Kyle/Documents/programs/prolang/runtime/mips-cs/cs-to-c/tests/core.txt"
int32_t Test_Defaults(void){
int32_t* a = (int32_t*)pvm_array(2, sizeof(int32_t));
return ({ __auto_type pvm_tmp_35 = ((int32_t)((*({ __auto_type pvm_tmp_37 = a; __auto_type pvm_tmp_38 = 0; (int32_t*)pvm_at(pvm_tmp_37, pvm_tmp_38, sizeof(int32_t)); })))); __auto_type pvm_tmp_36 = ((int32_t)((*({ __auto_type pvm_tmp_39 = a; __auto_type pvm_tmp_40 = 1; (int32_t*)pvm_at(pvm_tmp_39, pvm_tmp_40, sizeof(int32_t)); })))); pvm_tmp_35 + pvm_tmp_36; });
}

#include "entry.h"
