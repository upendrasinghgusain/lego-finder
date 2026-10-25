using LegoFinder.Core.Colour;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Imaging;
using LegoFinder.Core.Matching;
using LegoFinder.Core.Rendering;
using LegoFinder.Core.Scanning;

namespace LegoFinder.Core.Pipeline;

public sealed record FindOptions
{
    public MatchOptions Match { get; init; } = new();

    /// <summary>
    /// With colour matching on, pieces further than this ΔE from every needed colour are not sent to the recogniser.
    /// </summary>
    public double PrefilterDistance { get; init; } = 35;

    /// <summary>Disables the colour pre-filter.</summary>
    public bool RecogniseAll { get; init; }

    public string OutDir { get; init; } = "out";
    public int JpegQuality { get; init; } = 90;
}

/// <summary>
/// What <c>legofinder find</c> does: gather scanned photos (scanning or rescanning as needed), match them against the
/// parts list and draw the annotated images.
/// </summary>
public sealed class FindRunner(PhotoScanner scanner, Action<string>? log = null)
{
    /// <param name="photoPaths">Photos (or folders) to scan now.</param>
    /// <param name="scan">An existing scan to reuse, if any.</param>
    /// <param name="scanDirectory">Directory that relative paths in <paramref name="scan"/> are resolved against.</param>
    public async Task<FindResult> RunAsync(
        PartsList parts,
        IReadOnlyList<string> photoPaths,
        ScanResult? scan,
        string? scanDirectory,
        FindOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new FindOptions();
        var warnings = new List<string>();
        // Skipping pieces by colour only makes sense when colour decides matches.
        var filter = options.RecogniseAll || !options.Match.MatchColour ? null : ColourPrefilter.For(parts, options.PrefilterDistance);
        var photos = new List<(ScannedPhoto Scan, string? File)>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var recorded in scan?.Photos ?? [])
        {
            var file = Path.IsPathRooted(recorded.Path) ? recorded.Path : Path.Combine(scanDirectory ?? ".", recorded.Path);
            if (!File.Exists(file))
            {
                Warn($"{recorded.Path}: photo file not found; its pieces are matched but can't be drawn.");
                photos.Add((recorded, null));
                continue;
            }

            seen.Add(Path.GetFullPath(file));
            if (!PhotoScanner.Sha256Of(file).Equals(recorded.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Warn($"{recorded.Path}: photo has changed since it was scanned (pieces moved?); rescanning.");
                photos.Add((await scanner.ScanAsync(file, recorded.Path, filter, ct), file));
            }
            else
            {
                photos.Add((recorded, file));
            }
        }

        foreach (var file in PhotoScanner.ExpandPhotoPaths(photoPaths))
        {
            if (seen.Add(Path.GetFullPath(file)))
            {
                photos.Add((await scanner.ScanAsync(file, null, filter, ct), file));
            }
        }

        if (photos.Count == 0)
        {
            throw new ArgumentException("No photos to search. Pass --photos and/or --scan.");
        }

        var lines = PartMatcher.Match(parts, photos.Select(p => p.Scan).ToList(), options.Match);
        var images = Render(lines, photos, options, Warn);

        return new FindResult
        {
            Parts = ModelSummary.From(parts),
            Summary = FindSummary.From(lines),
            Images = images,
            Lines = lines,
            Warnings = warnings,
        };

        void Warn(string message)
        {
            warnings.Add(message);
            log?.Invoke("warning: " + message);
        }
    }

    private static List<AnnotatedImage> Render(
        IReadOnlyList<LineResult> lines,
        List<(ScannedPhoto Scan, string? File)> photos,
        FindOptions options,
        Action<string> warn)
    {
        var images = new List<AnnotatedImage>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(options.OutDir);

        foreach (var (photo, file) in photos)
        {
            var annotations = lines
                .SelectMany(l => l.Matches.Where(m => m.Photo == photo.Path).Select(m => new Annotation(l.Number, m.Box, m.Confident)))
                .ToList();
            if (annotations.Count == 0 || file is null)
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(file);
            var name = stem + ".annotated.jpg";
            for (var n = 2; !usedNames.Add(name); n++)
            {
                name = $"{stem}-{n}.annotated.jpg";
            }

            var output = Path.Combine(options.OutDir, name);
            using var original = ImageIo.LoadOriented(file);
            if (original.Width != photo.Width || original.Height != photo.Height)
            {
                warn($"{photo.Path}: size differs from the scan ({photo.Width}x{photo.Height}); circles may be misplaced.");
            }

            using var annotated = AnnotationRenderer.Render(original, annotations);
            File.WriteAllBytes(output, ImageIo.EncodeJpeg(annotated, options.JpegQuality));
            images.Add(new AnnotatedImage(photo.Path, output.Replace('\\', '/'), annotations.Count));
        }

        return images;
    }
}

/// <summary>Builds the "is this piece worth recognising?" test from the colours a parts list needs.</summary>
public static class ColourPrefilter
{
    /// <returns>A predicate, or null when filtering isn't possible because some line has no known colour.</returns>
    public static Func<Rgb, bool>? For(PartsList parts, double maxDistance)
    {
        var targets = new List<(Lab Lab, double Max)>();
        foreach (var line in parts.Lines)
        {
            if (!Rgb.TryParse(line.Colour?.Rgb, out var rgb))
            {
                return null;
            }

            // Transparent pieces pick up the background colour, so give them more room.
            targets.Add((ColourMath.ToLab(rgb), line.Colour!.IsTransparent ? maxDistance * 2 : maxDistance));
        }

        return measured =>
        {
            var lab = ColourMath.ToLab(measured);
            return targets.Any(t => ColourMath.DeltaE(lab, t.Lab) <= t.Max);
        };
    }
}
