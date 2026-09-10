# ProLang Text Editor

A text editor written in ProLang, running on the ui toolkit's OpenGL host — with syntax
highlighting for prolang itself.

```
.\build.ps1 -Run          # build, then start the editor
.\build.ps1 -Run main.prl # build, then open a file in it
.\build.ps1 -Test         # build, then run the test suite
.\build.ps1 -Install      # build, then install it with a Start Menu entry
```

## What it does

Editing: insert and delete, split and join lines, auto-indent on return (the current line's
leading whitespace carries over), Tab for four spaces, undo and redo (Ctrl+Z / Ctrl+Y), and save
with Ctrl+S. The cursor moves with the arrows, Home and End, Page Up and Page Down, and a mouse
click; Ctrl+Home and Ctrl+End jump to the ends of the document. Both scrolls follow the cursor.

Selection and the clipboard: drag or Shift+arrows to select, Ctrl+A selects everything, and
Ctrl+C / Ctrl+X / Ctrl+V copy, cut and paste through the Windows clipboard — a paste of many
lines becomes real lines, and undo takes a whole paste or cut back in one step. Typing over a
selection replaces it.

Files: the toolbar opens and saves — Open brings in an existing file, Save As picks where a
buffer lands, and both go through the common dialogs, which the shim drives from the single-
threaded apartment the compiler marks for a windowed program.

The toolbar carries the common tasks with icons: new, open, save, save as, undo, redo, copy, cut,
paste — and, at the right, theme and quit. Most glyphs come from the ui toolkit's icon set, drawn
in unit space from the theme's own colours; the four the set does not have (save as, copy, cut,
paste) are drawn here in the same style, so the row still reads as one set. Each is rasterised
oversized and averaged down, then uploaded as a texture the painter puts in its button.

Syntax highlighting runs while you type: the tokenizer in `syntax.prl` classifies keywords, type
names, calls, strings (including the language's doubled-quote escape), numbers in all four bases
with their float suffixes, line and block comments, operators and punctuation, and `syn_color`
maps each kind into a palette that follows the theme — four themes, dark and light variants of
each, cycled with the Theme button.

## How it is built

Four modules, three of them pure and under test:

| File | What it is |
|---|---|
| `keys.prl` | virtual-key code + shift → the character a US layout would type |
| `buffer.prl` | the document: lines, cursor, every edit, and the undo history |
| `syntax.prl` | the single-line tokenizer and the colour table |
| `editor.prl` | the window: input, layout, and the scene painter that draws everything |

`main.prl` is the entry point; `tests/` holds the suite (176 checks), run by `.\build.ps1 -Test`
and by `dotnet test` through the corpus.

Two shapes the language forced, worth knowing before changing anything:

- **The state lives in globals.** A painter is a function value and function values capture
  nothing, so `ed_paint` receives only the rectangle it paints into and reaches the buffer, the
  scroll and the theme through the `ed_*` globals. The rectangle lands back in `ed_view`, which
  is how a click becomes a cursor position a frame later.
- **The keyboard is polled, not evented.** The OpenGL host reports held keys, so `ed_input`
  walks the virtual-key space each frame, diffs against last frame in `ed_prevKeys`, and turns
  down-now-but-not-then into a press — with its own delay-then-rate auto-repeat.

The document is drawn by appending display-list records: one TEXT record per token, each in its
kind's colour, positioned on the monospace cell grid; the gutter, the current line, the caret and
the clip that keeps long lines inside the text area are records like any other widget's. Nothing
in the painter touches OpenGL — the same records would render on the WinForms host unchanged.

## Not here (yet)

Search, and a confirmation when quitting with unsaved changes. The toolkit has no text model to
borrow and this editor is the text model — those are the next things it would have to grow.
