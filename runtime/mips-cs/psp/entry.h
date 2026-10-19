int main(void) {
    prl_host_init(); prl_host_gc_init();
    /* Keep production collection work bounded instead of accumulating seconds of
     * tiny temporary strings. The shared V collector retains its own default. */
    prl_gc_growth = 16 * 1024; prl_gc_threshold = 16 * 1024;
    FILE *natural = fopen("natural-gc.txt", "r");
    if (natural) { pvm_natural_gc = 1; fclose(natural); }
    int status = 1;
    if (!setjmp(pvm_failure)) {
        int size = prl_host_size("program.dll");
        if (size < 1) Host_Fail("cannot read program.dll");
        unsigned char *data = Host_Perm(size);
        if (prl_host_read("program.dll", data, size) != size) Host_Fail("assembly read failed");
        int enabled = prl_host_jit_enabled();
        if (prl_log) { fprintf(prl_log, "RUNTIME csharp-portable\nENGINE %s\n", enabled ? "jit" : "interpreter"); fflush(prl_log); }
        VM_Run(data, size, enabled);
        if (prl_log) { fprintf(prl_log, "JIT total_code_bytes=%d\n", pvm_code_bytes); fflush(prl_log); }
        status = 0;
    }
    prl_host_finish(status); return status;
}
