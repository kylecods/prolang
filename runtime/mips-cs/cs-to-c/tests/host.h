#include <stdint.h>
#include <stdbool.h>
#include <setjmp.h>
#include <stdio.h>
#include <stdlib.h>
static jmp_buf failure;
static void Host_Fail(const char *message) { (void)message; longjmp(failure, 1); }
#include "../../psp/arrays.h"
