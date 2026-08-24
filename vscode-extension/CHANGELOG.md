# Changelog

## [0.2.0]

The extension no longer analyses ProLang. It starts `prolang lsp` and shows what the compiler
says, so every language feature the binder understands is understood by the editor.

### Added

- Real compiler diagnostics: syntax, type, and name-resolution errors, at the right column.
- Hover showing a symbol's signature and its documentation — for a library function, the comment
  written above it in `std/`; for a builtin, a description written into the compiler.
- Go to definition, find references, document highlight and rename, all resolved by symbol rather
  than by matching text, and all crossing into imported files and the standard library.
- Contextual completion: struct fields, string and array methods and enum members after `.`; every
  importable module, with a description, inside `import "`; types in a type position; parameter
  names inside a call. A builtin whose module is not imported is offered with an edit that adds
  the import.
- Signature help with the correct active parameter, including inside nested calls and for named
  arguments.
- Semantic highlighting, folding ranges, inlay hints for inferred `let` types, a nested document
  outline, workspace symbol search across the workspace and the standard library, and document
  links on import paths.

### Changed

- Formatting is driven by the parse tree. It dedents closing braces, honours the editor's tab
  settings, leaves blank lines blank, and ignores braces inside strings and comments.
- The TextMate grammar drops the `<script>` HTML embedding and `${` interpolation left over from
  an earlier design, and gains `struct`, `enum`, `as`, `null`, the full set of numeric types, and
  the hexadecimal, binary, octal and float-suffix literal forms.
- `for` snippet emits `for (let i = 0 to 10)`. The old one omitted `let` and did not compile.

### Removed

- The bundled TypeScript language server, which matched declarations with four regular
  expressions. It reported every non-ASCII character as an error — flagging every file in the
  standard library — reported nothing else at all, could not resolve any library import, and its
  rename sent a request to the client and then threw on the reply.
- The debug adapter, which simulated execution and never launched a ProLang program.
- `prolang.buildProject`, which showed a message and built nothing. `ProLang: Build File` replaces
  it and actually compiles.

## [0.1.0] - 2026-03-28

Initial release: TextMate grammar, language configuration, snippets, and a regex-based language
server.
