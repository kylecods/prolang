# The widget toolkit

The same counter, twice: once on Windows Forms, once on a PlayStation Portable.

```powershell
.\build.ps1 -Run                                    # the desktop version
prolang counter_psp.prl --emit-psp                  # then `make -f Makefile.psp` in WSL
```

## What this example is for

Put `counter.prl` beside `counter_psp.prl`. The part that says what the interface *is* — the
column, the card, the row of buttons, the spacer that pushes the last group to the right — is
character for character the same. What differs is four lines of setup and the names of two host
functions.

The two machines could hardly be less alike. One has windows, a pointer, GDI+ and a .NET runtime.
The other is 480x272 with no windows, no pointer, an 8x8 bitmap font, and no runtime at all — that
path transpiles to C99 and cross-compiles to a native MIPS binary.

Neither the widgets nor the layout nor the theming knows which it is on, because none of them ever
reaches a backend. They produce a display list, and the host executes it.
[`docs/architecture/ui-toolkit.md`](../../docs/architecture/ui-toolkit.md) is the full account.

## Compare it with the old API

[`examples/13-winforms/04_counter.prl`](../13-winforms/04_counter.prl) is the same program written
against the previous interface. Every control there is created at an absolute pixel position,
parented by hand, styled by hand, and would need a second hand-written pass to survive a resize.

Here nothing has a coordinate:

```prolang
ui_col(ui, pad: 20, gap: 16, bg: theme_background(theme))
    ui_text(ui, "Counter", font: Font.TITLE, fg: theme_text(theme))

    ui_box(ui, pad: 20, radius: 8, flex: 1, bg: theme_surface(theme),
           align: UiAlign.CENTER, justify: UiAlign.CENTER)
        ui_text(ui, "" + count[0], font: Font.TITLE, fg: theme_accent(theme))
    ui_end(ui)

    ui_row(ui, gap: 8)
        counter_button(ui, "Decrement", Act.DECREMENT, theme)
        counter_button(ui, "Increment", Act.INCREMENT, theme)
        ui_spacer(ui, flex: 1)
        counter_button(ui, "Quit", Act.QUIT, theme)
    ui_end(ui)
ui_end(ui)
```

Resizing works because the tree is laid out again against the new size. There is no second copy of
the arithmetic to keep in step with the first.

## Three things worth noticing

**A component is a function.** `counter_button` takes the arena and builds a button. There is no
base class to derive from and no interface to implement; composing components is calling them.

**Buttons size themselves.** No width is given: a button measures its label through the font table
and adds its padding. A number here would be one more thing to keep in step with the font.

**A flexible spacer is how something is pushed to the far end.** `ui_spacer(ui, flex: 1)` takes
whatever the fixed children leave, so everything after it ends up hard against the right edge.

## Events are integers

`hostw_present` returns the action of whatever was clicked, and the loop that owns the state
handles it:

```prolang
if (act == Act.INCREMENT) {
    count[0] = count[0] + 1
} elif (act == Act.THEME) {
    activeTheme[0] = theme_next(theme)
}
```

Not a callback, and not for want of trying. Function values in ProLang capture nothing — that is
what lets them compile to a bare function pointer on the PSP with no garbage collector — so a
handler could not reach `count` anyway. Returning the action to the loop keeps every state change
in one readable place instead of scattered across handlers.

`count` is an `array<int>` of one rather than a plain `int` for a related reason: a struct field
would be copied, and an array is the language's mutation channel.

## Controls

Desktop: click the buttons; the window can be resized and the layout follows.

PSP: the D-pad moves a cursor, Cross clicks, Start quits. A cursor rather than focus traversal, so
that `ui/hit` is reused exactly as the desktop uses it.
