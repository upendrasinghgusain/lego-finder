using LegoFinder.Core.Imaging;
using SkiaSharp;

namespace LegoFinder.Core.Tests;

public class ImageIoTests
{
    /// <summary>A 4×2 image whose top-left pixel is red and the rest white.</summary>
    private static SKBitmap Marked()
    {
        var bitmap = new SKBitmap(ImageIo.Info(4, 2));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        bitmap.SetPixel(0, 0, SKColors.Red);
        return bitmap;
    }

    // Where the stored top-left pixel ends up for each EXIF orientation, and the displayed size.
    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 0, 0, 4, 2)]
    [InlineData(SKEncodedOrigin.TopRight, 3, 0, 4, 2)]
    [InlineData(SKEncodedOrigin.BottomRight, 3, 1, 4, 2)]
    [InlineData(SKEncodedOrigin.BottomLeft, 0, 1, 4, 2)]
    [InlineData(SKEncodedOrigin.LeftTop, 0, 0, 2, 4)]
    [InlineData(SKEncodedOrigin.RightTop, 1, 0, 2, 4)]
    [InlineData(SKEncodedOrigin.RightBottom, 1, 3, 2, 4)]
    [InlineData(SKEncodedOrigin.LeftBottom, 0, 3, 2, 4)]
    public void Applies_exif_orientation(SKEncodedOrigin origin, int x, int y, int width, int height)
    {
        using var source = Marked();

        using var oriented = ImageIo.ApplyOrigin(source, origin);

        Assert.Equal((width, height), (oriented.Width, oriented.Height));
        Assert.Equal(SKColors.Red, oriented.GetPixel(x, y));
        Assert.Equal(1, Enumerable.Range(0, width * height).Count(i => oriented.GetPixel(i % width, i / width) == SKColors.Red));
    }

    [Fact]
    public void Loads_png_and_rejects_non_images()
    {
        using var dir = new TempDir();
        using var source = Marked();
        var png = TestImages.SavePng(source, dir.File("a.png"));
        File.WriteAllText(dir.File("b.jpg"), "not an image");

        using var loaded = ImageIo.LoadOriented(png);

        Assert.Equal((4, 2), (loaded.Width, loaded.Height));
        Assert.Equal(SKColorType.Rgba8888, loaded.ColorType);
        Assert.Equal(SKColors.Red, loaded.GetPixel(0, 0));
        Assert.Throws<InvalidDataException>(() => ImageIo.LoadOriented(dir.File("b.jpg")));
    }

    [Theory]
    [InlineData(1000, 500, 100, 100, 50)]
    [InlineData(500, 1000, 100, 50, 100)]
    [InlineData(80, 60, 100, 80, 60)]
    public void Fits_within_a_maximum_side(int w, int h, int max, int expectedW, int expectedH)
    {
        using var source = new SKBitmap(ImageIo.Info(w, h));

        using var fitted = ImageIo.FitWithin(source, max);

        Assert.Equal((expectedW, expectedH), (fitted.Width, fitted.Height));
    }

    [Fact]
    public void Resamples_a_sub_area()
    {
        using var tray = TestImages.Tray(100, 100, (new SKRect(50, 50, 100, 100), "C91A09"));

        using var crop = ImageIo.Resample(tray, new SKRectI(50, 50, 100, 100), 10, 10);

        Assert.Equal(SKColor.Parse("C91A09"), crop.GetPixel(5, 5));
    }

    [Fact]
    public void Encodes_jpeg_that_decodes_back()
    {
        using var tray = TestImages.Tray(64, 32, (new SKRect(0, 0, 32, 32), "C91A09"));

        var jpeg = ImageIo.EncodeJpeg(tray, 95);

        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        using var decoded = SKBitmap.Decode(jpeg);
        Assert.Equal((64, 32), (decoded.Width, decoded.Height));
    }

    [Fact]
    public void Rgba_bytes_are_tightly_packed()
    {
        using var source = Marked();

        var bytes = ImageIo.RgbaBytes(source);

        Assert.Equal(4 * 2 * 4, bytes.Length);
        Assert.Equal([255, 0, 0, 255], bytes[..4]);
        Assert.Equal([255, 255, 255, 255], bytes[4..8]);
    }

    [Fact]
    public void Rgba_bytes_rejects_other_pixel_formats()
    {
        using var bgra = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888));
        Assert.Throws<ArgumentException>(() => ImageIo.RgbaBytes(bgra));
    }
}
