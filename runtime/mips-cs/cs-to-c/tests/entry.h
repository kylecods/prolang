int main(void) {
    if (Test_Ordered() != 54 || Test_Defaults() != 0 || Test_Compound() != 3 || !Test_Promotion()) { return 1; }
    if (!setjmp(failure)) { Test_ReadBad(); return 2; }
    if (!setjmp(failure)) { Test_WriteBad(); return 3; }
    if (!setjmp(failure)) { Test_RefBad(); return 4; }
    if (!setjmp(failure)) { Test_NullBad(); return 5; }
    puts("translator array and evaluation tests passed");
    return 0;
}
