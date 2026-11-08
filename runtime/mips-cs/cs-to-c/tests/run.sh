#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../../.." && pwd)"
tests="$root/runtime/mips-cs/cs-to-c/tests"
out="$root/artifacts/mips-cs/translator-tests"
mkdir -p "$out"
dotnet run --project "$root/runtime/mips-cs/cs-to-c/CsToC.csproj" -c Release -- "$out/generated.c" "$tests/core.txt" "$tests/host-source.txt"
gcc -std=gnu11 -fwrapv -I"$tests" "$out/generated.c" -o "$out/test"
"$out/test"
printf '%s\n' 'public unsafe static class Bad { public static int* F(int* p) { return p; } }' > "$out/unsafe.cs"
if dotnet run --project "$root/runtime/mips-cs/cs-to-c/CsToC.csproj" -c Release -- "$out/bad.c" "$out/unsafe.cs" "$tests/host-source.txt"; then
    echo 'unsafe C# was accepted' >&2; exit 1
fi
printf '%s\n' 'public static class Bad { public static int F(int x) { return checked(x + 1); } }' > "$out/checked.cs"
if dotnet run --project "$root/runtime/mips-cs/cs-to-c/CsToC.csproj" -c Release -- "$out/bad.c" "$out/checked.cs" "$tests/host-source.txt"; then
    echo 'checked arithmetic was silently translated' >&2; exit 1
fi
printf '%s\n' 'public static class Bad { public static string F() { return "\u00e9"; } }' > "$out/nonascii.cs"
if dotnet run --project "$root/runtime/mips-cs/cs-to-c/CsToC.csproj" -c Release -- "$out/bad.c" "$out/nonascii.cs" "$tests/host-source.txt"; then
    echo 'non-ASCII string was silently translated' >&2; exit 1
fi
printf '%s\n' 'public static class Bad { public static string F() { return "\0"; } }' > "$out/nul.cs"
if dotnet run --project "$root/runtime/mips-cs/cs-to-c/CsToC.csproj" -c Release -- "$out/bad.c" "$out/nul.cs" "$tests/host-source.txt"; then
    echo 'embedded NUL string was silently translated' >&2; exit 1
fi
