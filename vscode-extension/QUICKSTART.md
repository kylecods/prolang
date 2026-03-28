# Quick Start Guide

Get the ProLang VS Code extension up and running in 5 minutes!

## Step 1: Install Node.js

The extension requires Node.js v16+.

### Windows
```powershell
winget install OpenJS.NodeJS.LTS
```

### macOS
```bash
brew install node
```

### Linux (Ubuntu/Debian)
```bash
curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash -
sudo apt-get install -y nodejs
```

### Verify installation
```bash
node --version  # Should show v16.x or higher
npm --version   # Should show 8.x or higher
```

## Step 2: Install the Extension

### Option A: Quick Install (Development)

```bash
cd /home/kyle/Documents/programs/prolang/vscode-extension
./build.sh
```

This will:
1. Install dependencies
2. Compile TypeScript
3. Package the extension

Then install:
```bash
code --install-extension prolang-0.1.0.vsix
```

### Option B: Run from Source (For Development)

```bash
cd /home/kyle/Documents/programs/prolang/vscode-extension
npm install
```

Then in VS Code:
1. Open the `vscode-extension` folder
2. Press `F5`
3. A new VS Code window opens with the extension loaded

## Step 3: Test the Extension

1. **Open a ProLang file**:
   ```bash
   code examples/hello/hello.prl
   ```

2. **Verify syntax highlighting**:
   - Keywords (`let`, `func`, `if`) should be purple
   - Types (`int`, `string`) should be cyan
   - Strings should be green
   - Comments should be gray/green

3. **Try snippets**:
   - Create a new file: `test.prl`
   - Type `func` and press `Tab`
   - Type `if` and press `Tab`

4. **Test language features**:
   - Hover over a variable to see its type
   - Press `F12` on a function name to go to definition
   - Press `Ctrl+Space` for completions

## Step 4: Debug a ProLang Program

1. Open a `.prl` file
2. Click in the left gutter to set breakpoints
3. Press `F5` to start debugging
4. Use debug controls to step through code

## Features Checklist

Test these features to verify everything works:

### Syntax Highlighting
- [ ] Keywords are colored
- [ ] Types are colored
- [ ] Strings are green
- [ ] Comments are gray/green
- [ ] Operators are colored
- [ ] Numbers are blue

### Snippets
- [ ] `func` creates function template
- [ ] `let` creates variable declaration
- [ ] `if` creates if statement
- [ ] `for` creates for loop

### Language Server
- [ ] Hover shows type information
- [ ] Completions appear on `Ctrl+Space`
- [ ] Go to Definition works (F12)
- [ ] Find References works (Shift+F12)
- [ ] Outline view shows functions/variables

### Debugging
- [ ] Can set breakpoints
- [ ] Can step through code
- [ ] Can see variables in debug view

## Troubleshooting

### "Node.js not found"
Install Node.js from https://nodejs.org/

### Extension not activating
- Make sure file extension is `.prl` or `.prolang`
- Try: `Ctrl+Shift+P` → "Change Language Mode" → "ProLang"

### No syntax highlighting
- Reload VS Code: `Ctrl+Shift+P` → "Developer: Reload Window"
- Check file is recognized: bottom-right should say "ProLang"

### Language server errors
- Open Output panel (Ctrl+Shift+U)
- Select "ProLang Language Server" from dropdown
- Check for error messages

## Next Steps

- Read `README.md` for full documentation
- Check `INSTALL.md` for detailed installation options
- See `CHANGELOG.md` for features and known issues
- Explore example files in `examples/` folder

## Support

For issues or questions:
- Check the documentation in this folder
- Review example files in `examples/`
- Open an issue on the ProLang repository
