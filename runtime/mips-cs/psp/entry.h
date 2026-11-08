/* MIPS frames are native pointers into the fixed VM value stack. Portable
 * helpers receive checked indices; no pointer is stored in a managed value. */
static int pvm_frame_index(const Val *frame) {
    uintptr_t address = (uintptr_t)frame, base = (uintptr_t)VM_memory;
    size_t bytes = (size_t)(pvm_length(VM_memory) < 16384 ? pvm_length(VM_memory) : 16384) * sizeof(Val);
    if (address < base || address - base >= bytes || (address - base) % sizeof(Val) != 0) { Host_Fail("invalid JIT frame"); }
    return (int)((address - base) / sizeof(Val));
}
static void pvm_enter(int mid, Val *frame) { VM_Enter(mid, pvm_frame_index(frame)); }
static void pvm_leave(int mid, Val *frame) { VM_Leave(mid, pvm_frame_index(frame)); }
static int pvm_slow(int mid, int pc, Val *frame) { return VM_Slow(mid, pc, pvm_frame_index(frame)); }
static Val *pvm_array_element(int handle, int index) {
    int checked = VM_ValidateArray(handle, index);
    Obj *object = (Obj *)pvm_at(VM_objects, checked, sizeof(Obj));
    return (Val *)pvm_at(object->values, index, sizeof(Val));
}
static Val *pvm_field_element(int handle, int field) {
    int slot = VM_ValidateField(handle, field);
    Obj *object = (Obj *)pvm_at(VM_objects, handle, sizeof(Obj));
    return (Val *)pvm_at(object->values, slot, sizeof(Val));
}
static int32_t Host_GlobalSlot(int index) {
    if (index < 16384 || index >= pvm_length(VM_memory)) { Host_Fail("invalid global slot"); }
    return (int32_t)(intptr_t)pvm_at(VM_memory, index, sizeof(Val));
}
static int32_t Host_Helper(int number) {
    return (int32_t)(intptr_t)(number == 1 ? (void *)pvm_enter : number == 2 ? (void *)pvm_leave : number == 3 ? (void *)pvm_slow : number == 5 ? (void *)pvm_array_element : number == 6 ? (void *)pvm_field_element : number == 7 ? (void *)VM_ArrayLength : (void *)pvm_trap);
}
static void Host_Call(int mid, int frame, void *result) {
    if ((unsigned)mid >= PVM_METHOD_LIMIT || !pvm_methods[mid] || (unsigned)frame >= 16384u || (unsigned)frame >= (unsigned)pvm_length(VM_memory)) { Host_Fail("invalid JIT call"); }
    ((void (*)(void *, void *))pvm_methods[mid])(VM_memory + frame, result);
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
        VM_Run(data, size, enabled);
        if (prl_log) { fprintf(prl_log, "JIT total_code_bytes=%d\n", pvm_code_bytes); fflush(prl_log); }
        status = 0;
    }
    prl_host_finish(status); return status;
}
