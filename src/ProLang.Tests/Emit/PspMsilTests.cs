using Mono.Cecil;
using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Emit;

public sealed class PspMsilTests
{
    [Fact]
    public void PspBuiltins_EmitRuntimeImportsInsteadOfLookingForUserMethods()
    {
        using var scratch = ScratchDirectory.Create("psp-msil");
        var result = CompilerHarness.CompileSource(scratch.Path, """
            import "psp"
            func main() {
                psp_init()
                psp_clear(0)
                psp_fill_rect(1, 2, 3, 4, 5)
                psp_draw_text(1, 2, "hello", 3)
                psp_draw_line(1, 2, 3, 4, 5)
                psp_swap_buffers()
                psp_vsync()
                let buttons: int = psp_buttons_held()
                let pressed: bool = psp_button_pressed(8)
            }
            """);
        Assert.True(result.Succeeded, result.DiagnosticText);
        using var assembly = AssemblyDefinition.ReadAssembly(result.AssemblyPath!);
        var calls = assembly.MainModule.Types.SelectMany(t => t.Methods)
            .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand).OfType<MethodReference>()
            .Where(m => m.DeclaringType.FullName == "ProLang.Runtime.PspOps")
            .Select(m => m.Name).Order().ToArray();
        Assert.Equal(new[] { "ButtonPressed", "ButtonsHeld", "Clear", "DrawLine", "DrawText", "FillRect", "Init", "SwapBuffers", "Vsync" }, calls);
    }

    [Fact]
    public void PspCube_CompilesToNormalMsilWithItsStructsAndPainterDelegate()
    {
        using var scratch = ScratchDirectory.Create("psp-cube-msil");
        var result = CompilerHarness.CompileToFile(scratch.Path,
            Path.Combine(TestPaths.Examples, "18-psp-cube", "psp_cube.prl"));
        Assert.True(result.Succeeded, result.DiagnosticText);
        using var assembly = AssemblyDefinition.ReadAssembly(result.AssemblyPath!);
        Assert.Contains(assembly.MainModule.Types, t => t.Name == "PspHost" && t.IsValueType);
        Assert.Contains(assembly.MainModule.Types.SelectMany(t => t.Methods)
            .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions),
            i => i.Operand is MethodReference m && m.Name == "Invoke" && m.DeclaringType.Name == "Func`9");
    }
}
