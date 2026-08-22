using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WinFormsHelper;

/// <summary>
/// Bulk transfer between a prolang <c>array&lt;int&gt;</c> of packed ARGB values and a
/// <see cref="Bitmap"/>'s pixel buffer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WinFormsHelper.SetPixel"/> is one interop call and one <c>Bitmap.SetPixel</c> per
/// pixel. Repainting a 256×256 canvas that way is 65,536 of each, which is far too slow to do on
/// every mouse-move. These methods move the whole buffer in one call.
/// </para>
/// <para>
/// A prolang <c>array&lt;int&gt;</c> is a genuine CLR <c>int[]</c> — the compiler maps
/// <c>array&lt;T&gt;</c> onto a real array type rather than a wrapper — so the argument arrives
/// here with no marshalling and no copy, and <see cref="GetPixels"/> can fill an array the
/// prolang program allocated.
/// </para>
/// <para>
/// Every method copies row by row rather than in one block: <see cref="BitmapData.Stride"/> is
/// rounded up to a four-byte boundary and is not required to equal <c>width * 4</c>. Assuming it
/// does is correct for most widths and silently skews the image for the rest.
/// </para>
/// </remarks>
public static partial class WinFormsHelper
{
    /// <summary>The pixel format every bitmap in the registry is created with.</summary>
    /// <remarks>
    /// Fixed rather than read from the bitmap so that the packed int a prolang program computes
    /// always means the same thing. <c>Bitmap(int, int)</c> already produces this format; naming
    /// it here means <see cref="LoadBitmapPng"/> can convert a loaded file into it rather than
    /// inheriting whatever the file happened to use.
    /// </remarks>
    internal const PixelFormat CanvasFormat = PixelFormat.Format32bppArgb;

    /// <summary>
    /// Copies <paramref name="pixels"/> into the bitmap, one packed ARGB value per pixel in
    /// row-major order.
    /// </summary>
    /// <param name="bitmapId">Handle from <see cref="CreateBitmap"/>.</param>
    /// <param name="pixels">Source buffer. Must hold at least <paramref name="w"/> × <paramref name="h"/> values.</param>
    /// <param name="w">Row width, in pixels.</param>
    /// <param name="h">Row count.</param>
    public static void SetPixels(int bitmapId, int[] pixels, int w, int h)
    {
        var bmp = _bitmaps[bitmapId];
        var rect = ClampToBitmap(bmp, ref w, ref h);

        if (rect.Width == 0 || rect.Height == 0 || pixels.Length < w * h)
        {
            return;
        }

        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, CanvasFormat);

        try
        {
            for (var row = 0; row < rect.Height; row++)
            {
                Marshal.Copy(pixels, row * w, data.Scan0 + (row * data.Stride), rect.Width);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Copies the bitmap's pixels into <paramref name="dest"/>, the inverse of
    /// <see cref="SetPixels"/>.
    /// </summary>
    /// <remarks>
    /// This is how an opened PNG becomes a prolang document: the program allocates the buffer and
    /// this fills it in place, so no array has to cross the boundary as a return value.
    /// </remarks>
    public static void GetPixels(int bitmapId, int[] dest, int w, int h)
    {
        var bmp = _bitmaps[bitmapId];
        var rect = ClampToBitmap(bmp, ref w, ref h);

        if (rect.Width == 0 || rect.Height == 0 || dest.Length < w * h)
        {
            return;
        }

        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, CanvasFormat);

        try
        {
            for (var row = 0; row < rect.Height; row++)
            {
                Marshal.Copy(data.Scan0 + (row * data.Stride), dest, row * w, rect.Width);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Writes <paramref name="count"/> individual pixels, addressed by flat row-major index.
    /// </summary>
    /// <param name="bitmapId">Handle from <see cref="CreateBitmap"/>.</param>
    /// <param name="indices">Flat indices (<c>y * w + x</c>) of the pixels to write.</param>
    /// <param name="colors">Packed ARGB value for each index, positionally matched.</param>
    /// <param name="count">How many leading entries of the two arrays are live.</param>
    /// <param name="w">Row width used to decode the flat indices.</param>
    /// <remarks>
    /// This is what makes a shape-tool preview cheap. Rubber-banding a line by re-blitting the
    /// whole canvas on every mouse-move is O(width × height) per event; the tools instead record
    /// the handful of pixels they touched and repaint only those. Out-of-range indices are
    /// skipped rather than throwing, because the caller is a prolang program with no exception
    /// handling of its own.
    /// </remarks>
    public static void SetPixelsAt(int bitmapId, int[] indices, int[] colors, int count, int w)
    {
        var bmp = _bitmaps[bitmapId];

        if (w <= 0 || count <= 0 || count > indices.Length || count > colors.Length)
        {
            return;
        }

        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, CanvasFormat);

        try
        {
            for (var i = 0; i < count; i++)
            {
                var index = indices[i];
                if (index < 0)
                {
                    continue;
                }

                var x = index % w;
                var y = index / w;

                if (x >= bmp.Width || y >= bmp.Height)
                {
                    continue;
                }

                Marshal.WriteInt32(data.Scan0 + (y * data.Stride) + (x * 4), colors[i]);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Narrows a caller-supplied width and height to what the bitmap can actually hold.
    /// </summary>
    /// <remarks>
    /// The row stride still uses the caller's <paramref name="w"/> — that is the layout of their
    /// buffer — while the copy length uses the clamped width, so a mismatch truncates the image
    /// instead of reading past the end of either side.
    /// </remarks>
    private static Rectangle ClampToBitmap(Bitmap bmp, ref int w, ref int h)
    {
        if (w <= 0 || h <= 0)
        {
            return Rectangle.Empty;
        }

        return new Rectangle(0, 0, Math.Min(w, bmp.Width), Math.Min(h, bmp.Height));
    }
}
