# ProLang VS Code Extension

Language support for the ProLang programming language.

## Features

- **Syntax Highlighting**: Full syntax highlighting for `.prl` and `.prolang` files
- **Code Snippets**: Common patterns and constructs
- **Language Server**:
  - Diagnostics (error detection)
  - Hover information
  - Auto-completion
  - Go to Definition
  - Find References
  - Document Symbols (outline)
  - Signature Help
  - Rename Symbol
  - Basic Formatting
- **Debugging Support**: Debug adapter for stepping through code

## Installation

### Prerequisites

- Node.js (v16 or higher)
- npm (v8 or higher)

### Setup

1. Navigate to the extension directory:
   ```bash
   cd vscode-extension
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

3. Compile TypeScript:
   ```bash
   npm run compile
   ```

4. Run the extension:
   - Press `F5` in VS Code to launch the Extension Development Host
   - Or package the extension:
     ```bash
     npm run package
     ```
   - Install the `.vsix` file:
     ```bash
     code --install-extension prolang-0.1.0.vsix
     ```

## Usage

### Syntax Highlighting

Open any `.prl` or `.prolang` file to see syntax highlighting for:
- Keywords (`let`, `func`, `if`, `while`, etc.)
- Types (`int`, `bool`, `string`, `array`, `map`, `any`)
- Operators and punctuation
- Strings and comments
- HTML script tags

### Snippets

Type these prefixes and press `Tab`:

| Prefix | Description |
|--------|-------------|
| `func` | Function declaration |
| `let` | Variable with explicit type |
| `letv` | Variable with inferred type |
| `if` | If statement |
| `ifelse` | If-else statement |
| `ifelif` | If-elif-else statement |
| `while` | While loop |
| `for` | For loop |
| `import` | Import module |
| `print` | Print statement |
| `array` | Array literal |
| `map` | Map literal |

### Language Server Features

- **Hover**: Hover over variables to see type information
- **Go to Definition**: F12 or Ctrl+Click on a symbol
- **Find References**: Shift+F12 on a symbol
- **Rename Symbol**: F2 on a symbol
- **Outline View**: See functions and variables in the Explorer sidebar

### Debugging

1. Open a `.prl` file
2. Set breakpoints by clicking in the gutter
3. Press `F5` or go to Run and Debug
4. Select "ProLang: Launch current file"

### Commands

- `ProLang: Run ProLang File` - Run the current ProLang file
- `ProLang: Build ProLang Project` - Build the project

## Configuration

Add these settings to your `settings.json`:

```json
{
  "prolang.languageServer.trace": "off",
  "prolang.enableBuiltInCompletions": true
}
```

## Development

### Project Structure

```
vscode-extension/
├── package.json              # Extension manifest
├── language-configuration.json
├── syntaxes/
│   └── prolang.tmLanguage.json
├── snippets/
│   └── prolang.json
├── src/
│   ├── extension.ts          # Client activation
│   ├── server.ts             # Language server
│   └── debugAdapter.ts       # Debug adapter
├── icons/
│   └── prolang-icon.png
└── .vscode/
    ├── launch.json
    └── tasks.json
```

### Debugging the Extension

1. Open the `vscode-extension` folder in VS Code
2. Press `F5` to launch the Extension Development Host
3. The language server will start automatically when you open a `.prl` file

### Debugging the Language Server

1. Run the "Language Server" debug configuration
2. Check the Output panel for "ProLang Language Server" logs

## Publishing

To publish to the VS Code Marketplace:

```bash
npm install -g @vscode/vsce
vsce package
vsce publish
```

## License

MIT
