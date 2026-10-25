using LegoFinder.Core.Colour;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Imaging;
using SkiaSharp;

namespace LegoFinder.Core.Scanning;

public sealed record SegmenterOptions
{
    /// <summary>Photos are downscaled to this long side for analysis (coordinates stay normalised).</summary>
    public int AnalysisMaxSide { get; init; } = 1600;

    /// <summary>CIELAB distance from the background above which a pixel counts as part of a piece.</summary>
    public double ForegroundThreshold { get; init; } = 18;

    public double MinAreaFraction { get; init; } = 0.0003;
    public double MaxAreaFraction { get; init; } = 0.25;

    /// <summary>Width of the border band used to estimate the background colour, as a fraction of the short side.</summary>
    public double BorderFraction { get; init; } = 0.02;

    /// <summary>Dilation radius (pixels) used to join small gaps before labelling.</summary>
    public int DilateRadius { get; init; } = 1;

    /// <summary>Backgrounds with less chroma than this are treated as neutral and used for white balance.</summary>
    public double NeutralBackgroundChroma { get; init; } = 12;
}

public sealed record SegmentedPiece(Box Box, double AreaFraction, Rgb MeasuredColour);

public sealed record Segmentation(Rgb Background, IReadOnlyList<SegmentedPiece> Pieces);

/// <summary>
/// Finds pieces lying on a plain background: background estimate → colour-distance mask → connected components.
/// </summary>
public static class PieceSegmenter
{
    public static Segmentation Segment(SKBitmap photo, SegmenterOptions? options = null)
    {
        options ??= new SegmenterOptions();
        using var small = ImageIo.FitWithin(photo, options.AnalysisMaxSide);
        return Segment(ImageIo.RgbaBytes(small), small.Width, small.Height, options);
    }

