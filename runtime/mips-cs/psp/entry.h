/* MIPS frames are native pointers into the fixed VM value stack. Portable
 * helpers receive checked indices; no pointer is stored in a managed value. */
static int pvm_frame_index(const Value *frame) {
    uintptr_t address = (uintptr_t)frame, base = (uintptr_t)Interpreter_Memory;
    size_t bytes = (size_t)(pvm_length(Interpreter_Memory) < Limits_ValueStackSlots ? pvm_length(Interpreter_Memory) : Limits_ValueStackSlots) * sizeof(Value);
    if (address < base || address - base >= bytes || (address - base) % sizeof(Value) != 0) { Host_Fail("invalid JIT frame"); }
    return (int)((address - base) / sizeof(Value));
}
static void pvm_enter(int mid, Value *frame) { Interpreter_Enter(mid, pvm_frame_index(frame)); }
static void pvm_leave(int mid, Value *frame) { Interpreter_Leave(mid, pvm_frame_index(frame)); }
static int pvm_slow(int mid, int pc, Value *frame) { return Interpreter_Slow(mid, pc, pvm_frame_index(frame)); }
static Value *pvm_array_element(int handle, int index) {
    int checked = Objects_ValidateArray(handle, index);
    HeapObject *object = (HeapObject *)pvm_at(Heap_HandleTable, checked, sizeof(HeapObject));
    return (Value *)pvm_at(object->Values, index, sizeof(Value));
}
static Value *pvm_field_element(int handle, int field) {
    int slot = Objects_ValidateField(handle, field);
    HeapObject *object = (HeapObject *)pvm_at(Heap_HandleTable, handle, sizeof(HeapObject));
    return (Value *)pvm_at(object->Values, slot, sizeof(Value));
}
static int32_t Host_GlobalSlot(int index) {
    if (index < Limits_ValueStackSlots || index >= pvm_length(Interpreter_Memory)) { Host_Fail("invalid global slot"); }
    return (int32_t)(intptr_t)pvm_at(Interpreter_Memory, index, sizeof(Value));
}
/* Helper numbers are the JitAbi constants. Helper 4, JitAbi_HelperTrap, is the last arm: pvm_trap. */
static int32_t Host_Helper(int number) {
    return (int32_t)(intptr_t)(number == JitAbi_HelperEnter ? (void *)pvm_enter : number == JitAbi_HelperLeave ? (void *)pvm_leave : number == JitAbi_HelperSlow ? (void *)pvm_slow : number == JitAbi_HelperArrayElement ? (void *)pvm_array_element : number == JitAbi_HelperFieldElement ? (void *)pvm_field_element : number == JitAbi_HelperArrayLength ? (void *)Objects_ArrayLength : (void *)pvm_trap);
}
static void Host_Call(int mid, int frame, void *result) {
    if ((unsigned)mid >= PVM_METHOD_LIMIT || !pvm_methods[mid] || (unsigned)frame >= (unsigned)Limits_ValueStackSlots || (unsigned)frame >= (unsigned)pvm_length(Interpreter_Memory)) { Host_Fail("invalid JIT call"); }
    ((void (*)(void *, void *))pvm_methods[mid])(Interpreter_Memory + frame, result);
}
int main(void) {
    prl_host_init();
    int status = 1;
    if (!setjmp(pvm_failure)) {
        int size = prl_host_size("program.dll");
        if (size < 1) Host_Fail("cannot read program.dll");
        unsigned char *data = pvm_array(size, sizeof(uint8_t));
        pvm_input = data;
        if (prl_host_read("program.dll", data, size) != size) Host_Fail("assembly read failed");
        int enabled = prl_host_jit_enabled();
        if (prl_log) { fprintf(prl_log, "RUNTIME csharp-portable\nENGINE %s\n", enabled ? "jit" : "interpreter"); fflush(prl_log); }
        Runtime_Run(data, size, enabled);
        if (prl_log) { fprintf(prl_log, "JIT total_code_bytes=%d\n", pvm_code_bytes); fflush(prl_log); }
        status = 0;
    }
    prl_host_finish(status); return status;
}
