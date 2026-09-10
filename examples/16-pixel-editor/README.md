# Pixel editor

A pixel art editor written in ProLang, on Windows Forms. Four modules of its own, built on the
`ui/` toolkit in [the standard library](../../std/README.md), with a 450-check test suite also
written in ProLang.

```powershell
.\build.ps1 -Run      # build and start it
.\build.ps1 -Test     # build and run the test suite
.\build.ps1 -Install  # install it, with a Start Menu entry
```

The build produces `bin/pixel-editor.exe` — a real executable, not a `dotnet` invocation.

Tools: pencil, eraser, flood fill, line, rectangle (outline and filled), ellipse (outline and
filled), colour picker. Sixteen-colour palette, integer zoom with a pixel grid, undo and redo,
and PNG open and save.

Right-click erases. Single letters select tools (`p` `e` `f` `l` `r` `o` `i`), `ctrl+z` and
`ctrl+y` undo and redo, `+` and `-` zoom, `t` changes theme, and the wheel zooms about the cursor.

## The icons are drawn in ProLang

Every toolbar icon is described in `icons.prl` as geometry on a 48-unit square — strokes of one
weight with round caps, discs, arcs and rounded rectangles — and rasterised at whatever pixel size
the display calls for. There is no authored resolution and there are no image files.

That is worth doing rather than shipping PNGs. A set of PNGs needs one file per size per theme,
is still wrong at the next scaling factor Windows offers, and has to be kept in step with a colour
scheme it cannot see. These follow the theme because they are drawn from its colours, and they
follow the display because they have no fixed size.

**Smooth edges without an anti-aliasing rasteriser.** `shape.prl` only ever writes solid pixels.
Each icon is drawn four times oversized and averaged down by `Document->downsample_into`, so every
intermediate shade in the finished glyph comes from coverage rather than from any drawing code
knowing about it. Supersampling is a dozen lines and applies to every shape at once, including
where two of them overlap — a rasteriser computing coverage per shape would have to decide what
happens where an outline crosses its own fill, and would get it visibly wrong along every seam.

Colours are averaged **weighted by alpha**. The pixels outside a glyph are transparent *black*, and
letting their zeroed channels into the mean draws a dark halo around every edge.

`test_icons.prl` covers what can go mechanically wrong with a drawn icon: one that draws nothing,
one that fills its box, one that loses its margin, one that uses a colour it was not handed and so
ignores the theme, and any two that come out identical. It also checks the property that makes the
set resolution independent — the same glyph at twice the size covers the same *share* of its box —
which is what catches a coordinate accidentally written in pixels, since that looks perfectly
correct at whatever size the author happened to try.

## The program icon is drawn too

`appicon.prl` is a stepped diagonal — three blocks climbing left to right with two filling in
behind them. It is a picture of the thing the program is for, the staircase an angled line turns
into when it is made of pixels, and it survives being shrunk to sixteen, which is the only test of
an application icon that matters.

It deliberately does not reuse a toolbar glyph. A program whose icon is one of its own buttons
reads as a button, and at 16 pixels in a taskbar there is no context to say otherwise.

It ends up in two places, which are genuinely different:

- **In `pixel-editor.exe`**, embedded as a Win32 resource by `prolang --icon=`. This is what
  Explorer, the taskbar and the Start Menu shortcut read. `makeicon.prl` writes the `.ico` at six
  resolutions and `build.ps1` runs it before compiling the editor — the icon has to exist before
  the executable that carries it.
- **On the window**, set at runtime through `chrome_window_icon`. This one is rebuilt on every
  theme change, which the embedded one cannot be.

Nothing is committed: `bin/pixel-editor.ico` is build output, drawn from source like everything
else here.

Unlike a toolbar glyph the icon fills its box edge to edge, and it has to. A margin given in units
is a fraction of a pixel at sixteen and truncates away entirely, so the icon would have one at the
large sizes and none at the small ones — `test_appicon.prl` checks that every size covers the same
share of its box, which is what caught that.

## It sizes itself to the display

Every measurement in the interface is a function of the display's scaling factor and the room the
screen has, in `layout.prl`. Sizes are written once at 100% and scaled through `layout_px`.

The editor used to hard-code its pixels. On a 96-dpi display that is right; on the 150% and 200%
displays laptops now ship with it is wrong in the worst way, because everything still *works* — it
is just half the size it should be, with 36-pixel buttons measuring 18 real points. Windows papers
over that by stretching the finished window, which is exactly what makes an application look dated.