    /// <summary>Segments tightly packed RGBA pixels.</summary>
    internal static Segmentation Segment(byte[] pixels, int w, int h, SegmenterOptions options)
    {

        var background = EstimateBackground(pixels, w, h, options.BorderFraction);
        var bgLab = ColourMath.ToLab(background);

        var mask = new bool[w * h];
        for (var i = 0; i < mask.Length; i++)
        {
            var o = i * 4;
            mask[i] = ColourMath.DeltaE(ColourMath.ToLab(pixels[o], pixels[o + 1], pixels[o + 2]), bgLab) > options.ForegroundThreshold;
        }

        var joined = options.DilateRadius > 0 ? Dilate(mask, w, h, options.DilateRadius) : mask;
        var (labels, count) = Label(joined, w, h);

        // Per component: bounding box and area over the *undilated* mask, so boxes stay tight.
        var minX = Filled(count, int.MaxValue);
        var minY = Filled(count, int.MaxValue);
        var maxX = Filled(count, -1);
        var maxY = Filled(count, -1);
        var area = new int[count];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (!mask[i])
                {
                    continue;
                }

                var l = labels[i];
                area[l]++;
                minX[l] = Math.Min(minX[l], x);
                minY[l] = Math.Min(minY[l], y);
                maxX[l] = Math.Max(maxX[l], x);
                maxY[l] = Math.Max(maxY[l], y);
            }
        }

        var total = (double)(w * h);
        var kept = Enumerable.Range(0, count)
            .Where(l => area[l] / total >= options.MinAreaFraction && area[l] / total <= options.MaxAreaFraction)
            .ToList();

        var colours = MedianColours(pixels, mask, labels, kept, count);
        var gains = WhiteBalanceGains(background, bgLab, options.NeutralBackgroundChroma);

        var pieces = kept
            .Select(l => new SegmentedPiece(
                new Box(
                    (double)minX[l] / w,
                    (double)minY[l] / h,
                    (double)(maxX[l] - minX[l] + 1) / w,
                    (double)(maxY[l] - minY[l] + 1) / h),
                area[l] / total,
                Apply(colours[l], gains)))
            // Reading order: top-to-bottom in rows roughly one piece tall, then left-to-right.
            .OrderBy(p => Math.Round(p.Box.CentreY * 20))
            .ThenBy(p => p.Box.CentreX)
            .ToList();

        return new Segmentation(background, pieces);
    }

    internal static Rgb EstimateBackground(byte[] pixels, int w, int h, double borderFraction)
    {
        var band = Math.Max(1, (int)Math.Round(Math.Min(w, h) * borderFraction));
        var hist = new int[3, 256];
        var n = 0;
        for (var y = 0; y < h; y++)
        {
            var inRowBand = y < band || y >= h - band;
            for (var x = 0; x < w; x++)
            {
                if (!inRowBand && x >= band && x < w - band)
                {
                    continue;
                }

                var o = (y * w + x) * 4;
                hist[0, pixels[o]]++;
                hist[1, pixels[o + 1]]++;
                hist[2, pixels[o + 2]]++;
                n++;
            }
        }

        return new Rgb(Median(hist, 0, n), Median(hist, 1, n), Median(hist, 2, n));
    }

    internal static bool[] Dilate(bool[] mask, int w, int h, int r)
    {
        // Separable square dilation: horizontal pass then vertical pass.
        var horizontal = new bool[mask.Length];
        for (var y = 0; y < h; y++)
        {
            var lastOn = int.MinValue / 2;
            for (var x = 0; x < w + r; x++)
            {
                if (x < w && mask[y * w + x])
                {
                    lastOn = x;
                }

                var target = x - r;
                if (target >= 0 && x - lastOn <= 2 * r)
                {
                    horizontal[y * w + target] = true;
                }
            }
        }

        var result = new bool[mask.Length];
        for (var x = 0; x < w; x++)
        {
            var lastOn = int.MinValue / 2;
            for (var y = 0; y < h + r; y++)
            {
                if (y < h && horizontal[y * w + x])
                {
                    lastOn = y;
                }

                var target = y - r;
                if (target >= 0 && y - lastOn <= 2 * r)
                {
                    result[target * w + x] = true;
                }
            }
        }

        return result;
    }

    /// <summary>8-connected component labelling. Background pixels get label -1.</summary>
    internal static (int[] Labels, int Count) Label(bool[] mask, int w, int h)
    {
        var labels = new int[mask.Length];
        Array.Fill(labels, -1);
        var count = 0;
        var stack = new Stack<int>();
        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || labels[start] >= 0)
            {
                continue;
            }

            labels[start] = count;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                var x = i % w;
                var y = i / w;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= h)
                    {
                        continue;
                    }

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= w)
                        {
                            continue;
                        }

                        var j = ny * w + nx;
                        if (mask[j] && labels[j] < 0)
                        {
                            labels[j] = count;
                            stack.Push(j);
                        }
                    }
                }
            }

            count++;
        }

        return (labels, count);
    }

    private static Dictionary<int, Rgb> MedianColours(byte[] pixels, bool[] mask, int[] labels, List<int> kept, int count)
    {
        var slot = Filled(count, -1);
        for (var k = 0; k < kept.Count; k++)
        {
            slot[kept[k]] = k;
        }

        var hists = kept.Select(_ => new int[3, 256]).ToArray();
        var counts = new int[kept.Count];
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
            {
                continue;
            }

            var s = slot[labels[i]];
            if (s < 0)
            {
                continue;
            }

            var o = i * 4;
            hists[s][0, pixels[o]]++;
            hists[s][1, pixels[o + 1]]++;
            hists[s][2, pixels[o + 2]]++;
            counts[s]++;
        }

        return kept.Select((l, k) => (l, k)).ToDictionary(
            t => t.l,
            t => new Rgb(Median(hists[t.k], 0, counts[t.k]), Median(hists[t.k], 1, counts[t.k]), Median(hists[t.k], 2, counts[t.k])));
    }

    /// <summary>Per-channel gains that make a near-neutral background grey, correcting the camera's colour cast.</summary>
    internal static (double R, double G, double B) WhiteBalanceGains(Rgb background, Lab backgroundLab, double neutralChroma)
    {
        if (ColourMath.Chroma(backgroundLab) > neutralChroma || background.R == 0 || background.G == 0 || background.B == 0)
        {
            return (1, 1, 1);
        }

        var mean = (background.R + background.G + background.B) / 3.0;
        return (mean / background.R, mean / background.G, mean / background.B);
    }

    private static Rgb Apply(Rgb c, (double R, double G, double B) gains) => new(
        (byte)Math.Clamp(Math.Round(c.R * gains.R), 0, 255),
        (byte)Math.Clamp(Math.Round(c.G * gains.G), 0, 255),
        (byte)Math.Clamp(Math.Round(c.B * gains.B), 0, 255));

    private static byte Median(int[,] hist, int channel, int n)
    {
        var half = (n + 1) / 2;
        var seen = 0;
        for (var v = 0; v < 256; v++)
        {
            seen += hist[channel, v];
            if (seen >= half)
            {
                return (byte)v;
            }
        }

        return 0;
    }

    private static int[] Filled(int length, int value)
    {
        var a = new int[length];
        Array.Fill(a, value);
        return a;
    }
}
