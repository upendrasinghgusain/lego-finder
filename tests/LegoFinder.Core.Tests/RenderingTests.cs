using LegoFinder.Core.Contracts;
using LegoFinder.Core.Rendering;
using SkiaSharp;

namespace LegoFinder.Core.Tests;

public class RenderingTests
{
    [Fact]
    public void Draws_a_ring_in_the_line_colour_and_leaves_the_original_untouched()
    {
        using var photo = TestImages.Tray(400, 400, (new SKRect(180, 180, 220, 220), "C91A09"));
        var annotation = new Annotation(2, new Box(0.45, 0.45, 0.1, 0.1), Confident: true);
        var p = AnnotationRenderer.Place(annotation, 400, 400);

        using var output = AnnotationRenderer.Render(photo, [annotation]);

        Assert.Equal((400, 400), (output.Width, output.Height));
        // Rightmost point of the ring is the ring colour; the original there is still background.
        var ringX = (int)Math.Round(p.Cx + p.Radius);
        var ringY = (int)Math.Round(p.Cy);
        Assert.True(Close(output.GetPixel(ringX, ringY), AnnotationRenderer.RingColour(2)), $"got {output.GetPixel(ringX, ringY)}");
        Assert.Equal(TestImages.Background, photo.GetPixel(ringX, ringY));
        // The piece itself is not painted over.
        Assert.Equal(SKColor.Parse("C91A09"), output.GetPixel(200, 200));
    }

    [Fact]
    public void Badge_shows_white_digits_on_a_dark_disc()
    {
        using var photo = TestImages.Tray(400, 400);
        var annotation = new Annotation(1, new Box(0.45, 0.45, 0.1, 0.1), true);
        var p = AnnotationRenderer.Place(annotation, 400, 400);

        using var output = AnnotationRenderer.Render(photo, [annotation]);

        var pixels = Square(output, p.BadgeX, p.BadgeY, p.BadgeRadius * 0.6f).ToList();
        Assert.Contains(pixels, c => c.Red > 240 && c.Green > 240 && c.Blue > 240);
        Assert.Contains(pixels, c => c.Red < 30 && c.Green < 30 && c.Blue < 30);
    }

    [Fact]
    public void Uncertain_matches_get_a_dashed_ring()
    {
        using var photo = TestImages.Tray(400, 400);
        var box = new Box(0.4, 0.4, 0.2, 0.2);
        var p = AnnotationRenderer.Place(new Annotation(3, box, true), 400, 400);

        using var solid = AnnotationRenderer.Render(photo, [new Annotation(3, box, true)]);
        using var dashed = AnnotationRenderer.Render(photo, [new Annotation(3, box, false)]);

        var ring = AnnotationRenderer.RingColour(3);
        Assert.True(RingCoverage(solid, p, ring) > 0.95);
        Assert.InRange(RingCoverage(dashed, p, ring), 0.3, 0.8);
    }

    [Fact]
    public void Badges_stay_inside_the_image()
    {
        var p = AnnotationRenderer.Place(new Annotation(12, new Box(0, 0, 0.05, 0.05), true), 300, 200);

        Assert.True(p.BadgeX >= p.BadgeRadius && p.BadgeY >= p.BadgeRadius);
    }

    [Fact]
    public void Sizes_scale_with_the_photo_but_have_minimums()
    {
        var small = AnnotationRenderer.Place(new Annotation(1, new Box(0.4, 0.4, 0.2, 0.2), true), 200, 200);
        var big = AnnotationRenderer.Place(new Annotation(1, new Box(0.4, 0.4, 0.2, 0.2), true), 4000, 3000);

        Assert.Equal((3f, 16f), (small.Stroke, small.BadgeRadius));
        Assert.Equal(12f, big.Stroke, 3);
        Assert.Equal(66f, big.BadgeRadius, 3);
    }

    [Fact]
    public void Ring_colours_cycle_through_the_palette()
    {
        Assert.Equal(AnnotationRenderer.Palette[0], AnnotationRenderer.RingHex(1));
        Assert.Equal(AnnotationRenderer.RingHex(1), AnnotationRenderer.RingHex(1 + AnnotationRenderer.Palette.Length));
        Assert.NotEqual(AnnotationRenderer.RingHex(1), AnnotationRenderer.RingHex(2));
        Assert.Equal(AnnotationRenderer.RingHex(1), AnnotationRenderer.RingHex(0));
    }

    [Fact]
    public void Digit_font_glyphs()
    {
        Assert.True(DigitFont.IsSet('1', 2, 0));
        Assert.False(DigitFont.IsSet('1', 0, 0));
        Assert.False(DigitFont.IsSet('x', 0, 0));
        Assert.Equal(0, DigitFont.CellWidth(""));
        Assert.Equal(5, DigitFont.CellWidth("7"));
        Assert.Equal(17, DigitFont.CellWidth("123"));

        // Every digit is distinct.
        var shapes = "0123456789".Select(d => string.Concat(
            Enumerable.Range(0, 35).Select(i => DigitFont.IsSet(d, i % 5, i / 5) ? '#' : '.'))).ToList();
        Assert.Equal(10, shapes.Distinct().Count());
    }

    [Fact]
    public void Digit_font_draws_only_digits()
    {
        using var bitmap = new SKBitmap(Imaging.ImageIo.Info(60, 30));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        DigitFont.Draw(canvas, "8", 30, 15, 3, SKColors.White);
        canvas.Flush();

        // Glyph top is 15 - 7*3/2 = 4.5, so pixel row 5–6 is inside the top bar of "8"; row 0 is above it.
        Assert.Equal(SKColors.White, bitmap.GetPixel(30, 6));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(30, 1));
        Assert.Throws<ArgumentException>(() => DigitFont.Draw(canvas, "1a", 0, 0, 1, SKColors.White));
    }

    private static IEnumerable<SKColor> Square(SKBitmap bitmap, float cx, float cy, float half)
    {
        for (var y = (int)(cy - half); y <= (int)(cy + half); y++)
        {
            for (var x = (int)(cx - half); x <= (int)(cx + half); x++)
            {
                yield return bitmap.GetPixel(x, y);
            }
        }
    }

    /// <summary>Fraction of points around the ring (away from the badge) drawn in the ring colour.</summary>
    private static double RingCoverage(SKBitmap bitmap, AnnotationRenderer.Placement p, SKColor ring)
    {
        var hits = 0;
        var total = 0;
        for (var deg = 0; deg < 360; deg += 2)
        {
            if (deg is > 200 and < 250)
            {
                continue; // badge sits at the upper-left (225°)
            }

            var a = deg * Math.PI / 180;
            var x = (int)Math.Round(p.Cx + p.Radius * Math.Cos(a));
            var y = (int)Math.Round(p.Cy + p.Radius * Math.Sin(a));
            total++;
            if (Close(bitmap.GetPixel(x, y), ring))
            {
                hits++;
            }
        }

        return (double)hits / total;
    }

    private static bool Close(SKColor a, SKColor b) =>
        Math.Abs(a.Red - b.Red) < 40 && Math.Abs(a.Green - b.Green) < 40 && Math.Abs(a.Blue - b.Blue) < 40;
}
