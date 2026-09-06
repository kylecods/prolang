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
click. Both scrolls follow the cursor.

It opens the file named on the command line, or a small sample of the language when started
without one. `.\build.ps1 -Run examples/21-text-editor/syntax.prl` edits this editor's own
highlighter.

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

Selection, clipboard, and search. The toolkit has no text model to borrow and this editor is the
text model — those three are the next things it would have to grow.
