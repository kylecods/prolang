# Pixel editor

A pixel art editor written in ProLang, on Windows Forms. Fourteen modules, ~2,600 lines, with a
331-check test suite also written in ProLang.

```powershell
.\build.ps1 -Run     # build and start it
.\build.ps1 -Test    # build and run the test suite
```

Tools: pencil, eraser, flood fill, line, rectangle (outline and filled), ellipse (outline and
filled), colour picker. Sixteen-colour palette, integer zoom with a pixel grid, undo and redo,
and PNG open and save.

Right-click erases. Single letters select tools (`p` `e` `f` `l` `r` `o` `i`), `ctrl+z` and
`ctrl+y` undo and redo, `+` and `-` zoom, `t` changes theme, and the wheel zooms about the cursor.

## The icons are drawn in ProLang

Every toolbar icon is pixel art, drawn at 16x16 by the same rasteriser that draws in the canvas —
`icons.prl` is Bresenham lines, rectangles and ellipses, the module the editor already had.

That is worth doing rather than shipping PNGs. The icons recolour themselves for each theme,
because they are *drawn* from theme colours rather than tinted. They stay sharp at any integer
zoom, because `doc_scale_into` repeats pixels instead of resampling. There are no binary assets to
keep in step with the source. And an editor whose own buttons are pixel art looks like nothing
else.

`test_icons.prl` covers what can go mechanically wrong with a drawn icon: one that draws nothing,
one that spills outside its grid, one that uses a colour it was not handed and so ignores the
theme, and any two that come out identical.

## Themes

Four — Midnight, Paper, Ember, Contrast — cycled with the theme button or `t`. A theme is an int,
and each colour role is a function of it (`theme_surface`, `theme_accent`, `theme_grid`, …) rather
than a struct of colours: ProLang has no module-level state a table could live in, and no way to
hold a nested struct safely, so a lookup chain per role is what the language actually supports.

The roles are named for purpose rather than appearance, so a light theme is a matter of returning
different values instead of every call site asking which theme is active.

`test_theme.prl` mostly checks *properties across all four* rather than particular colours, since
the numbers are meant to be retuned by eye: text stays legible against its surface, the accent
stays distinct from what it sits on, buttons give hover feedback, and the grid stays visible.
That last one caught a real bug — Contrast's grid was near-opaque white, which is invisible on the
white canvas every new image starts as.

## Layout

Everything above `render.prl` is pure ProLang with no reference to Windows Forms. That is what
makes it testable: `tests/run_tests.prl` compiles the whole import graph into one assembly, so a
single edge to the shim would make the suite require the Windows Desktop runtime pack and stop it
being runnable under `dotnet test`.

| Module | |
|---|---|
| `util.prl` | integer helpers the ~25-function standard library does not have |
| `color.prl` | ARGB packing and unpacking |
| `document.prl` | the image: a flat pixel buffer and its size |
| `intstack.prl` | an explicit stack, for the flood fill |
| `raster.prl` | lines, rectangles, ellipses, brushes |
| `fill.prl` | flood fill |
| `history.prl` | undo and redo, as a ring of whole-image snapshots |
| `palette.prl` | the sixteen colours and the swatch strip |
| `view.prl` | zoom and pan arithmetic |
| `tools.prl` | the press / drag / release state machine |
| `theme.prl` | the four themes, as a colour per role |
| `icons.prl` | every toolbar icon, drawn as 16x16 pixel art |
| `render.prl` | the boundary: hands model state to the shim |
| `app.prl` | window construction and the event loop |

`main.prl` is the only file the compiler needs to be given — `import` is transitive.

## What the language forced

Four properties of ProLang shaped this more than any design preference did.

**No delegates, lambdas or function values.** Windows Forms raises events by calling a delegate,
and ProLang has none, so it cannot subscribe to anything. The C# shim owns every handler; they
push a flattened record onto a queue and `app_run` pulls from it. What would be a callback is an
`elif` arm.

**Structs are .NET value types.** A struct parameter is a copy, so writing to `stroke.active`
inside a function changes nothing for the caller — every mutator returns a new struct instead.

The exception is what makes the whole thing work: an `array<T>` field *is* a reference to a real
CLR array, so `doc_set` writing through a `Document` parameter is visible to the caller. All the
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

`tests/run_tests.prl` is the single entry point and the only file here classified `Runnable` in
`src/ProLang.Tests/Infrastructure/TestCorpus.cs`, so `dotnet test` compiles it, runs it, and
compares its output against a golden file.

Failures are real failures: `t_report` calls the `assert` builtin, which throws, and the execution
tests require a zero exit code and empty stderr. Before `assert` existed a ProLang test suite
could only print the word "failed" and still exit 0.

The C# shim has its own suite at `src/WinFormsHelper.Tests/` covering the event queue's coalescing
and overflow rules and the pixel-blit and PNG round-trips.

## Notes

There is no `.prlproj` here. The MSBuild integration under `examples/` replaces `CoreCompile` with
a fixed compiler invocation that cannot pass `--target=winexe`, so it would produce a
console-subsystem assembly requesting the wrong framework. `build.ps1` is the supported path.
