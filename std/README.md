# The ProLang standard library

Modules that ship beside the compiler and are imported by name, from anywhere:

```prolang
import "util"
import "ui/shape"
import "ui/theme"
```

An extensionless import is resolved against the `std/` directory next to the compiler executable,
not against the importing file. That is what makes these a library rather than files to copy: a
program in any directory gets the same modules, and there are no `../../` paths to keep correct.
A path with an extension — `import "tools.prl"` — still resolves relative to the importing file,
so an application's own modules and the library's never collide.

| Module | |
|---|---|
| `util` | integer helpers the ~25-function builtin library does not have: `util_abs`, `util_clamp`, `util_floor_div`, `util_pad_left`, `util_parse_int` |
| `char` | ASCII helpers for the `char` type: `char_is_digit`, `char_digit_value`, `char_is_alpha`, `char_to_upper`, `char_is_whitespace`, and friends |
| `intstack` | an explicit stack of ints, for algorithms that cannot recurse |
| `dynarray` | a growable array, generic |
| `testing` | the test harness: a `TestRun` tally threaded through checks, failed by the `assert` builtin |
| `winforms` | the Windows Forms shim, a C# assembly rather than prolang |
| `gl` | OpenGL 3D graphics bindings and native windowing via `GLHelper` |
| `opengl` | alias for `gl` |

## `ui/` — a toolkit for drawing interfaces

Everything under `ui/` except `ui/chrome` is pure prolang: it computes colours, sizes and pixels
and has no idea a window exists. That is why it is testable at all, and it is what makes
`tests/std/run_tests.prl` runnable under `dotnet test` on a machine with no Windows Desktop
runtime pack.

| Module | |
|---|---|
| `ui/color` | ARGB packing, unpacking, luma, hex formatting |
| `ui/document` | a pixel buffer: flat row-major ARGB plus its size, with scaling and alpha-weighted downsampling |
| `ui/raster` | pixel-exact primitives — Bresenham lines, rectangles, midpoint ellipses — that record every pixel they touch so a preview can be rewound |
| `ui/shape` | smooth primitives — discs, rings, arcs, round-capped strokes, convex polygons, rounded rectangles — by distance test rather than scan conversion, so overlapping shapes compose exactly |
| `ui/fill` | scanline flood fill with an explicit stack |
| `ui/view` | zoom and pan arithmetic for a canvas, in integers |
| `ui/history` | undo and redo, as a ring of whole-image snapshots in one flat buffer |
| `ui/palette` | the DawnBringer-16 palette and its swatch strip, with hit testing |
| `ui/theme` | four themes, as a colour per *role* rather than a struct of colours |
| `ui/icons` | 21 icons described as geometry on a 48-unit square, rasterised at any size |
| `ui/layout` | every size in an interface as a function of the display's scaling factor and the room available |
| `ui/chrome` | **talks to Windows Forms.** Blitting, canvas views, icon bitmaps, flat buttons, toolbars, program icons |

### The widget toolkit

A second, higher-level way to build an interface: describe a tree of widgets and let it lay itself
out, rather than placing controls at pixel positions. It is additive — `ui/chrome` and the pixel
editor built on it are unchanged — and it is framework-independent, so the same widget code runs on
Windows Forms and on the PSP.

| Module | |
|---|---|
| `ui/widget` | the node arena and the open/close builder: `Ui->col`, `Ui->row`, `Ui->box`, `Ui->text`, `Ui->button`, `Ui->spacer`, `Ui->image`, `Ui->scene`, `Ui->end` |
| `ui/box` | layout — measure bottom-up, place top-down, divide leftover space between `flex:` children |
| `ui/hit` | hover, press and click, tracked across frames by a widget's id |
| `ui/draw` | flattens a laid-out tree into a display list: a flat `array<int>` of drawing commands |
| `ui/font` | character-width tables, so the layout pass can measure text without asking a window |
| `ui/fps` | a frame-rate counter, averaged over a window, with the worst frame of each window |
| `ui/host_winforms` | **imports `winforms`.** Opens a window and submits each frame in one interop call |
| `ui/host_gl` | **imports `gl`.** Hardware-accelerated OpenGL host for desktop UI widgets |
| `ui/host_psp` | **imports `psp`.** Executes the same display list through the GU, with a D-pad cursor |

```prolang
import "ui/host_winforms"

let host: Host = hostw_open("Counter", 480, 300)
let fonts: FontSet = hostw_font("Segoe UI", 10)
let ui: Ui = Ui->new(256)

while (hostw_running(host)) {
    hostw_begin(host, ui)

    ui->col(pad: 16, gap: 8)
        ui->text("Count: " + count[0])
        ui->button("Increment", action: 1, bg: 4473924, fg: 0 - 1, pad: 8)
    ui->end()

    if (hostw_present(host, ui, fonts) == 1) { count[0] = count[0] + 1 }
}
```