So the process is DPI aware and does its own scaling. Windows Forms is told **not** to scale
anything (`AutoScaleMode.None`), for a reason particular to this program: the canvas maps one image
pixel to an exact whole number of screen pixels, and a framework free to apply a 1.5x factor
somewhere in that chain puts a fractional offset in it. The visible result is a brush that paints
the pixel next to the one under the cursor.

Awareness is *system*, not per-monitor. A per-monitor process is told its factor has changed when
the window is dragged to another display and is expected to lay itself out again — a message this
program has nowhere to deliver, since the ProLang side reads its metrics once while building the
window. System awareness fixes the factor for the life of the process, which is a promise this
design can keep.

The window is also sized from the screen rather than to a constant, so it does not open taller than
the display it is on, and does not span all of a very wide one either.

**Resizing.** The layout is re-run from the new client size on every resize, so maximising fills
the window instead of leaving a band of background down one side. Windows Forms can do this itself
through docking and anchoring, but those are properties and interop cannot assign to a property —
so it is arithmetic in `app_place_controls`, which is no loss: it is the same arithmetic
`layout.prl` already does, and one expression of where things go beats two that have to agree.

The image is re-centred in the canvas afterwards, keeping the zoom. Clamping the pan alone leaves
it against whichever edge it was near, so making the window bigger pushes the artwork further from
the middle than it was before.

Three things set the window's minimum, in order: a canvas still comfortable to draw in; the
palette, which unlike everything else below the canvas has a width of its own — sixteen swatches
are sixteen swatches, and sizing to the canvas alone let the strip run off the right edge; and
finally the screen, which has the last word, because a minimum larger than the display is one the
user cannot satisfy and Windows honours it anyway.

`test_layout.prl` checks the *relationships* at every factor Windows offers — the bar is wide
enough for its own buttons, the window is exactly its parts, a hairline never rounds away to
nothing, an icon stays smaller than its button and large enough to identify — rather than checking
pixel counts at the one factor the development machine happens to have.

## Themes

Four — Midnight, Paper, Ember, Contrast — cycled with the theme button or `t`. A theme is an int,
and each colour role is a function of it (`theme_surface`, `theme_accent`, `theme_grid`, …) rather
than a struct of colours: ProLang has no way to hold a nested struct safely, so a lookup chain per
role is what the language actually supports.

The roles are named for purpose rather than appearance, so a light theme is a matter of returning
different values instead of every call site asking which theme is active.

`test_theme.prl` mostly checks *properties across all four* rather than particular colours, since
the numbers are meant to be retuned by eye: text stays legible against its surface, the accent
stays distinct from what it sits on, buttons give hover feedback, and the grid stays visible.
That last one caught a real bug — Contrast's grid was near-opaque white, which is invisible on the
white canvas every new image starts as.

## Layout

Most of what this editor was built from is no longer here. Everything general moved into the
standard library at `std/`, and the editor is what is left once you take it away:

| Module | |
|---|---|
| `tools.prl` | the press / drag / release state machine, and which icon each tool shows |
| `appicon.prl` | the program's own icon, drawn on the same 48-unit grid as the toolbar glyphs |
| `render.prl` | the two things that know what this program is: which document to draw, and the status line |
| `app.prl` | window construction, the layout pass, and the event loop |
| `main.prl` | `func main()` — the only file the compiler needs to be given, since `import` is transitive |
| `makeicon.prl` | a build step, not part of the editor: writes `bin/pixel-editor.ico` before the executable that carries it is built |

Everything else comes from [`std/ui/`](../../std/README.md): `ui/document`, `ui/raster`,
`ui/shape`, `ui/fill`, `ui/view`, `ui/history`, `ui/palette`, `ui/theme`, `ui/icons`, `ui/layout`
and `ui/chrome`, plus `util` and `intstack`.

The extraction was worth doing for a reason beyond tidiness: it found real coupling. `icons.prl`
imported the editor's `Tool` enum so it could offer `icon_for_tool`, which meant a general icon set
depended on one application's idea of what a tool is. That mapping is now `tool_icon` in
`tools.prl`, where it belongs — the library draws a pencil without knowing this program has a
pencil tool.

