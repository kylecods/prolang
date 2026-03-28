# Installation Guide

## Prerequisites

Before installing the ProLang VS Code extension, you need:

1. **Visual Studio Code** (v1.80.0 or higher)
   - Download from: https://code.visualstudio.com/

2. **Node.js** (v16 or higher) and **npm**
   - Download from: https://nodejs.org/
   - Or use a version manager:
     ```bash
     # Using nvm (Linux/Mac)
     curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.0/install.sh | bash
     nvm install 18
     nvm use 18
     
     # Using fnm (Windows/Linux/Mac)
     # Windows (PowerShell)
     winget install Schniz.fnm
     
     # Verify installation
     node --version
     npm --version
     ```

## Installation Options

### Option 1: Install from VSIX (Recommended for Development)

1. **Navigate to the extension directory**:
   ```bash
   cd /home/kyle/Documents/programs/prolang/vscode-extension
   ```

2. **Install Node.js dependencies**:
   ```bash
   npm install
   ```

3. **Compile TypeScript**:
   ```bash
   npm run compile
   ```

4. **Package the extension**:
   ```bash
   npm run package
   ```
   This creates a `prolang-0.1.0.vsix` file.

5. **Install the VSIX in VS Code**:
   ```bash
   # Command line
   code --install-extension prolang-0.1.0.vsix
   
   # Or in VS Code:
   # 1. Open VS Code
   # 2. Go to Extensions (Ctrl+Shift+X)
   # 3. Click the "..." menu
   # 4. Select "Install from VSIX..."
   # 5. Choose the .vsix file
   ```

### Option 2: Run Extension from Source (Development Mode)

1. **Open the extension folder in VS Code**:
   ```bash
   cd /home/kyle/Documents/programs/prolang/vscode-extension
   code .
   ```

2. **Install dependencies**:
   ```bash
   npm install
   ```

3. **Start the development host**:
   - Press `F5` or go to Run → Start Debugging
   - This opens a new VS Code window with the extension loaded

4. **Test the extension**:
   - Open a `.prl` file in the new window
   - Verify syntax highlighting, completions, etc.

### Option 3: Publish to VS Code Marketplace

1. **Install vsce**:
   ```bash
   npm install -g @vscode/vsce
   ```

2. **Create a Microsoft account** and publish at:
   https://marketplace.visualstudio.com/manage

3. **Create a publisher** (one-time):
   ```bash
   vsce create-publisher prolang-team
   ```

4. **Login**:
   ```bash
   vsce login prolang-team
   ```

5. **Package and publish**:
   ```bash
   vsce package
   vsce publish
   ```

## Verifying Installation

After installation, verify the extension is working:

1. **Open a `.prl` file** in VS Code
2. **Check syntax highlighting** - keywords should be colored
3. **Try a snippet** - type `func` and press Tab
4. **Check the status bar** - should show "ProLang" in the bottom right

## Troubleshooting

### Extension not activating

- Make sure you're opening a `.prl` or `.prolang` file
- Check the Output panel → "ProLang Language Server" for errors

### Language server not starting

1. Check Node.js version: `node --version` (should be v16+)
2. Check if dependencies are installed: `ls node_modules`
3. Reinstall dependencies: `npm install`
4. Check Output panel → "ProLang Language Server"

### Syntax highlighting not working

1. Check file extension is `.prl` or `.prolang`
2. Try: View → Command Palette → "Change Language Mode" → "ProLang"
3. Reload VS Code: Ctrl+Shift+P → "Developer: Reload Window"

### Debugging issues

- Make sure the debug adapter is compiled: `npm run compile`
- Check the Debug Console for errors
- Try setting `"stopOnEntry": true` in launch.json

## Updating

To update after making changes:

```bash
# Recompile
npm run compile

# Repackage (if needed)
npm run package

# Reinstall
code --install-extension prolang-0.1.0.vsix --force
```

## Uninstalling

```bash
# Command line
code --uninstall-extension prolang-team.prolang

# Or in VS Code:
# Extensions → ProLang → Uninstall
```
