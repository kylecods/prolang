#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
export PSPDEV="${PSPDEV:-$HOME/pspdev-tmp/pspdev}"
export PATH="$PSPDEV/bin:$PATH"
command -v psp-gcc >/dev/null || { echo "Set PSPDEV to your PSP toolchain directory" >&2; exit 1; }
root="$PWD"
optimization="${1:-3}"
case "$optimization" in s|2|3) ;; *) echo 'Optimization must be s, 2 or 3' >&2; exit 1 ;; esac
cd artifacts/msil-psp
make -f "$root/runtime/psp-v/Makefile.psp" RUNTIME_DIR="$root/runtime/psp-v" OPT="-O$optimization"