**The boundary is `ui/chrome`.** It is the only module that imports `winforms`, and nothing a test
suite can reach may import it: the whole import graph compiles into one assembly, so a single edge
to the shim would make the suite need the Windows Desktop runtime pack and stop it being runnable
under `dotnet test`.

## What the language forced

Four properties of ProLang shaped this more than any design preference did.

**No delegates, lambdas or function values.** Windows Forms raises events by calling a delegate,
and ProLang has none, so it cannot subscribe to anything. The C# shim owns every handler; they
push a flattened record onto a queue and `app_run` pulls from it. What would be a callback is an
`elif` arm.

**Structs are .NET value types.** A struct parameter is a copy, so writing to `stroke.active`
inside a function changes nothing for the caller — every mutator returns a new struct instead.

The exception is what makes the whole thing work: an `array<T>` field *is* a reference to a real
CLR array, so `Document->set` writing through a `Document` parameter is visible to the caller. All the
mutable state lives in those buffers.

**No `array<array<int>>`.** It does not parse — `>>` lexes as a single shift token. The undo
history is therefore one flat buffer of `slots x width x height` ints used as a ring, which is a
better representation here anyway: it never reallocates.

**No string-to-number conversion.** `int("64")` is a compile error, because the conversion
classifier has no rule from `string` to `int`. `util_parse_int` exists because of that, and the
new-image dialog goes through `WinFormsHelper.InputInt`.

## Three compiler bugs this shook out

Writing this found three defects in the compiler, all of which produced no diagnostic. They are
fixed, with regression tests in `tests/language/structs/` and `tests/language/compiler/`.

- **`array<SomeStruct>` emitted invalid IL.** Element access fell through to `stelem.ref` /
  `ldelem.ref`, which store and load an *object reference* and are not valid for a value type. The
  evaluation stack stays balanced either way, so `AssemblyValidityTests` could not see it; the
  program compiled and then killed the runtime with `Internal CLR error (0x80131506)`.
- **`arr[i].f = x` and `outer.inner.leaf = x` silently did nothing.** Writing to a struct field
  needs the *address* of the struct, and the emitter only produced one for a bare local or
  parameter. Everything else was pushed by value and stored into a copy that was then discarded.
- **A misspelled call bound to an arbitrary BCL method.** Unqualified calls fall back to searching
  the loaded assemblies, and that search matched case-insensitively and ignored parameter count.
  `equals(1)` reported that `Equals` requires 2 arguments instead of that `equals` does not exist.

The last one is why the `doc_` / `raster_` / `hist_` prefixes still earn their keep: the fallback
now demands an exact name, a matching arity, and an unambiguous match, but the language still has
one flat global namespace across every imported file, so every name in the program must be unique.

The editor's own design is unchanged by the fixes. It still uses parallel `array<int>` buffers and
scalar-only structs, which remain the right shape here — the flat history ring never reallocates,
and the pixel buffer is what crosses to C# as an `int[]` for the bulk blit.

## Tests

Two suites, split the same way the code is. `tests/std/run_tests.prl` at the repository root covers
the library — 395 checks over shapes, documents, themes, icons, layout and the rest — and
`tests/run_tests.prl` here covers what the editor still owns, which is the drag state machine and
the tool-to-icon mapping.

Both are entry points classified `Runnable` in
`src/ProLang.Tests/Infrastructure/TestCorpus.cs`, so `dotnet test` compiles each, runs it, and
compares its output against a golden file.

Failures are real failures: `t_report` calls the `assert` builtin, which throws, and the execution
tests require a zero exit code and empty stderr. Before `assert` existed a ProLang test suite
could only print the word "failed" and still exit 0.

The C# shim has its own suite at `src/WinFormsHelper.Tests/` covering the event queue's coalescing
and overflow rules, the pixel-blit and PNG round-trips, and the display metrics. Those last cannot
assert a particular scaling factor — it is whatever the machine running the tests is set to, and a
test expecting 100% would fail on any scaled laptop — so they assert that the numbers are usable
and that two calls agree, which is what the layout depends on because it reads them once.

## Notes

There is no `.prlproj` here. The MSBuild integration under `examples/` replaces `CoreCompile` with
a fixed compiler invocation that cannot pass `--target=winexe` or `--apphost`, so it would produce
a console-subsystem assembly requesting the wrong framework and no launcher. `build.ps1` is the
supported path.
