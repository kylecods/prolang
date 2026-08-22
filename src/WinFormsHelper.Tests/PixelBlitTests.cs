using Helper = WinFormsHelper.WinFormsHelper;

namespace WinFormsHelperTests;

/// <summary>
/// Covers the bulk pixel transfer and the PNG round-trip.
/// </summary>
/// <remarks>
/// These touch GDI+ bitmaps but no window, so they do not need an STA thread. They share the
/// helper's process-wide bitmap registry, so they run in one collection rather than in parallel.
/// </remarks>
[Collection("WinFormsHelper")]
public sealed class PixelBlitTests
{
    private static int[] Ramp(int width, int height)
    {
        var pixels = new int[width * height];

        for (var i = 0; i < pixels.Length; i++)
        {
            // Opaque, with each channel varying independently so a transposed or mis-strided
            // copy shows up as a mismatch rather than as a plausible-looking image.
            pixels[i] = Helper.MakeColor(255, i % 256, (i * 7) % 256, (i * 13) % 256);
        }

        return pixels;
    }

    [Fact]
    public void MakeColor_PacksArgbInTheDocumentedOrder()
    {
        var packed = Helper.MakeColor(255, 0x12, 0x34, 0x56);

        Assert.Equal(0x12, (packed >> 16) & 0xFF);
        Assert.Equal(0x34, (packed >> 8) & 0xFF);
        Assert.Equal(0x56, packed & 0xFF);
        Assert.Equal(255, (packed >> 24) & 0xFF);
    }

    /// <summary>
    /// An alpha above 127 sets the sign bit, so an opaque colour is a negative int. Callers rely
    /// on the bit pattern surviving that, not on the value being positive.
    /// </summary>
    [Fact]
    public void OpaqueColours_AreNegativeAndSurviveTheRoundTrip()
    {
        var white = Helper.MakeColor(255, 255, 255, 255);
        Assert.True(white < 0);

        var id = Helper.CreateBitmap(1, 1);
        try
        {
            Helper.SetPixels(id, [white], 1, 1);
            Assert.Equal(white, Helper.GetPixel(id, 0, 0));
        }
        finally
        {
            Helper.DisposeBitmap(id);
        }
    }

    [Theory]
    // A range of widths, because BitmapData.Stride is rounded up to a four-byte boundary and is
    // not required to equal width * 4. Copying the whole buffer in one block would pass at some
    // of these sizes and skew the image at others.
    [InlineData(1, 1)]
    [InlineData(3, 5)]
    [InlineData(7, 3)]
    [InlineData(16, 16)]
    [InlineData(33, 17)]
    [InlineData(64, 64)]
    public void SetPixels_ThenGetPixels_RoundTrips(int width, int height)
    {
        var source = Ramp(width, height);
        var id = Helper.CreateBitmap(width, height);

        try
        {
            Helper.SetPixels(id, source, width, height);

            var readBack = new int[width * height];
            Helper.GetPixels(id, readBack, width, height);

            Assert.Equal(source, readBack);
        }
        finally
        {
            Helper.DisposeBitmap(id);
        }
    }

