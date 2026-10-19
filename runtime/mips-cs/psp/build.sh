#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
root="$PWD"
export PSPDEV="${PSPDEV:-$HOME/pspdev-tmp/pspdev}"
export PATH="$PSPDEV/bin:$PATH"
command -v psp-gcc >/dev/null || { echo 'Set PSPDEV to your PSP SDK directory' >&2; exit 1; }
cd "$root/artifacts/mips-cs/psp"
make -f "$root/runtime/mips-cs/psp/Makefile.psp" RUNTIME_DIR="$root/runtime/mips-cs/psp"
psp-objdump -d prolang-csharp-runtime.elf > runtime-mips.txt
