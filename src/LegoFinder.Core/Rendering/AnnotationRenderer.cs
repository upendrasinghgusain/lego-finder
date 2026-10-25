using System.Globalization;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Imaging;
using SkiaSharp;

namespace LegoFinder.Core.Rendering;

/// <summary>One circle to draw: the parts-line number and where the piece is.</summary>
public sealed record Annotation(int Number, Box Box, bool Confident);

/// <summary>Draws numbered rings around matched pieces on a copy of the photo.</summary>
public static class AnnotationRenderer
{
    /// <summary>High-contrast ring colours, cycled by line number so neighbouring numbers differ.</summary>
    public static readonly string[] Palette =
    [
        "FF1744", "00E5FF", "FFEA00", "76FF03", "FF9100", "D500F9", "2979FF", "FF4081", "1DE9B6", "C6FF00",
    ];

    private static readonly SKColor Outline = new(0, 0, 0, 200);
    private static readonly SKColor BadgeFill = SKColor.Parse("111111");

    public static string RingHex(int number) => Palette[(Math.Max(1, number) - 1) % Palette.Length];

    public static SKColor RingColour(int number) => SKColor.Parse(RingHex(number));

    /// <summary>Geometry of one annotation in pixels; exposed so tests can check placement.</summary>
    public readonly record struct Placement(float Cx, float Cy, float Radius, float BadgeX, float BadgeY, float BadgeRadius, float Stroke);

    public static Placement Place(Annotation a, int width, int height)
    {
        var minSide = Math.Min(width, height);
        var stroke = MathF.Max(3, minSide * 0.004f);
        var badgeRadius = MathF.Max(16, minSide * 0.022f);
        var cx = (float)(a.Box.CentreX * width);
        var cy = (float)(a.Box.CentreY * height);
        var radius = (float)(0.5 * Math.Sqrt(Math.Pow(a.Box.W * width, 2) + Math.Pow(a.Box.H * height, 2))) + stroke * 2;

        // Badge sits on the ring at the upper-left, kept inside the image.
        var bx = Math.Clamp(cx - radius * 0.7071f, badgeRadius, Math.Max(badgeRadius, width - badgeRadius));
        var by = Math.Clamp(cy - radius * 0.7071f, badgeRadius, Math.Max(badgeRadius, height - badgeRadius));
        return new Placement(cx, cy, radius, bx, by, badgeRadius, stroke);
    }

    /// <summary>Returns a new image; <paramref name="photo"/> is not modified.</summary>
    public static SKBitmap Render(SKBitmap photo, IEnumerable<Annotation> annotations)
    {
        var output = ImageIo.Resample(photo, new SKRectI(0, 0, photo.Width, photo.Height), photo.Width, photo.Height);
        var placed = annotations.Select(a => (A: a, P: Place(a, output.Width, output.Height))).ToList();

        using var canvas = new SKCanvas(output);
        using var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Outline };
        using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = BadgeFill };

        // Rings first, then badges, so a badge is never hidden under a neighbour's ring.
        foreach (var (a, p) in placed)
        {
            outline.StrokeWidth = p.Stroke * 2;
            canvas.DrawCircle(p.Cx, p.Cy, p.Radius, outline);
            Style(ring, a, p.Stroke);
            canvas.DrawCircle(p.Cx, p.Cy, p.Radius, ring);
            ring.PathEffect?.Dispose();
        }

        foreach (var (a, p) in placed)
        {
            var text = a.Number.ToString(CultureInfo.InvariantCulture);
            canvas.DrawCircle(p.BadgeX, p.BadgeY, p.BadgeRadius, fill);
            Style(ring, a, p.Stroke);
            canvas.DrawCircle(p.BadgeX, p.BadgeY, p.BadgeRadius, ring);
            ring.PathEffect?.Dispose();

            var cell = MathF.Min(p.BadgeRadius * 1.1f / DigitFont.GlyphHeight, p.BadgeRadius * 1.5f / DigitFont.CellWidth(text));
            DigitFont.Draw(canvas, text, p.BadgeX, p.BadgeY, cell, SKColors.White);
        }

        canvas.Flush();
        return output;
    }

    private static void Style(SKPaint paint, Annotation a, float stroke)
    {
        paint.Color = RingColour(a.Number);
        paint.StrokeWidth = stroke;
        paint.PathEffect = a.Confident ? null : SKPathEffect.CreateDash([stroke * 3, stroke * 2], 0);
    }
}

/// <summary>Draws every detection in a scanned photo, numbered by its scan id (e.g. "tray#7" → 7), for checking segmentation.</summary>
public static class DetectionPreview
{
    public static void Write(string photoPath, ScannedPhoto photo, string outputPath, int jpegQuality = 85)
    {
        using var image = ImageIo.LoadOriented(photoPath);
        var annotations = photo.Detections.Select((d, i) => new Annotation(
            int.TryParse(d.Id.AsSpan(d.Id.LastIndexOf('#') + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : i + 1,
            d.Box,
            Confident: d.Candidates.Count > 0));
        using var output = AnnotationRenderer.Render(image, annotations);
        File.WriteAllBytes(outputPath, ImageIo.EncodeJpeg(output, jpegQuality));
    }
}
