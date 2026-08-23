# The UI toolkit

A framework-independent way to build interfaces in ProLang. A program describes a widget tree; the
toolkit lays it out and flattens it into drawing commands; a backend executes them. Windows Forms
and the PSP are the two backends today, and the same widget code runs on both.

---

## Why it is shaped this way

The previous API was Windows Forms, thinly wrapped. Every control was created at an absolute pixel
position, parented by hand, styled through a static method per property, and repositioned by a
second hand-written pass when the window was resized. `examples/16-pixel-editor/app.prl` spends
~360 lines on one window, and its layout exists twice because interop cannot assign to `Dock` or
`Anchor`.

None of it could reach the PSP, whose whole graphics surface is `psp_fill_rect` and
`psp_draw_text`, and none of it could reach a browser.

Two designs were considered.

**Markup — a JSX for ProLang.** Rejected. The HTML syntax already in `Parser.cs` is dead
scaffolding: `ParseHtmlStartTag` accepts exactly `< identifier >` — no attributes, no self-closing
tags, no text nodes — closing tags are never matched against their openers, and `Binder.cs` drops
`HtmlDeclarationSyntax` without a diagnostic. More to the point, markup is a *syntax over* a widget
model rather than a substitute for one: it would need everything below plus a parser, plus
HTML/CSS semantics that do not describe a 480x272 screen. It remains possible later, over exactly
the same arena.

**Components, as Flutter does it.** Chosen, with the display list as the portability boundary.

---

## The layers

```
        pure ProLang — no platform, testable under `dotnet test` on any OS
   ┌──────────────────────────────────────────────────────────────────────┐
   │  ui/widget   the arena, and the builder that fills it                │
   │  ui/box      layout: measure bottom-up, place top-down               │
   │  ui/hit      hover, press, and what a click means                    │
   │  ui/draw     the tree  ->  a flat array<int> of drawing commands     │
   │  ui/font     character-width tables, so layout needs no callback     │
   └──────────────────────────────────────────────────────────────────────┘
                     │  ops: array<int>   strings: array<string>
                     ▼
   ┌──────────────────────────────────────────────────────────────────────┐
   │  ui/host_winforms   one interop call per frame, executed by GDI+     │
   │  ui/host_psp        a ProLang loop calling psp_fill_rect / draw_text │
   └──────────────────────────────────────────────────────────────────────┘
```

A backend implements no interface and subclasses nothing. It executes a buffer.

---

## The widget tree is an int arena

A node is an `int`, and the whole tree is one `array<int>` addressed as `node * ui_stride() +
field`. Children hang off `FIRST_KID` / `NEXT_SIB`. "No node" is `-1`.

The obvious `struct Node { kids: array<Node> }` is not available, and it is worth recording why,
because all three reasons are properties of the compiler rather than preferences:

- **Recursive structs do not bind.** `Binder.BindStructDeclaration` resolves a field's type before
  it declares the struct, so a struct cannot name itself. (The emitters already support it —
  `TypeEmitter` registers the type before adding fields, deliberately — so this is a binder change
  of about thirty lines if it is ever wanted.)
- **`CEmitter` emits only non-generic structs** (`StructTypes.Where(s => !s.IsGeneric)`), so a
  `Ui<S>` would silently vanish on the C and PSP backends.
- **Casting `any` to a struct emits `isinst`**, which yields a boxed reference for a value type, so
  a heterogeneous `array<any>` of nodes cannot be read back.

The arena is also simply the better representation: one allocation per frame, no pointer chasing,
and it transpiles unchanged to C, to the PSP, and to anything a browser backend would want.

**Reference types were considered and not added.** The `TypeEmitter` pivot is small — struct-ness
comes from one `System.ValueType` base reference, and `IsEmittedAsValueType` derives the rest from
the Cecil flag — but the cost lands on the C and PSP backends, which have no allocation or lifetime
story. The arena gives mutation *and* better locality at no backend risk.

### Mutating through a value type

`Ui` is a struct, so it is copied into every function it is passed to. Each of its four fields is
an `array<T>`, which is a real CLR reference, so writes *through* them reach the caller. That is
why the node count, the string count and the builder's stack depth live in the `st` array rather
than in scalar fields: a scalar would be incremented on a copy and thrown away.

---

## The builder opens and closes