`examples/17-widgets/` is the worked example, and
[`docs/architecture/ui-toolkit.md`](../docs/architecture/ui-toolkit.md) explains the design — why
the tree is an arena of ints, why the builder opens and closes rather than nesting, and what a
backend has to implement.

### The line at `ui/chrome` and `ui/host_winforms`

Importing either pulls in `winforms`, which makes a program need the Windows Desktop runtime pack.
A test suite must not import them, or the suite stops being runnable under `dotnet test` — the
whole import graph compiles into one assembly, so a single reference is enough.

That is why the toolkit is split where it is: `ui/widget`, `ui/box`, `ui/hit`, `ui/draw` and
`ui/font` are pure prolang and carry the whole of `tests/std/run_tests.prl`'s coverage of the
toolkit, while the two host modules have none — each of their functions hands an already-computed
result to a backend and does no arithmetic of its own.

That constraint is why the split is drawn where it is rather than somewhere more convenient, and
why `ui/chrome` is the one module with no tests. Each of its functions hands an already-computed
result to the shim and does no arithmetic of its own, so there is nothing in it a test would catch
that a glance would not.

### Program icons

A Windows icon is not one image but several, so the system can pick the right one for each place it
appears — 16 pixels in a title bar, 32 in Alt-Tab, 256 on the desktop. Handing it one image and
letting it rescale is exactly what produces the blurry application icons that give away a program
nobody finished.

That is a problem for image files and not for this library: everything under `ui/` is described in
units, so drawing the same artwork at six resolutions costs six calls.

```prolang
let sizes: array<int> = chrome_icon_sizes()
let bitmaps: array<int> = array_new(sizes.length())

let i: int = 0
while (i < sizes.length()) {
    let oversized: Document = Document->new(sizes[i] * 4, sizes[i] * 4, 0)
    my_icon_draw(oversized)                                    // your artwork, any ui/shape calls
    bitmaps[i] = chrome_bitmap_downsampled(oversized, sizes[i], 4)
    i = i + 1
}

chrome_save_icon("bin/app.ico", bitmaps)                       // for prolang --icon=...
chrome_window_icon(form, bitmaps)                              // for the running window
chrome_free_bitmaps(bitmaps)
```

The two are different things and both are worth setting. `chrome_save_icon` writes the file that
`prolang --icon=` embeds into the executable, which is what Explorer and the Start Menu read;
`chrome_window_icon` sets what the title bar, taskbar button and Alt-Tab show while the program
runs. The second can follow a theme change, which the first — fixed at build time — cannot.

`chrome_window_icon_sizes()` is the shorter list for the second case: a window never shows an icon
above 48 pixels, and drawing a 256-pixel one at start-up costs more than all the others together.

### Two rasterisers, on purpose

`ui/raster` and `ui/shape` overlap and neither replaces the other.

`ui/raster` draws into an image a user is editing. It is one-pixel-exact and reproducible — the
same endpoints always paint the same pixels, including when a line is dragged back and forth — and
it records the flat index of every pixel it writes, so a shape tool can undo its preview by
restoring exactly those.

`ui/shape` draws interfaces. It has none of that and wants the opposite: smooth edges at whatever
size the display happens to be. It writes only solid pixels; smoothness comes from drawing several
times oversized and averaging down through `Document->downsample_into`, which is why `ui/icons` looks
right at 100%, 150% and 200% from one description.

## Writing an application against it

```prolang
import "io"
import "ui/shape"
import "ui/document"
import "ui/color"

func main()
{
    let canvas: Document = Document->new(32, 32, 0)
    shape_disc(canvas, 16, 16, 10, color_rgb(255, 0, 0))

    print("painted " + (canvas->pixel_count() - canvas->count_colour(0)) + " pixels")
}
```

```powershell
prolang app.prl --target=console --apphost -o bin/app.dll
./bin/app.exe
```

`examples/16-pixel-editor/` is the worked example. After the library was extracted it keeps four
files — `tools.prl`, `render.prl`, `app.prl`, `main.prl` — and everything else it needs comes from
here.

## The one flat namespace

ProLang has a single global namespace across every imported file, so every function and struct in a
program must have a unique name. The per-module prefix is not a style choice: `doc_`, `raster_`,
`shape_`, `theme_`, `icon_`, `layout_`, `chrome_` are what keep the library from colliding with the
application using it.

It also matters more than it looks. An unqualified call that matches nothing falls back to
searching the loaded .NET assemblies, so a name clash or a typo can bind to an arbitrary BCL method
instead of reporting that the function does not exist.

## Adding a module

1. Put it under `std/` (or `std/ui/`) with a prefix nothing else uses.
2. Import library modules by name, applications' modules never.
3. Add tests to `tests/std/`, import them from `tests/std/run_tests.prl`, and classify both files
   in `src/ProLang.Tests/Infrastructure/TestCorpus.cs` — the corpus integrity test fails the whole
   suite otherwise. Files under `std/` itself are not scanned.
4. If it touches Windows Forms it belongs in `ui/chrome` or `ui/host_winforms`, or the test suite
   stops being runnable.
