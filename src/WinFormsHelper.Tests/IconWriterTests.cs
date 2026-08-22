using System.Drawing;

using Helper = WinFormsHelper.WinFormsHelper;

namespace WinFormsHelperTests;

/// <summary>
/// Covers writing a multi-resolution <c>.ico</c> from drawn bitmaps.
/// </summary>
/// <remarks>
/// These touch GDI+ bitmaps but no window, so they need no STA thread. They share the helper's
/// process-wide bitmap registry, so they run in one collection rather than in parallel.
/// </remarks>
[Collection("WinFormsHelper")]
public sealed class IconWriterTests
{
    /// <summary>Creates a bitmap of one colour and returns its handle.</summary>
    private static int Solid(int size, int argb)
    {
        var id = Helper.CreateBitmap(size, size);
        var pixels = new int[size * size];

        Array.Fill(pixels, argb);
        Helper.SetPixels(id, pixels, size, size);

        return id;
    }

    private static string ScratchFile(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "prolang-icon-tests");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, $"{Guid.NewGuid():N}-{name}");
    }

    [Fact]
    public void SaveIcon_WritesAFileWindowsCanRead()
    {
        var path = ScratchFile("program.ico");
        var ids = new[] { Solid(16, unchecked((int)0xFF3366CC)), Solid(32, unchecked((int)0xFF3366CC)) };

        try
        {
            Assert.True(Helper.SaveIcon(ids, path));

            // The real assertion: Windows' own icon loader accepts it.
            using var icon = new Icon(path);
            Assert.True(icon.Width > 0);
        }
        finally
        {
            foreach (var id in ids) { Helper.DisposeBitmap(id); }
            File.Delete(path);
        }
    }

    /// <summary>
    /// Every size asked for is present and picked out individually.
    /// </summary>
    /// <remarks>
    /// This is the whole point of a multi-resolution icon: Windows must find an image at the size
    /// it wants rather than scaling the nearest. An icon carrying one image would still load, so
    /// checking that it loads proves nothing.
    /// </remarks>
    [Fact]
    public void SaveIcon_KeepsEverySizeSeparately()
    {
        var path = ScratchFile("sizes.ico");
        var sizes = new[] { 16, 32, 48 };
        var ids = sizes.Select(size => Solid(size, unchecked((int)0xFF20A020))).ToArray();

        try
        {
            Assert.True(Helper.SaveIcon(ids, path));

            foreach (var size in sizes)
            {
                using var icon = new Icon(path, size, size);
                Assert.Equal(size, icon.Width);
                Assert.Equal(size, icon.Height);
            }
        }
        finally
        {
            foreach (var id in ids) { Helper.DisposeBitmap(id); }
            File.Delete(path);
        }
    }

    /// <summary>
    /// Colour survives the round trip, right way up and without the channels swapped.
    /// </summary>
    /// <remarks>
    /// The image is stored bottom-up and as BGRA rather than ARGB, so a flip or a channel swap is
    /// the natural mistake here. Neither would stop the icon loading — it would just be wrong, and
    /// at 16 pixels wrong is easy to miss.
    /// </remarks>
    [Fact]
    public void SaveIcon_PreservesTheImage()
    {
        var path = ScratchFile("colour.ico");
        var id = Helper.CreateBitmap(4, 4);

        // A different colour in each corner, so an upside-down or mirrored image shows up.
        var pixels = new int[16];
        Array.Fill(pixels, unchecked((int)0xFF000000));
        pixels[0] = unchecked((int)0xFFFF0000);   // top left, red
        pixels[3] = unchecked((int)0xFF00FF00);   // top right, green
        pixels[12] = unchecked((int)0xFF0000FF);  // bottom left, blue
        Helper.SetPixels(id, pixels, 4, 4);

        try
        {
            Assert.True(Helper.SaveIcon([id], path));

            using var icon = new Icon(path);
            using var bitmap = icon.ToBitmap();

            Assert.Equal(Color.FromArgb(255, 255, 0, 0), bitmap.GetPixel(0, 0));
            Assert.Equal(Color.FromArgb(255, 0, 255, 0), bitmap.GetPixel(3, 0));
            Assert.Equal(Color.FromArgb(255, 0, 0, 255), bitmap.GetPixel(0, 3));
        }
        finally
        {
            Helper.DisposeBitmap(id);
            File.Delete(path);
        }
    }

    /// <summary>
    /// The directory is ordered smallest first and its offsets address the images.
    /// </summary>
    /// <remarks>
    /// Read out of the raw bytes rather than through <see cref="Icon"/>, because a directory whose
    /// offsets are wrong by a fixed amount can still load — the loader finds *an* image, just not
    /// the one the entry describes.
    /// </remarks>
    [Fact]
    public void SaveIcon_WritesADirectoryThatAddressesItsImages()
    {
        var path = ScratchFile("directory.ico");
        var ids = new[] { Solid(32, unchecked((int)0xFF808080)), Solid(16, unchecked((int)0xFF808080)) };

        try
        {
            Assert.True(Helper.SaveIcon(ids, path));

            var bytes = File.ReadAllBytes(path);

            Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
            Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(2, BitConverter.ToUInt16(bytes, 4));

            // Written smallest first, whatever order the handles arrived in.
            Assert.Equal(16, bytes[6]);
            Assert.Equal(32, bytes[6 + 16]);

            var previousEnd = 6 + (16 * 2);

            for (var i = 0; i < 2; i++)
            {
                var entry = 6 + (i * 16);
                var length = BitConverter.ToInt32(bytes, entry + 8);
                var offset = BitConverter.ToInt32(bytes, entry + 12);

                Assert.Equal(previousEnd, offset);
                Assert.True(offset + length <= bytes.Length, "an entry runs past the end of the file");

                // An uncompressed image starts with the 40-byte header size.
                Assert.Equal(40, BitConverter.ToInt32(bytes, offset));

                previousEnd = offset + length;
            }

            Assert.Equal(bytes.Length, previousEnd);
        }
        finally
        {
            foreach (var id in ids) { Helper.DisposeBitmap(id); }
            File.Delete(path);
        }
    }

    /// <summary>The largest size is stored compressed, which is why the format allows it.</summary>
    /// <remarks>
    /// A 256-pixel image is a quarter of a megabyte uncompressed. Writing every entry that way
    /// works, and produces an icon file larger than the program carrying it.
    /// </remarks>
    [Fact]
    public void SaveIcon_CompressesTheLargestSize()
    {
        var path = ScratchFile("large.ico");
        var id = Solid(256, unchecked((int)0xFF404040));

        try
        {
            Assert.True(Helper.SaveIcon([id], path));

            var bytes = File.ReadAllBytes(path);
            var offset = BitConverter.ToInt32(bytes, 6 + 12);

            // The PNG signature, rather than the 40 an uncompressed image would start with.
            Assert.Equal(0x89, bytes[offset]);
            Assert.Equal((byte)'P', bytes[offset + 1]);
            Assert.Equal((byte)'N', bytes[offset + 2]);
            Assert.Equal((byte)'G', bytes[offset + 3]);

            Assert.True(bytes.Length < 256 * 256 * 4, "the largest size was not compressed");

            // Zero is how 256 is written: the field is one byte and 256 does not fit in it.
            Assert.Equal(0, bytes[6]);
        }
        finally
        {
            Helper.DisposeBitmap(id);
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveIcon_RefusesToWriteNothing()
    {
        var path = ScratchFile("empty.ico");

        Assert.False(Helper.SaveIcon([], path));
        Assert.False(File.Exists(path));
    }

    /// <summary>Handles that no longer exist are skipped rather than throwing.</summary>
    [Fact]
    public void SaveIcon_IgnoresDisposedBitmaps()
    {
        var path = ScratchFile("stale.ico");
        var live = Solid(16, unchecked((int)0xFF112233));
        var dead = Solid(32, unchecked((int)0xFF112233));

        Helper.DisposeBitmap(dead);

        try
        {
            Assert.True(Helper.SaveIcon([live, dead], path));
            Assert.Equal(1, BitConverter.ToUInt16(File.ReadAllBytes(path), 4));
        }
        finally
        {
            Helper.DisposeBitmap(live);
            File.Delete(path);
        }
    }
}
