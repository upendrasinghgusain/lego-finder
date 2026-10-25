using SkiaSharp;

namespace LegoFinder.Core.Imaging;

/// <summary>Image decoding, resampling and encoding. Every bitmap is RGBA8888 so pixel code can read bytes directly.</summary>
public static class ImageIo
{
    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public static SKImageInfo Info(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

    /// <summary>Decodes a photo and applies its EXIF orientation, so pixels match what a person sees.</summary>
    public static SKBitmap LoadOriented(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"Not a supported image: {path}");
        var raw = new SKBitmap(Info(codec.Info.Width, codec.Info.Height));
        var result = codec.GetPixels(raw.Info, raw.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            raw.Dispose();
            throw new InvalidDataException($"Could not decode {path}: {result}");
        }

        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft)
        {
            return raw;
        }

        using (raw)
        {
            return ApplyOrigin(raw, codec.EncodedOrigin);
        }
    }

    /// <summary>Returns a new bitmap transformed from the stored orientation to the display orientation.</summary>
    internal static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width;
        float h = source.Height;
        var swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        // Maps source (x, y) to destination: x' = sx*x + kx*y + tx, y' = ky*x + sy*y + ty.
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var output = new SKBitmap(Info(swaps ? source.Height : source.Width, swaps ? source.Width : source.Height));
        using var canvas = new SKCanvas(output);
        canvas.SetMatrix(m);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return output;
    }

    /// <summary>Copies <paramref name="area"/> of <paramref name="source"/> into a new bitmap of the given size.</summary>
    public static SKBitmap Resample(SKBitmap source, SKRectI area, int width, int height)
    {
        var output = new SKBitmap(Info(Math.Max(1, width), Math.Max(1, height)));
        using var canvas = new SKCanvas(output);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, area, new SKRect(0, 0, output.Width, output.Height), Smooth);
        return output;
    }

    /// <summary>Scales so the long side is at most <paramref name="maxSide"/>; returns a copy either way.</summary>
    public static SKBitmap FitWithin(SKBitmap source, int maxSide) => FitWithin(source, new SKRectI(0, 0, source.Width, source.Height), maxSide);

    public static SKBitmap FitWithin(SKBitmap source, SKRectI area, int maxSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(area.Width, area.Height));
        return Resample(source, area, (int)Math.Round(area.Width * scale), (int)Math.Round(area.Height * scale));
    }

    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality = 90)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality)
            ?? throw new InvalidOperationException("JPEG encoding failed.");
        return data.ToArray();
    }

    /// <summary>The bitmap's pixels as tightly packed RGBA bytes.</summary>
    public static byte[] RgbaBytes(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Rgba8888)
        {
            throw new ArgumentException("Expected an RGBA8888 bitmap.", nameof(bitmap));
        }

        var rowBytes = bitmap.Width * 4;
        var span = bitmap.GetPixelSpan();
        if (bitmap.RowBytes == rowBytes)
        {
            return span.ToArray();
        }

        var packed = new byte[rowBytes * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            span.Slice(y * bitmap.RowBytes, rowBytes).CopyTo(packed.AsSpan(y * rowBytes));
        }

        return packed;
    }
}
