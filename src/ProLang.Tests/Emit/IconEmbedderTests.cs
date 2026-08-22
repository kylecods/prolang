using ProLang.Compiler;
using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Emit;

/// <summary>
/// Covers embedding a program icon into the native launcher.
/// </summary>
/// <remarks>
/// <para>
/// The icon goes in as Win32 resources, which means the <c>.ico</c> file is taken apart: each
/// image becomes its own resource and the file's directory is rewritten as a group whose entries
/// point at resource ids rather than at file offsets.
/// </para>
/// <para>
/// That rewrite is where this can go wrong, and it goes wrong silently. A malformed group is still
/// a valid resource in a valid executable — Windows simply cannot parse it and falls back to the
/// generic icon, with nothing written anywhere to say why. So the group is checked byte for byte
/// rather than by asking whether the build succeeded.
/// </para>
/// </remarks>
public sealed class IconEmbedderTests
{
    private const string HelloSource = """
        import "io"

        func main()
        {
            print("hello")
        }
        """;

    /// <summary>A group entry is 14 bytes: 8 of descriptor, a 4-byte length, a 2-byte id.</summary>
    /// <remarks>
    /// The file's own entries are 16, because they end in a 4-byte offset. Carrying too many bytes
    /// across from the file is the mistake this size exists to pin down.
    /// </remarks>
    private const int GroupEntrySize = 14;

    [Fact]
    public void Embed_AddsTheImagesAndAWellFormedGroup()
    {
        using var scratch = ScratchDirectory.Create("icon-embed");

        if (!TryBuildLauncher(scratch, out var launcher))
        {
            return;
        }

        var sizes = new[] { 16, 32, 48 };
        var iconPath = WriteIcon(scratch, sizes);
        var before = new FileInfo(launcher).Length;

        Assert.True(IconEmbedder.TryEmbed(launcher, iconPath, out var error), error);

        var executable = File.ReadAllBytes(launcher);

        Assert.True(executable.Length > before, "the executable did not grow");

        // A resource directory, which the apphost template does not have until one is added.
        Assert.Contains(".rsrc", ReadSectionNames(executable));

        // Every image, byte for byte.
        foreach (var size in sizes)
        {
            var image = ImageFor(size);
            Assert.True(
                executable.AsSpan().IndexOf(image) >= 0,
                $"the {size}-pixel image is not in the executable");
        }

        // And the group that ties them together, which is what Windows actually reads.
        Assert.True(
            executable.AsSpan().IndexOf(ExpectedGroup(sizes)) >= 0,
            "the icon group is absent or malformed");
    }

