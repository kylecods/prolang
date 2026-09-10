# ProLang OpenGL Cube Example

Hardware-accelerated 3D rendering in ProLang using the OpenGL standard library module (`std/gl.prl`).

## Overview

This example demonstrates how to use ProLang's OpenGL bindings to:
- Open a native window with an active OpenGL rendering context
- Enable and configure hardware depth testing (`GL.DEPTH_TEST`)
- Set up perspective projection via `gl_perspective`
- Position and rotate camera and 3D objects with `gl_translatef` and `gl_rotatef`
- Render 3D geometry using immediate mode quads and distinct face colors
- Present double-buffered frames smoothly with `gl_swap_buffers`
- Handle keyboard input (press **Escape** to close)

## Running the Example

### Using the build script (PowerShell)

```powershell
.\build.ps1 -Run
```

### Manual compilation

```powershell
# 1. Build the compiler (also compiles GLHelper into lib/)
dotnet build src/ProLang/ProLang.csproj -c Release

# 2. Compile cube.prl
dotnet run --project src/ProLang/ProLang.csproj -c Release -- examples/19-opengl-cube/cube.prl -o bin/cube.dll

# 3. Run the compiled executable
dotnet bin/cube.dll
```

## How It Works

The example imports `"gl"` from ProLang's standard library:

```prolang
import "gl"

func main() {
    let win: int = gl_create_window("OpenGL 3D Cube - ProLang", 800, 600)
    gl_enable(GL.DEPTH_TEST)

    while (!gl_window_should_close(win)) {
        gl_poll_events()
        ...
        gl_swap_buffers(win)
    }

    gl_close_window(win)
}
```

The underlying implementation uses `GLHelper`, a native Win32/WGL interop assembly located in `std/GLHelper`, deployed alongside the compiler output.
