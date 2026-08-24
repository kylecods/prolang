# ProLang for VS Code

Editor support for [ProLang](../README.md), backed by the ProLang compiler itself.

The extension does not analyse ProLang. It starts `prolang lsp`, and everything it shows — the
errors, the types, the completions, the documentation — comes from the same lexer, parser and
binder that compile the program. There is no second implementation of the language to drift out of
step with the first.

## What it does

| | |
|---|---|
| **Errors** | Real compiler diagnostics, on every keystroke: syntax errors, type errors, undefined names, argument mismatches. |
| **Hover** | A symbol's signature and its documentation. For a library function that is the comment written above it in `std/`; for a builtin it is written into the compiler. |
| **Go to definition** | Follows a name to its declaration, across the whole import graph — including into the standard library. Go to definition on an `import` opens the module. |
| **Find references, rename** | Every use of *that* symbol, not every occurrence of its name. Renaming a library symbol is refused rather than half-done. |
| **Completion** | Contextual. After `.` it offers the fields of the struct, or a string's methods, or an enum's members. Inside `import "` it lists every module with a description of each. In a type position it offers types. Inside a call it offers the parameter names. |
| **Signature help** | The parameter list while typing a call, with the right argument highlighted, defaults shown, and named arguments understood. |
| **Semantic highlighting** | Colouring by what each name resolved to, so a struct, a parameter, an enum member and a builtin each look like what they are. |
| **Outline, folding, inlay hints, formatting** | Structs and enums nest their members; inferred `let` types are shown where the annotation would be; formatting re-indents from the parse tree. |

## Requirements

The `prolang` compiler, which is also the language server. Install it with `install.ps1` from the
repository root, or:

```
dotnet tool install -g ProLang.Compiler
```

The extension finds it by looking, in order, at the `prolang.serverPath` setting, the `PROLANG_PATH`
environment variable, the ProLang repository if that is what is open, and finally `PATH`.

## Settings

| Setting | |
|---|---|
| `prolang.serverPath` | Path to the compiler. Empty searches as described above. |
| `prolang.stdPath` | Where library imports resolve from. Empty uses the `std` directory beside the compiler — except when the ProLang repository is open, where its own `std/` is used so that navigation lands in files you can edit. |
| `prolang.trace.server` | Logs the messages exchanged with the server, for diagnosing the extension itself. |

## Commands

`ProLang: Run File`, `ProLang: Build File`, `ProLang: Restart Language Server`,
`ProLang: Show Language Server Info`.

Restarting is worth knowing about: the server keeps analysis cached in memory, and restarting is
the quickest way to pick up a change made outside the editor.

## Building it

```
npm install
npm run compile
npm run package     # produces a .vsix
```

The server is not part of this build. It is `src/ProLang/Lsp/` in the compiler, and its tests are
in `src/ProLang.Tests/Lsp/`.