```prolang
ui_col(ui, pad: 16, gap: 12)
    ui_text(ui, "Count: " + n, font: TITLE)
    ui_row(ui, gap: 8)
        ui_button(ui, "Increment", action: ACT_INC)
        ui_button(ui, "Reset",     action: ACT_RESET)
    ui_end(ui)
ui_end(ui)
```

Containers open a scope; `ui_end` closes the innermost. Indentation carries the tree.

The nested-call form Flutter and JSX use would require an array literal of children passed inline
as an argument — a path nothing in this repository exercises. This form needs no array literals, no
nested call expressions and no recursive types, so it works on every backend as the language
stands. A conditional child is an ordinary `if` around a statement rather than something that has
to produce a list.

Its one hazard is an unbalanced `ui_end`, so that is checked: `ui_end` on an empty stack asserts,
and so does presenting a frame with a container still open.

Default and named arguments are what make this readable, and they were added to the compiler for
it. Without them every widget would take a dozen positional integers.

---

## Scenes: content the toolkit knows nothing about

`ui_scene` is the escape hatch — a 3D view, a chart, a game viewport — and it is a first-class
widget rather than something bolted on beside one. It takes `flex`, it sits inside a padded card, it
moves when the window is resized, and it composites in tree order.

What makes it portable is that **the painter does not draw; it appends display-list records.**

```prolang
func my_painter(ops: array<int>, at: int, tag: int,
                x: int, y: int, w: int, h: int, state: array<int>) : int
```

It is handed the rectangle inside the scene's padding and returns the new record count, normally by
chaining `dl_emit` calls. Those records are the ones every backend already executes, so a scene
needs **no backend code at all** — a painter emitting twelve `LINE`s is a wireframe cube on Windows
Forms, on the PSP, and on anything a browser backend would run.

Drawing directly was the obvious alternative and it fails twice over: it would mean one painter per
platform, and on Windows Forms it could not work at all, because the display list is executed later
on the UI thread inside a paint message. There is no moment at which a ProLang callback could run.

`state` is an `array<int>` rather than anything captured, because a ProLang function value captures
nothing — the same restriction that lets one compile to a bare function pointer on the C and PSP
backends. Whatever the painter must reach goes in there.

Wire it up with `dl_build_scenes`, or the `_scene` variant of either host's present:

```prolang
let act: int = hostw_present_scene(host, ui, fonts, my_painter, state)
```

`dl_build` and the plain `present` still work; they pass `dl_no_scene`, so a scene reserves its
space and shows its background. Forgetting a painter therefore looks like an empty panel rather than
a crash.

`examples/18-psp-cube/` is the worked example. Its README records the three silent failures the
cube's own arithmetic produced — none of which raised an error, and one of which every structural
test still passed.

---

## Layout

Two passes, in integers, with no platform anywhere in them.

1. `box_measure` walks bottom-up. A text node answers from the font table; a container sums its
   children plus its own gaps and padding.
2. `box_place` walks top-down, handing each node its rectangle and dividing any leftover space
   between the children that asked for it with `flex:`.

Two passes rather than one because a child's share of the leftover cannot be known until every
sibling has said what it needs.

Integer division loses a pixel when three children split a hundred, so the **last flexible child
takes the remainder** and children always sum to exactly the space available. A layout that loses a
pixel per row is visibly ragged down its right edge.

### Text metrics are data, not a callback

Layout needs a string's width before anything is drawn, and only the backend truly knows it. Rather
than calling out mid-layout, **the host measures every glyph once at start-up** into a table, and
`font_measure` sums advances over it.

That is what keeps the layout engine pure: it runs identically on Windows Forms, on the PSP, and
under `dotnet test` on a machine with no window system. It is also why `charCode` exists as a
builtin — without it, choosing among ninety-five widths would mean ninety-five string comparisons
per character.

The cost is kerning: a proportional font measures a few pixels wide over a long run. The error is
always towards reserving too much room rather than clipping, which is the right direction.

---

## Interaction

A button fires when a press **and** the release both land on it, so a press can be taken back by
sliding off before letting go. That needs memory between frames, and the tree is discarded every
frame — a node index is not stable when a widget above it appears.

What is remembered is the widget's `id`, which defaults to its `action`. Two states persist in
`Ui.st`: `HOT_ID` (under the cursor) and `ACTIVE_ID` (where the press landed).