    [Fact]
    public void Embed_RefusesAFileThatIsNotAnIcon()
    {
        using var scratch = ScratchDirectory.Create("icon-not-an-icon");

        if (!TryBuildLauncher(scratch, out var launcher))
        {
            return;
        }

        var notAnIcon = scratch.File("notes.txt");
        File.WriteAllText(notAnIcon, "this is not an icon file at all");

        Assert.False(IconEmbedder.TryEmbed(launcher, notAnIcon, out var error));
        Assert.Contains("not a usable icon file", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// An icon whose directory points past the end of the file is refused rather than copied.
    /// </summary>
    /// <remarks>
    /// Without the bounds check this reads whatever follows in memory and embeds it, producing a
    /// program that builds cleanly and shows a corrupt icon — a failure with nothing to trace it
    /// back to the file that caused it.
    /// </remarks>
    [Fact]
    public void Embed_RefusesAnIconThatLiesAboutItsContents()
    {
        using var scratch = ScratchDirectory.Create("icon-truncated");

        if (!TryBuildLauncher(scratch, out var launcher))
        {
            return;
        }

        var icon = File.ReadAllBytes(WriteIcon(scratch, [16]));
        var truncated = scratch.File("truncated.ico");
        File.WriteAllBytes(truncated, icon[..(icon.Length / 2)]);

        Assert.False(IconEmbedder.TryEmbed(launcher, truncated, out var error));
        Assert.Contains("lies outside the file", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Embed_ReportsAMissingFile()
    {
        using var scratch = ScratchDirectory.Create("icon-missing");

        if (!TryBuildLauncher(scratch, out var launcher))
        {
            return;
        }

        Assert.False(IconEmbedder.TryEmbed(launcher, scratch.File("absent.ico"), out var error));
        Assert.Contains("no icon file at", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Compiles a trivial program and writes a launcher for it, or reports that the machine has no
    /// apphost template to work from.
    /// </summary>
    private static bool TryBuildLauncher(ScratchDirectory scratch, out string launcher)
    {
        var source = scratch.File("hello.prl");
        File.WriteAllText(source, HelloSource);

        var result = CompilerHarness.CompileToFile(scratch.Path, source);
        Assert.True(result.Succeeded, result.DiagnosticText);

        return AppHost.TryCreate(result.AssemblyPath!, windowsGui: false, out launcher, out _);
    }

    /// <summary>Writes a minimal but valid multi-resolution icon file.</summary>
    /// <remarks>
    /// Built here rather than committed, so the test carries its own input and the bytes it asserts
    /// on are the bytes it wrote. Each image is a 32-bit uncompressed bitmap, which is the icon
    /// format's original encoding and needs no image library to produce.
    /// </remarks>
    private static string WriteIcon(ScratchDirectory scratch, int[] sizes)
    {
        var path = scratch.File("program.ico");

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // 1 = icon
        writer.Write((ushort)sizes.Length);

        var offset = 6 + (16 * sizes.Length);

        foreach (var size in sizes)
        {
            var image = ImageFor(size);

            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);          // palette size
            writer.Write((byte)0);          // reserved
            writer.Write((ushort)1);        // planes
            writer.Write((ushort)32);       // bits per pixel
            writer.Write(image.Length);
            writer.Write(offset);

            offset += image.Length;
        }

        foreach (var size in sizes)
        {
            writer.Write(ImageFor(size));
        }

        return path;
    }

    /// <summary>
    /// One image: a BITMAPINFOHEADER, opaque pixels, and the mask the format requires.
    /// </summary>
    /// <remarks>
    /// The pixel bytes vary with the size so that finding one in the executable proves that
    /// image was embedded, rather than proving some image was.
    /// </remarks>
    private static byte[] ImageFor(int size)
    {
        var pixels = size * size * 4;
        var maskStride = ((size + 31) / 32) * 4;
        var mask = maskStride * size;

        using var buffer = new MemoryStream(40 + pixels + mask);
        using var writer = new BinaryWriter(buffer);

        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);             // colour rows plus mask rows
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);                    // uncompressed
        writer.Write(pixels + mask);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (var i = 0; i < pixels; i++)
        {
            writer.Write((byte)((i + size) % 251));
        }

        writer.Write(new byte[mask]);

        writer.Flush();
        return buffer.ToArray();
    }

    /// <summary>The group resource the embedder should have written for these sizes.</summary>
    private static byte[] ExpectedGroup(int[] sizes)
    {
        using var buffer = new MemoryStream(6 + (sizes.Length * GroupEntrySize));
        using var writer = new BinaryWriter(buffer);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);

        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)sizes[i]);
            writer.Write((byte)sizes[i]);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(ImageFor(sizes[i]).Length);
            writer.Write((ushort)(i + 1));  // resource id, where the file had an offset
        }

        writer.Flush();
        return buffer.ToArray();
    }

    /// <summary>The names of a PE image's sections.</summary>
    private static List<string> ReadSectionNames(byte[] image)
    {
        var peOffset = BitConverter.ToInt32(image, 0x3C);
        var sectionCount = BitConverter.ToUInt16(image, peOffset + 6);
        var optionalHeaderSize = BitConverter.ToUInt16(image, peOffset + 20);
        var firstSection = peOffset + 24 + optionalHeaderSize;

        var names = new List<string>();

        for (var i = 0; i < sectionCount; i++)
        {
            var start = firstSection + (i * 40);
            names.Add(System.Text.Encoding.ASCII.GetString(image, start, 8).TrimEnd('\0'));
        }

        return names;
    }
}
