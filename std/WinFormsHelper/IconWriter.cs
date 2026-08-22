using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WinFormsHelper;

/// <summary>
/// Builds Windows icons from bitmaps: <c>.ico</c> files for a program's own executable, and
/// in-memory icons for a window's title bar and taskbar button.
/// </summary>
/// <remarks>
/// <para>
/// An icon is not one image but several, so that Windows can pick the right one for each place it
/// appears — 16 pixels in a title bar, 32 in Alt-Tab, 256 on a large-icon desktop. Handing it a
/// single image and letting it rescale is exactly what produces the blurry application icons that
/// give away a program nobody finished.
/// </para>
/// <para>
/// This exists rather than <see cref="Bitmap.GetHicon"/>, which is the obvious call and the wrong
/// one: it reduces the alpha channel to a one-bit mask, so a glyph with any soft edge comes out
/// with a hard jagged outline. Writing the icon format directly keeps the 32-bit alpha the
/// supersampled artwork was drawn with.
/// </para>
/// </remarks>
public static partial class WinFormsHelper
{
    /// <summary>Icons are 32-bit BGRA; there is no reason to write anything else today.</summary>
    private const ushort IconBitCount = 32;

    /// <summary>
    /// Sizes at or above this are written as PNG rather than as an uncompressed bitmap.
    /// </summary>
    /// <remarks>
    /// A 256-pixel image is 256 KB uncompressed and around a tenth of that as PNG, which is why
    /// the format allows it. Below that the saving is not worth the compatibility question:
    /// PNG-compressed entries need Windows Vista or later, and uncompressed ones always work.
    /// </remarks>
    private const int PngThreshold = 256;

    /// <summary>
    /// Writes a multi-resolution <c>.ico</c> from bitmaps that have already been drawn.
    /// </summary>
    /// <param name="bitmapIds">
    /// Handles from <see cref="CreateBitmap"/>, one per resolution. Order does not matter.
    /// </param>
    /// <param name="path">Where to write the file.</param>
    /// <returns>False if nothing was drawn, or the file could not be written.</returns>
    /// <remarks>
    /// Each bitmap should be square and should have been drawn at its own size rather than scaled
    /// from one master — that is the entire point of a multi-resolution icon, and a prolang program
    /// using <c>ui/icons</c> gets it for nothing, since those are described in units and rasterised
    /// at whatever size is asked for.
    /// </remarks>
    public static bool SaveIcon(int[] bitmapIds, string path)
    {
        var bitmaps = Resolve(bitmapIds);

        if (bitmaps.Count == 0)
        {
            return false;
        }

        try
        {
            using var file = File.Create(path);
            WriteIcon(file, bitmaps);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sets the icon a window shows in its title bar, the taskbar and Alt-Tab.
    /// </summary>
    /// <param name="formId">Handle from <see cref="CreateForm"/>.</param>
    /// <param name="bitmapIds">Handles of the drawn images, one per resolution.</param>
    /// <returns>False if nothing was drawn or the icon could not be built.</returns>
    /// <remarks>
    /// Separate from the icon embedded in the executable, and worth setting even when that is
    /// present: the embedded one is what Explorer and the Start Menu show, and this is what the
    /// running window shows. A program that sets only one of them looks finished in half the
    /// places it appears.
    /// </remarks>
    public static bool SetWindowIcon(int formId, int[] bitmapIds)
    {
        var bitmaps = Resolve(bitmapIds);

        if (bitmaps.Count == 0)
        {
            return false;
        }

        try
        {
            using var buffer = new MemoryStream();
            WriteIcon(buffer, bitmaps);
            buffer.Position = 0;

            // The Icon owns its own copy of the bytes, so the stream can go.
            Get<Form>(formId).Icon = new Icon(buffer);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or IOException or ExternalException)
        {
            return false;
        }
    }

    /// <summary>Bitmaps for the given handles, skipping any that no longer exist.</summary>
    private static List<Bitmap> Resolve(int[] bitmapIds)
    {
        var bitmaps = new List<Bitmap>();

        foreach (var id in bitmapIds)
        {
            if (_bitmaps.TryGetValue(id, out var bitmap))
            {
                bitmaps.Add(bitmap);
            }
        }

        // Largest last is not required by the format, but is what every icon editor writes and
        // what makes a hex dump of the result readable.
        bitmaps.Sort((a, b) => a.Width.CompareTo(b.Width));

        return bitmaps;
    }

    /// <summary>
    /// Writes the ICO container: a directory of fixed-size entries, then the images.
    /// </summary>
    /// <remarks>
    /// Each directory entry has to carry the byte length and file offset of its image, so the
    /// images are encoded first and the directory written from what they turned out to be. The
    /// alternative — writing the directory with placeholders and seeking back — needs a seekable
    /// stream, which rules out writing an icon to anything but a file.
    /// </remarks>
    private static void WriteIcon(Stream output, List<Bitmap> bitmaps)
    {
        var images = bitmaps.Select(Encode).ToList();

        const int DirectoryHeaderSize = 6;
        const int DirectoryEntrySize = 16;

        var offset = DirectoryHeaderSize + (DirectoryEntrySize * images.Count);

        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // 1 = icon, 2 = cursor
        writer.Write((ushort)images.Count);

        for (var i = 0; i < images.Count; i++)
        {
            var bitmap = bitmaps[i];

            // 256 is stored as zero: the field is one byte, and 256 does not fit in it.
            writer.Write((byte)(bitmap.Width >= 256 ? 0 : bitmap.Width));
            writer.Write((byte)(bitmap.Height >= 256 ? 0 : bitmap.Height));
            writer.Write((byte)0);            // palette size; 0 for a true-colour image
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // colour planes
            writer.Write(IconBitCount);
            writer.Write(images[i].Length);
            writer.Write(offset);

            offset += images[i].Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }
    }

    /// <summary>One image, in whichever of the two encodings its size calls for.</summary>
    private static byte[] Encode(Bitmap bitmap)
        => bitmap.Width >= PngThreshold ? EncodePng(bitmap) : EncodeDib(bitmap);

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Png);
        return buffer.ToArray();
    }

