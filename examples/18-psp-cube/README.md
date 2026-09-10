# A rotating cube, as a scene component

```powershell
.\build.ps1 -Run     # the desktop version
.\build.ps1 -Test    # the cube's own test suite
.\build.ps1 -Psp     # transpile the PSP version to C99
```

The point of this example is `Ui->scene` — the toolkit's escape hatch for content it knows nothing
about. A 3D scene, a chart, a game viewport: anything that is not a widget, plugged in as though it
were one.

## What makes it pluggable

`cube.prl` imports no host and draws nothing. It is a **scene painter**: given the rectangle the
layout assigned, it appends display-list records — twelve `LINE`s for the twelve edges.

```prolang
func cube_paint(ops: array<int>, at: int, tag: int,
                x: int, y: int, w: int, h: int, state: array<int>) : int
```

Because it emits rather than draws, the cube needs no support from any backend: those records are
the ones every backend already executes. The same file drives `psp_cube.prl` on the hardware and
`cube_desktop.prl` on Windows Forms with nothing changed, and it composites in tree order — the
buttons below it are built after it, so they draw over it.

Drawing directly would have meant one painter per platform, and on Windows Forms it could not have
worked at all: the display list is executed later, on the UI thread, inside a paint message.

In the tree it is an ordinary widget:

```prolang
ui->col(pad: 12, gap: 10)
    ui->text("X 30   Y 40   Z 0")

    ui->scene(tag: 1, flex: 1, pad: 8, radius: 8, bg: theme_surface(theme))

    ui->row(gap: 6)
        cube_btn(ui, "X-", CubeAct.ROT_XM, theme)
        ...
    ui->end()
ui->end()
```

`flex: 1` gives it the leftover space, the card's rounded background is drawn for it, and it follows
the window when it is resized because it sizes the cube from the rectangle rather than a constant.
Pass the painter to `hostw_present_scene` / `hostp_present_scene` and that is the whole wiring.

`state` is an `array<int>` because a ProLang function value **captures nothing** — which is exactly
what lets one compile to a bare function pointer on the PSP with no garbage collector. The angles,
the sine table and the projection scratch all live there. It is a `global` of the program rather
than a local of `main`: the host invokes the painter from inside its own frame, and a global is the
one thing both sides can see.

## Three bugs worth recording

The cube did not appear at first, and the reasons are all the kind that produce no error.

**Everything truncated to zero.** The cube was built from vertices at plus or minus **1** rather
than plus or minus one *fixed-point unit* (4096). Every product went through `(1 * cosine) / 4096`,
and since a cosine is at most 4096, integer division sent all of them to zero. All eight corners
projected to the centre, the twelve edges became twelve zero-length lines, and nothing was drawn.
The arithmetic was not wrong — the units were.

**The sine table was read from the wrong offset.** It is a slice of the state array starting at
`cube_sin_base()`, and `cube_sin` indexed from zero, so it returned the angles and the colour as
though they were sine values. The result was wrong by a plausible-looking amount, and every
structural test — eight distinct corners, twelve records, nothing zero-length — still passed. The
exact quadrant checks (`sin 0 == 0`, `sin(quarter turn) == one`) are what caught it.

**It drew outside its own rectangle.** The cube was sized so its front face fitted, which is right
for an unrotated cube and wrong for every other one: a corner sits `sqrt(3)` units from the centre
rather than one, so turning it swings that diagonal into view *and* brings it nearer the eye. At
forty-five degrees it reached about 1.17 times past its box, over the widgets beside it. The test
now sweeps all three axes rather than checking a few chosen angles, because the worst case is at
none of the angles anybody would think to write down.

A fourth thing was not a bug but still wrong: at an eye distance of four units the geometry was
correct and it still looked like a truncated pyramid, because the near and far corners were 2.5
times apart in depth. Twelve units brings that to 1.34, which reads as a cube.

## The frame-rate counter

The header shows `60 fps   peak 12 ms`, from `ui/fps`. It is averaged over half a second rather than
taken per frame, because at sixty frames a second a frame is 16 or 17 milliseconds depending on
where the boundary fell, and `1000 / lastFrame` swings between 62 and 58 too fast to read. The worst
frame of each window is shown beside the average, since an average is exactly the statistic that
hides a stutter.

It earned its place immediately: it read **40 fps** on the desktop build. The cube was not slow —
`hostw_begin` waits up to 16ms for an event before drawing, which is right for an interface that
only changes when someone touches it and is just a frame-rate cap for one that animates. Passing a
1ms wait while spinning took it to **64 fps**, with the frame time dropping from 32ms to 4ms.

`Fps->sample` takes the time as a parameter and only `Fps->tick` reads the clock, which is what lets
`tests/std/test_fps.prl` drive it with invented timestamps and check the cases that are hard to
produce deliberately — a frame too fast to measure, a window that overshoots, and the clock wrapping
past the end of a 32-bit int.

## Controls

Desktop: the buttons rotate and reset; **Spin** toggles the animation. The window can be resized
and the cube follows.

PSP: the D-pad moves the cursor and Cross clicks; the shoulder triggers spin it directly; Start
quits.

## Tests

`tests/run_tests.prl` covers the whole of `cube.prl` with no window, no PSP and no graphics — it can
because the painter only produces records. Twenty-six checks over the trigonometry, the projection
and the painter, including the angle sweep above.

The PSP program transpiles under `--emit-psp` and is checked no further here: there is no pspdev
toolchain in this repository, so it has not been run on hardware or in an emulator.
