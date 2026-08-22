namespace ProLang.Compiler;

/// <summary>
/// The two properties of an emitted assembly that depend on how it will be launched rather than
/// on the program's own contents: which subsystem it runs in, and which shared framework it asks
/// the host to load.
/// </summary>
/// <remarks>
/// <para>
/// Both were hardcoded — every assembly was a console-subsystem <c>.dll</c> requesting
/// <c>Microsoft.NETCore.App</c>. That is right for the console programs the compiler was written
/// for and wrong for anything using Windows Forms, which needs
/// <c>Microsoft.WindowsDesktop.App</c> and a console window it does not own.
/// </para>
/// <para>
/// The workaround was to rewrite the emitted <c>runtimeconfig.json</c> from a build script after
/// the compiler had finished with it — see the history of
/// <c>examples/13-winforms/build.ps1</c>. That put a fact the compiler knows into a file the
/// compiler had already written, so every new GUI program had to rediscover it.
/// </para>
/// <para>
/// This is a record with defaults rather than extra parameters on <c>Emit</c> so that the many
/// existing callers — the test harness, the benchmarks, the REPL — keep compiling and keep
/// producing byte-identical output. <see cref="Default"/> is exactly the old behaviour.
/// </para>
/// </remarks>
/// <param name="TargetKind">The subsystem the assembly declares.</param>
/// <param name="FrameworkName">
/// The shared framework named in <c>runtimeconfig.json</c>, for example
/// <c>Microsoft.WindowsDesktop.App</c>.
/// </param>
/// <param name="FrameworkVersion">The version of that framework to request.</param>
public sealed record EmitOptions(
    EmitTargetKind TargetKind = EmitTargetKind.Library,
    string FrameworkName = EmitOptions.DefaultFrameworkName,
    string FrameworkVersion = EmitOptions.DefaultFrameworkVersion)
{
    /// <summary>The framework a program gets when it does not ask for another.</summary>
    public const string DefaultFrameworkName = "Microsoft.NETCore.App";

    /// <summary>The framework version requested alongside <see cref="DefaultFrameworkName"/>.</summary>
    public const string DefaultFrameworkVersion = "10.0.0";

    /// <summary>The framework that supplies Windows Forms and System.Drawing.</summary>
    public const string WindowsDesktopFrameworkName = "Microsoft.WindowsDesktop.App";

    /// <summary>What the compiler emitted before either property was configurable.</summary>
    public static EmitOptions Default { get; } = new();

    /// <summary>
    /// A Windows Forms program: windows subsystem, and the desktop framework.
    /// </summary>
    /// <remarks>
    /// Paired deliberately. Selecting the windows subsystem without the desktop framework
    /// produces an assembly that starts and then fails to resolve <c>System.Windows.Forms</c>,
    /// with no console attached to report it on.
    /// </remarks>
    public static EmitOptions WindowsForms { get; } =
        new(EmitTargetKind.WindowsApplication, WindowsDesktopFrameworkName);
}

/// <summary>Which subsystem an emitted assembly declares.</summary>
public enum EmitTargetKind
{
    /// <summary>A <c>.dll</c>, launched with <c>dotnet app.dll</c>. The default.</summary>
    Library,

    /// <summary>A console-subsystem application.</summary>
    ConsoleApplication,

    /// <summary>
    /// A windows-subsystem application: no console window is allocated when it starts.
    /// </summary>
    WindowsApplication,
}
