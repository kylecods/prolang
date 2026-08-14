CC ?= cc
CFLAGS = -std=c99 -Wall -O2

03_control_flow: 03_control_flow.c prolang_runtime.h
	$(CC) $(CFLAGS) -o $@ $< -lm

clean:
	rm -f 03_control_flow 03_control_flow.exe

.PHONY: clean
