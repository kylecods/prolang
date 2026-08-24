# The language server

`prolang lsp` runs a Language Server Protocol server over standard input and output. It lives in
`src/ProLang/Lsp/`, inside the compiler, and answers every question by asking the compiler.

## Why it is part of the compiler

The alternative — and what existed before — was a separate implementation in the editor extension
that found declarations with regular expressions. It could not type-check, could not resolve a
scope, could not follow an import, and reported diagnostics that were simply invented. Any
independent implementation has that trajectory: it starts as an approximation of the language and
diverges from it with every compiler change.

Being a subcommand rather than a separate program is what makes that guarantee complete. The
`prolang` on your PATH is both the thing that compiles the program and the thing that describes it,
so the two cannot be different versions of the language. It also means the standard library the
editor navigates is the one the compiler will actually resolve, because it is found the same way.

## What the compiler had to grow

Three things were missing, and one was broken.

**Token positions were wrong.** The lexer built each token with the offset it had reached rather
than the offset the token started at, so every span in the compiler — and every diagnostic column —
sat one token-length to the right of the text it described. Nothing had caught it: no test asserted
a diagnostic position, and the code that renders the offending source line wraps the range
arithmetic in a bare `catch`, so a wrong span printed as no line at all.

**Nothing mapped a position to a symbol.** `BoundNode` carries a kind and nothing else — no span,
no back-reference to the syntax it came from. The decisive part is that annotating the bound tree
would not have been enough anyway: binding a type clause produces a bare `TypeSymbol` and never a
bound node, so *no* amount of work on the bound tree would let anyone navigate from the `Point` in
`let p: Point` to the struct.

So the binder records what it resolves, as it resolves it. `BindingRecorder` collects two things:

- **Symbol occurrences** — `(location, symbol, definition-or-reference)` — written at the dozen or
  so points where the binder turns a name into a symbol. Hover, go to definition, find references,
  rename, document highlight and semantic colouring are all one query over this list.
- **Expression types** — `(span, type)` — written at the single choke point every expression passes
  through, `BindExpression`. This is what lets member completion answer `getBox(i).` and `arr[0].`,
  whose receivers have a type but no symbol of their own.

The recorder is null unless a caller asks for it, so an ordinary compile pays one null check per
bind site. `ProLangCompilation.CreateForAnalysis` is the entry point, and `GetSemanticModel()` is
the view over the result.

**Documentation had nowhere to live.** `Symbol.Documentation` is now the single place, filled two
ways: a builtin carries it from its declaration in `BuiltInFunctions`, because a builtin has no
source file; everything written in ProLang gets it from the comment above the declaration, via
`DocumentationExtractor`. That last part is why the standard library needed no new documentation to
become documented — it was already commented, and nothing had been reading the comments.

## The parts, and where they are

```
src/ProLang/Lsp/
  LanguageServerCommand.cs   the `prolang lsp` verb
  LanguageServer.cs          lifecycle, dispatch, publishing diagnostics
  Protocol/                  JSON-RPC framing, and the protocol types actually exchanged
  Workspace/                 open buffers, incremental sync, the compilation cache
  Handlers/                  one file per capability, each a function of an Analysis
```

The protocol layer is hand-written against `System.Text.Json`. The base protocol is a
`Content-Length` header and a JSON body, and the established library for it would have brought a
dependency injection container, a mediator and a logging framework into a package whose entire
dependency list is three entries and which ships as a `dotnet tool`.

Every handler takes an `Analysis` and a request and returns a response. Nothing in `Handlers/`
touches a stream, which is what lets `src/ProLang.Tests/Lsp/` drive them directly, with no process
and no pipe.

## Three constraints worth knowing before changing it

**Analysis is single-threaded.** `BuiltInModule`'s registry and `DotNetAssemblyRegistry` are
process-wide mutable state — the test project already disables parallelism because of it — so
binding two documents at once would race on both.

**Every open file is its own compilation.** ProLang has no project file: what a program consists of
is decided by what it imports. Two open files that both import `std/util` therefore bind it twice.
That is accepted rather than solved, because solving it means inventing a project system.

**Half-typed input is the normal case.** `GetDiagnostics` deliberately stops at the first stage that
failed, so that one unresolved import does not report every name in the file as undefined. The
semantic model deliberately does *not*: it forces binding regardless, because a file being typed
into almost always has a syntax error somewhere, and stopping would mean the editor knew nothing
about a file precisely while it was being written. Both call sites are guarded, because the binder
throws outright on a few syntax shapes that no one would compile and someone will certainly type.
