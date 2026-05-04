#!/bin/bash

# ProLang VS Code Extension Build Script

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== ProLang VS Code Extension Build ==="
echo ""

# Check for Node.js
if ! command -v node &> /dev/null; then
    echo "❌ Node.js is not installed."
    echo ""
    echo "Please install Node.js (v16 or higher):"
    echo "  - Visit: https://nodejs.org/"
    echo "  - Or use nvm: curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.0/install.sh | bash"
    echo "             nvm install 18"
    echo ""
    exit 1
fi

# Check Node.js version
NODE_VERSION=$(node --version | cut -d'v' -f2 | cut -d'.' -f1)
if [ "$NODE_VERSION" -lt 16 ]; then
    echo "❌ Node.js version must be 16 or higher (current: $(node --version))"
    exit 1
fi

echo "✓ Node.js version: $(node --version)"
echo "✓ npm version: $(npm --version)"
echo ""

# Install dependencies
echo "📦 Installing dependencies..."
npm install
echo ""

# Compile TypeScript
echo "🔨 Compiling TypeScript..."
npm run compile
echo ""

# Check if vsce is installed for packaging
if command -v vsce &> /dev/null || command -v npx &> /dev/null; then
    echo "📦 Packaging extension..."
    if command -v vsce &> /dev/null; then
        vsce package
    else
        npx @vscode/vsce package
    fi
    echo ""
    echo "✅ Build complete! Extension package created."
    echo ""
    echo "To install:"
    echo "  code --install-extension prolang-*.vsix"
    echo ""
    echo "Or to run in development mode:"
    echo "  1. Open this folder in VS Code"
    echo "  2. Press F5"
else
    echo "✅ Build complete!"
    echo ""
    echo "To package the extension, install vsce:"
    echo "  npm install -g @vscode/vsce"
    echo ""
    echo "To run in development mode:"
    echo "  1. Open this folder in VS Code"
    echo "  2. Press F5"
fi
