# ProLang OpenGL UI Widgets Demo

Hardware-accelerated UI widgets with an embedded interactive 3D OpenGL viewport.

## Overview

This example demonstrates how ProLang's UI widget toolkit runs on top of the built-in OpenGL backend (`std/ui/host_gl.prl`):
- **Hardware-accelerated UI widgets**: Buttons, styled cards, columns, rows, spacers, and labels laid out dynamically and rendered via OpenGL.
- **Embedded 3D Viewport (`Ui->gl_scene`)**: An interactive 3D cube rendered inside a UI card with automatic viewport and scissor clipping.
- **Real-time FPS tracking**: Uses `std/ui/fps` to measure rendering performance.
- **Interactive Controls**:
  - Buttons for rotating along the X, Y, and Z axes
  - Auto-spin toggle
  - Wireframe mode toggle (`gl_polygon_mode`)
  - Dynamic theme switcher cycling through ProLang UI themes
  - Direct mouse dragging to rotate the 3D model

## Building and Running

### Using the build script (PowerShell)

```powershell
.\build.ps1 -Run
```

### Manual compilation

```powershell
# 1. Build the compiler
dotnet build src/ProLang/ProLang.csproj -c Release

# 2. Compile the demo
dotnet run --project src/ProLang/ProLang.csproj -c Release --no-build -- `
    examples/20-opengl-widgets/main.prl --target=winexe --apphost -o bin/opengl_widgets.dll

# 3. Run
.\bin\opengl_widgets.exe
```