    /// <summary>
    /// One image as an uncompressed device-independent bitmap, the icon format's original form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two details are easy to get wrong and produce an icon that is upside down or invisible.
    /// The header's height is <em>twice</em> the image's, because the format expects the colour
    /// data to be followed by a monochrome mask; and the rows run bottom-up, which is how a DIB
    /// has always been stored.
    /// </para>
    /// <para>
    /// The mask itself is written as all zeroes — meaning fully opaque everywhere — because
    /// Windows uses the alpha channel for a 32-bit icon and ignores it. It cannot be omitted:
    /// the size declared in the header includes it, and leaving it out truncates the image.
    /// </para>
    /// </remarks>
    private static byte[] EncodeDib(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        var pixelBytes = width * height * 4;
        var maskStride = ((width + 31) / 32) * 4;
        var maskBytes = maskStride * height;

        using var buffer = new MemoryStream(40 + pixelBytes + maskBytes);
        using var writer = new BinaryWriter(buffer);

        writer.Write(40);                     // BITMAPINFOHEADER size
        writer.Write(width);
        writer.Write(height * 2);             // colour rows plus mask rows
        writer.Write((ushort)1);              // planes
        writer.Write(IconBitCount);
        writer.Write(0);                      // BI_RGB, uncompressed
        writer.Write(pixelBytes + maskBytes);
        writer.Write(0);                      // horizontal resolution, unused
        writer.Write(0);                      // vertical resolution, unused
        writer.Write(0);                      // palette entries used
        writer.Write(0);                      // palette entries required

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, CanvasFormat);

        try
        {
            var row = new byte[width * 4];

            for (var y = height - 1; y >= 0; y--)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);
                writer.Write(row);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        writer.Write(new byte[maskBytes]);

        return buffer.ToArray();
    }
}
