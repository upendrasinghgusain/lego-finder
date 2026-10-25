using System.Security.Cryptography;
using LegoFinder.Core.Colour;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Imaging;
using SkiaSharp;

namespace LegoFinder.Core.Scanning;

public sealed record ScanOptions
{
    public SegmenterOptions Segmenter { get; init; } = new();

    /// <summary>Padding added around each piece when cropping it for the recogniser, as a fraction of its size.</summary>
    public double CropPadding { get; init; } = 0.15;
    public int CropMaxSide { get; init; } = 512;
    public int Concurrency { get; init; } = 4;
}

/// <summary>Segments a photo and recognises each piece, using the cache where possible.</summary>
public sealed class PhotoScanner(IPartRecogniser? recogniser, RecognitionCache? cache, ScanOptions? options = null, Action<string>? log = null)
{
    private readonly ScanOptions _options = options ?? new ScanOptions();

    public string RecogniserName => recogniser?.Name ?? "none";

    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png"];

    /// <summary>Expands folders into the photos they contain (non-recursive, sorted); files are kept as given.</summary>
    public static IReadOnlyList<string> ExpandPhotoPaths(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var p in paths)
        {
            if (System.IO.Directory.Exists(p))
            {
                result.AddRange(System.IO.Directory.EnumerateFiles(p)
                    .Where(IsPhoto)
                    .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith(".annotated", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase));
            }
            else if (File.Exists(p))
            {
                result.Add(p);
            }
            else
            {
                throw new FileNotFoundException($"Photo or folder not found: {p}");
            }
        }

        return result;
    }

    public static bool IsPhoto(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <param name="path">Photo to scan.</param>
    /// <param name="recordedPath">Path written into the result (e.g. relative to the scan file).</param>
    /// <param name="shouldRecognise">Optional colour pre-filter; pieces it rejects are marked <see cref="RecognitionStatus.Skipped"/>.</param>
    public async Task<ScannedPhoto> ScanAsync(
        string path,
        string? recordedPath = null,
        Func<Rgb, bool>? shouldRecognise = null,
        CancellationToken ct = default)
    {
        var sha = Sha256Of(path);
        using var image = ImageIo.LoadOriented(path);
        var segmentation = PieceSegmenter.Segment(image, _options.Segmenter);
        var stem = Path.GetFileNameWithoutExtension(path);

        var detections = segmentation.Pieces
            .Select((p, i) => new Detection
            {
                Id = $"{stem}#{i + 1}",
                Box = Round(p.Box),
                AreaFraction = Math.Round(p.AreaFraction, 6),
                Colour = new DetectionColour { Measured = p.MeasuredColour.ToHex() },
            })
            .ToArray();

        if (recogniser is not null)
        {
            var work = new List<(int Index, string Key, byte[]? Jpeg)>();
            for (var i = 0; i < detections.Length; i++)
            {
                if (shouldRecognise is not null && !shouldRecognise(segmentation.Pieces[i].MeasuredColour))
                {
                    detections[i] = detections[i] with { Recognition = RecognitionStatus.Skipped };
                    continue;
                }

                var (key, jpeg) = Crop(image, segmentation.Pieces[i].Box);
                if (cache?.TryGet(key) is { } hit)
                {
                    detections[i] = With(detections[i], hit, RecognitionStatus.Cached);
                }
                else
                {
                    work.Add((i, key, jpeg));
                }
            }

            log?.Invoke($"{Path.GetFileName(path)}: {detections.Length} pieces, {work.Count} to recognise, "
                + $"{detections.Count(d => d.Recognition == RecognitionStatus.Cached)} cached, "
                + $"{detections.Count(d => d.Recognition == RecognitionStatus.Skipped)} skipped by colour");

            await Parallel.ForEachAsync(
                work,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.Concurrency), CancellationToken = ct },
                async (item, token) =>
                {
                    try
                    {
                        var result = await recogniser.RecogniseAsync(item.Jpeg!, token);
                        cache?.Put(item.Key, result);
                        detections[item.Index] = With(detections[item.Index], result, RecognitionStatus.Ok);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException
                        && !token.IsCancellationRequested)
                    {
                        log?.Invoke($"warning: recognising {detections[item.Index].Id} failed: {ex.Message}");
                        detections[item.Index] = detections[item.Index] with { Recognition = RecognitionStatus.Failed };
                    }
                });
        }
        else
        {
            log?.Invoke($"{Path.GetFileName(path)}: {detections.Length} pieces (no recogniser)");
        }

        return new ScannedPhoto
        {
            Path = recordedPath ?? path,
            Sha256 = sha,
            Width = image.Width,
            Height = image.Height,
            Background = segmentation.Background.ToHex(),
            Detections = detections,
        };
    }

    private (string Key, byte[] Jpeg) Crop(SKBitmap image, Box box)
    {
        var padX = box.W * _options.CropPadding;
        var padY = box.H * _options.CropPadding;
        var x0 = (int)Math.Floor(Math.Max(0, box.X - padX) * image.Width);
        var y0 = (int)Math.Floor(Math.Max(0, box.Y - padY) * image.Height);
        var x1 = (int)Math.Ceiling(Math.Min(1, box.X + box.W + padX) * image.Width);
        var y1 = (int)Math.Ceiling(Math.Min(1, box.Y + box.H + padY) * image.Height);
        var rect = new SKRectI(x0, y0, Math.Max(x0 + 1, x1), Math.Max(y0 + 1, y1));

        using var crop = ImageIo.FitWithin(image, rect, _options.CropMaxSide);
        var key = RecognitionCache.Key(RecogniserName, ImageIo.RgbaBytes(crop), crop.Width, crop.Height);
        return (key, ImageIo.EncodeJpeg(crop));
    }

    private static Detection With(Detection d, Recognition r, RecognitionStatus status) => d with
    {
        Candidates = r.Candidates,
        Colour = d.Colour with { Predicted = r.Colours },
        Recognition = status,
    };

    private static Box Round(Box b) => new(Math.Round(b.X, 5), Math.Round(b.Y, 5), Math.Round(b.W, 5), Math.Round(b.H, 5));
}
