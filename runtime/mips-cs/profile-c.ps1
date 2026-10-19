param([int]$Frames = 180, [string]$Distro = 'Ubuntu')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$out = "$repo/artifacts/mips-cs/c-baseline"
Push-Location $repo
try {
    & dotnet src/ProLang/bin/Release/net10.0/ProLang.dll examples/18-psp-cube/psp_cube.prl --emit-psp "--c-output=$out"
    if ($LASTEXITCODE) { throw 'C cube generation failed' }
    # Newlib's <string.h> includes <strings.h>; the generated project used that same
    # basename. Keep this experiment's generated header out of the system include path.
    Move-Item -LiteralPath "$out/strings.h" -Destination "$out/prl_strings.h" -Force
    foreach ($header in 'prolang_runtime.h','prl_types.h') {
        $headerPath = "$out/$header"
        [IO.File]::WriteAllText($headerPath, [IO.File]::ReadAllText($headerPath).Replace('"strings.h"','"prl_strings.h"'))
    }
    $path = "$out/prl_psp.h"
    $c = [IO.File]::ReadAllText($path)
    $declarations = @'
#include <pspgu.h>
static FILE *c_log;
static int c_frames, c_lines, c_rects, c_texts;
static unsigned c_hash = 2166136261u;
static SceInt64 c_start;
'@
    $c = $c.Replace('#include <pspgu.h>', $declarations)
    $c = $c.Replace('prl_psp_gu_ready = 1;', 'prl_psp_gu_ready = 1; c_log = fopen("runtime.log", "w"); c_start = sceKernelGetSystemTimeWide();')
    $c = $c.Replace('static void prl_psp_fill_rect(INT32 x, INT32 y, INT32 w, INT32 h, INT32 color) {', 'static void prl_psp_fill_rect(INT32 x, INT32 y, INT32 w, INT32 h, INT32 color) { c_rects++;')
    $c = $c.Replace('static void prl_psp_draw_text(INT32 x, INT32 y, PrlString s, INT32 color) {', 'static void prl_psp_draw_text(INT32 x, INT32 y, PrlString s, INT32 color) { c_texts++;')
    $c = $c.Replace('static void prl_psp_draw_line(INT32 x1, INT32 y1, INT32 x2, INT32 y2, INT32 color) {', 'static void prl_psp_draw_line(INT32 x1, INT32 y1, INT32 x2, INT32 y2, INT32 color) { c_lines++; c_hash = (c_hash ^ (unsigned)x1)*16777619u; c_hash = (c_hash ^ (unsigned)y1)*16777619u; c_hash = (c_hash ^ (unsigned)x2)*16777619u; c_hash = (c_hash ^ (unsigned)y2)*16777619u;')
    $swap = @"
static void prl_psp_swap_buffers(void) {
    int work = (int)(sceKernelGetSystemTimeWide() - c_start);
    prl_psp_present(1); c_frames++;
    if (c_log) {
        fprintf(c_log, "FRAME %d lines=%d rects=%d texts=%d geometry=%08x work_us=%d\n", c_frames,c_lines,c_rects,c_texts,c_hash,work);
        if (c_frames == $Frames) fprintf(c_log, "PROLANG OK\n");
        fflush(c_log);
    }
    c_lines=c_rects=c_texts=0; c_hash=2166136261u;
    c_start = sceKernelGetSystemTimeWide();
}
"@
    $c = [regex]::Replace($c, 'static void prl_psp_swap_buffers\(void\)\s*\{\s*prl_psp_present\(1\);\s*\}', [Text.RegularExpressions.MatchEvaluator]{ param($m) $swap })
    [IO.File]::WriteAllText($path, $c)
    $make = @'
TARGET = cube-c
.DEFAULT_GOAL := all
OBJS = psp_cube.o psp_main.o
CFLAGS = -O3 -G0 -std=gnu11 -ffunction-sections -fdata-sections
LDFLAGS = -Wl,--gc-sections
LIBS = -lpspgu -lpspge -lpspdisplay -lpspctrl -lm
BUILD_PRX = 1
EXTRA_TARGETS = EBOOT.PBP
PSP_EBOOT_TITLE = ProLang C cube baseline
PSPSDK := $(shell psp-config --pspsdk-path)
include $(PSPSDK)/lib/build.mak
'@
    [IO.File]::WriteAllText("$out/Makefile.profile", $make.Replace("`r`n", "`n"))
    $wslOut = (& wsl -d $Distro --exec wslpath -a $out).Trim()
    & wsl -d $Distro --exec bash -lc 'export PSPDEV="$HOME/pspdev-tmp/pspdev"; export PATH="$PSPDEV/bin:$PATH"; cd "$1"; make -f Makefile.profile' -- $wslOut
    if ($LASTEXITCODE) { throw 'C cube PSP build failed' }
    $portable = "$repo/artifacts/mips-cs/emulator"
    Set-Content -LiteralPath "$out/test.ini" -Value "[General]`nPauseOnLostFocus = False`n[Graphics]`nGraphicsBackend = 2"
    if (Test-Path -LiteralPath "$out/runtime.log") { Remove-Item -LiteralPath "$out/runtime.log" }
    $emuArgs = @("--log=`"$out/emulator.log`"", "--appendconfig=`"$out/test.ini`"", '-direct3d11', '--windowed', "`"$out/EBOOT.PBP`"")
    $process = Start-Process -FilePath "$portable/PPSSPPWindows64.exe" -ArgumentList $emuArgs -WorkingDirectory $out -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(90)
        do {
            Start-Sleep -Milliseconds 250
            $log = ''
            if (Test-Path -LiteralPath "$out/runtime.log") {
                $stream = [IO.File]::Open("$out/runtime.log", [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                $reader = [IO.StreamReader]::new($stream)
                try { $log = $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
        } until ($log -match 'PROLANG OK' -or $process.HasExited -or [DateTime]::UtcNow -ge $deadline)
        if ($log -notmatch 'PROLANG OK') { throw "C baseline failed or timed out: $log" }
        Write-Host "PASS C cube: $out/runtime.log"
    } finally {
        if (!$process.HasExited) { $null = $process.CloseMainWindow(); if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id; $process.WaitForExit() } }
        $process.Dispose()
    }
} finally { Pop-Location }