`hit_topmost` searches the arena **backwards**, because nodes are painted in allocation order, so
the last node containing the point is the one actually visible. Searching forwards would let a
widget report clicks through whatever covers it.

### Events are integers, not callbacks

`ui_present` returns the action that fired; the frame loop handles it against state it owns.

This is not a limitation being worked around. Function values in ProLang capture nothing — that
restriction is what lets them compile to a bare function pointer on the C and PSP backends with no
garbage collector — so a handler could not reach the counter it was supposed to change. Returning
the action to the loop that owns the state is the shape that fits, and it keeps every state change
in one readable place.

---

## The display list

The contract. Fixed-width records of `dl_stride()` = **7** ints, whatever the opcode, so a reader
steps by a constant and a test can address record *n* directly.

| op | name | operands |
|---|---|---|
| 0 | `END` | |
| 1 | `RECT` | x, y, w, h, argb |
| 2 | `RRECT` | x, y, w, h, argb, radius |
| 3 | `TEXT` | x, y, stringIndex, argb, font |
| 4 | `LINE` | x1, y1, x2, y2, argb, width |
| 5 | `IMAGE` | x, y, w, h, imageId |
| 6 | `CLIP_PUSH` | x, y, w, h |
| 7 | `CLIP_POP` | |

Colours are packed ARGB. Text carries an **index into the string table**, not a string, so the
whole frame is one `int[]` plus one `string[]`. A text record's `y` is the top of the line box, not
the baseline; a backend that draws from a baseline adds the font's ascent.

Records are emitted in tree order, so a parent's background precedes its children and later
siblings paint over earlier ones — the same order `hit_topmost` searches in reverse.

A transparent background emits nothing. Most nodes in a real tree are structural, and a
transparent rectangle for each would multiply the list for no pixels.

Overflowing the buffer truncates rather than throwing: an incomplete frame is recoverable and
visible, a program that dies mid-paint is neither. Compare the returned count against
`dl_capacity`.

---

## Writing a backend

Four things, none of which mentions a widget:

1. Open a surface and report its size.
2. Measure the fonts once into a `FontSet`.
3. Report a pointer position and a button state.
4. Execute the display list.

`std/ui/host_winforms.prl` submits the whole buffer to `WinFormsHelper.SurfaceSubmit` in one
interop call — a ProLang `array<int>` is a real CLR `int[]`, so it crosses without marshalling —
and `Surface.cs` executes it through GDI+.

`std/ui/host_psp.prl` loops over the buffer in ProLang, because `psp_fill_rect` is a compiler
builtin that becomes a direct C call and there is no boundary to amortise. It approximates what the
hardware lacks and says so: rounded rectangles are drawn square, lines only when axis-aligned,
images skipped, clipping ignored. A D-pad-driven cursor stands in for the pointer, which reuses
`ui/hit` unchanged rather than inventing focus traversal for one backend.

A JS or WASM backend would be a third sibling: `canvas2d` maps onto these eight opcodes almost
directly, and `CSharpBackend.cs` is the template for the compiler side.

---

## Testing

Everything above the host line is covered by `tests/std/run_tests.prl`, which is `Runnable` in the
corpus, so `dotnet test` compiles it, runs it and compares its output. It needs no Windows Desktop
runtime pack, because nothing it reaches imports `winforms` — that is the line `ui/host_winforms`
sits on, the same one `ui/chrome` sits on.

The tests assert **relationships rather than pixel counts**: a row's children and gaps sum to its
width exactly, a flexible child absorbs the remainder, a press and a release on the same widget
fire once and only once, a release elsewhere fires nothing. That is what survives the sizes being
retuned, and it is what catches the errors that matter.

`WinFormsHelper.SurfaceSaveFrame(surfaceId, path)` renders the current display list into a PNG with
no window involved. It is how a frame can be looked at directly rather than through a screenshot,
which drags the compositor and the display's colour profile into a picture that is meant to be
about the toolkit — and it is what a golden-image test would use.

---

## What is not there yet

- **Scrolling and clipping.** The opcodes exist; no widget emits them.
- **Text input.** There is no caret, selection or IME. On Windows Forms the escape hatch is a real
  `TextBox` positioned by the layout; the toolkit does not do this yet.
- **Multi-line and wrapped text.** `ui_text` is one line.
- **Focus and keyboard navigation.** `ui/hit` is pointer-only.
- **Images on the PSP.** There is no path from a bitmap handle to a GU texture.