    /// <summary>
    /// The bulk path and the single-pixel path have to agree, or a dirty-pixel update would
    /// disagree with a full repaint of the same image.
    /// </summary>
    [Fact]
    public void SetPixels_AgreesWithGetPixel()
    {
        const int Width = 5;
        const int Height = 4;

        var source = Ramp(Width, Height);
        var id = Helper.CreateBitmap(Width, Height);

        try
        {
            Helper.SetPixels(id, source, Width, Height);

            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    Assert.Equal(source[(y * Width) + x], Helper.GetPixel(id, x, y));
                }
            }
        }
        finally
        {
            Helper.DisposeBitmap(id);
        }
    }

    [Fact]
    public void SetPixelsAt_WritesOnlyTheListedPixels()
    {
        const int Width = 4;
        const int Height = 4;

        var background = Helper.MakeColor(255, 0, 0, 0);
        var mark = Helper.MakeColor(255, 255, 0, 0);

        var id = Helper.CreateBitmap(Width, Height);

        try
        {
            var blank = new int[Width * Height];
            Array.Fill(blank, background);
            Helper.SetPixels(id, blank, Width, Height);

            // Indices 0, 5 and 10 are the leading diagonal.
            Helper.SetPixelsAt(id, [0, 5, 10], [mark, mark, mark], 3, Width);

            Assert.Equal(mark, Helper.GetPixel(id, 0, 0));
            Assert.Equal(mark, Helper.GetPixel(id, 1, 1));
            Assert.Equal(mark, Helper.GetPixel(id, 2, 2));
            Assert.Equal(background, Helper.GetPixel(id, 3, 3));
            Assert.Equal(background, Helper.GetPixel(id, 1, 0));
        }
        finally
        {
            Helper.DisposeBitmap(id);
        }
    }

    /// <summary>
    /// Out-of-range indices are skipped rather than throwing: the caller is a prolang program,
    /// which has no way to catch an exception.
    /// </summary>
    [Fact]
    public void SetPixelsAt_IgnoresOutOfRangeIndices()
    {
        var id = Helper.CreateBitmap(2, 2);

        try
        {
            var mark = Helper.MakeColor(255, 1, 2, 3);
            Helper.SetPixelsAt(id, [-1, 99, 0], [mark, mark, mark], 3, 2);

            Assert.Equal(mark, Helper.GetPixel(id, 0, 0));
        }
        finally
        {
            Helper.DisposeBitmap(id);
        }
    }

    [Fact]
    public void SetPixels_WithATooSmallBuffer_DoesNothing()
    {
        var id = Helper.CreateBitmap(4, 4);

        try
        {
            var before = Helper.GetPixel(id, 0, 0);
            Helper.SetPixels(id, new int[2], 4, 4);

            Assert.Equal(before, Helper.GetPixel(id, 0, 0));
        }
        finally
        {
            Helper.DisposeBitmap(id);
        }
    }

    [Fact]
    public void SaveAndLoadPng_RoundTripsEveryPixel()
    {
        const int Width = 12;
        const int Height = 9;

        var source = Ramp(Width, Height);

        // A fully transparent pixel, to prove the alpha channel survives the encode.
        source[0] = 0;

        var path = Path.Combine(Path.GetTempPath(), $"prolang-blit-{Guid.NewGuid():N}.png");
        var saved = Helper.CreateBitmap(Width, Height);

        try
        {
            Helper.SetPixels(saved, source, Width, Height);
            Assert.True(Helper.SaveBitmapPng(saved, path));

            var loaded = Helper.LoadBitmapPng(path);
            Assert.True(loaded >= 0);

            try
            {
                Assert.Equal(Width, Helper.GetBitmapWidth(loaded));
                Assert.Equal(Height, Helper.GetBitmapHeight(loaded));

                var readBack = new int[Width * Height];
                Helper.GetPixels(loaded, readBack, Width, Height);

                Assert.Equal(source, readBack);
            }
            finally
            {
                Helper.DisposeBitmap(loaded);
            }
        }
        finally
        {
            Helper.DisposeBitmap(saved);
            File.Delete(path);
        }
    }

    /// <summary>
    /// A missing file reports -1 rather than throwing, because the prolang caller has no way to
    /// recover from an exception.
    /// </summary>
    [Fact]
    public void LoadPng_OfAMissingFile_ReturnsMinusOne()
        => Assert.Equal(-1, Helper.LoadBitmapPng(Path.Combine(Path.GetTempPath(), "prolang-does-not-exist.png")));

    [Fact]
    public void SavePng_ToAnUnwritablePath_ReturnsFalse()
    {
        var id = Helper.CreateBitmap(2, 2);

        try
        {
            Assert.False(Helper.SaveBitmapPng(id, Path.Combine(Path.GetTempPath(), "no-such-dir", "x.png")));
        }
        finally
        {
            Helper.DisposeBitmap(id);
        }
    }

    [Fact]
    public void BitmapDimensions_OfAnUnknownHandle_AreZero()
    {
        Assert.Equal(0, Helper.GetBitmapWidth(987654));
        Assert.Equal(0, Helper.GetBitmapHeight(987654));
    }
}

/// <summary>
/// Serialises the tests that share the helper's process-wide bitmap and control registries.
/// </summary>
[CollectionDefinition("WinFormsHelper", DisableParallelization = true)]
public sealed class WinFormsHelperCollection;
