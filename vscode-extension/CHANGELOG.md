# Changelog

All notable changes to the ProLang VS Code extension will be documented in this file.

## [0.1.0] - 2026-03-28

### Added

#### Syntax Highlighting
- Full TextMate grammar for ProLang language
- Support for keywords, types, operators, and literals
- HTML script tag embedding (`<script>...</script>`)
- Block and line comments
- String literals with escape sequences

#### Language Configuration
- Auto-closing brackets and quotes
- Auto-indentation rules
- Folding markers (region/endregion)
- Comment toggling (Ctrl+/)

#### Code Snippets
- Function declarations (`func`, `funcv`, `funcrec`)
- Variable declarations (`let`, `letv`)
- Control flow (`if`, `ifelse`, `ifelif`, `while`, `for`)
- Imports (`import`, `importdn`, `importio`, `importarray`)
- Data structures (`array`, `arrayd`, `map`, `mapd`)
- Utilities (`print`, `prints`, `main`, `script`)

#### Language Server
- **Diagnostics**: Error detection for:
  - Unterminated strings
  - Invalid characters
- **Hover**: Type information for variables and functions
- **Completion**: Auto-complete for:
  - Keywords
  - Built-in types
  - Built-in modules
  - Functions and variables in scope
- **Go to Definition**: Navigate to symbol definitions
- **Find References**: Find all usages of a symbol
- **Document Symbols**: Outline view for functions and variables
- **Signature Help**: Function parameter hints
- **Rename Symbol**: Rename across file
- **Formatting**: Basic indentation formatting

#### Debug Adapter
- Basic debug adapter implementation
- Support for:
  - Breakpoints
  - Step over/in/out
  - Continue
  - Stack traces
  - Variable inspection (local and global scopes)
  - Expression evaluation (placeholder)

#### Commands
- `ProLang: Run ProLang File` - Execute current file
- `ProLang: Build ProLang Project` - Build project

#### Configuration
- `prolang.languageServer.trace` - LSP tracing
- `prolang.enableBuiltInCompletions` - Toggle built-in completions

### Technical Details
- Implemented in TypeScript
- Uses vscode-languageclient for LSP communication
- Debug adapter protocol for debugging support
- Compatible with VS Code v1.80.0+

### Known Issues
- Debug adapter is a stub - needs integration with actual ProLang runtime
- Type inference is basic - only explicit types are shown
- Import resolution doesn't search workspace yet
- No workspace-wide symbol search yet

### Future Plans
- Integrate with actual ProLang compiler/runtime
- Add workspace symbol search
- Improve type inference
- Add import resolution and module navigation
- Add code lens for running functions
- Add test integration
- Add IntelliSense for .NET interop
