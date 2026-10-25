using System.Globalization;
using System.Text.RegularExpressions;
using LegoFinder.Core.Colour;
using LegoFinder.Core.Contracts;

namespace LegoFinder.Core.Matching;

public sealed record MatchOptions
{
    /// <summary>
    /// When false (the default) any colour of the right part counts; colour only breaks ties between equally
    /// likely pieces. When true a piece must also be the line's colour.
    /// </summary>
    public bool MatchColour { get; init; }

    /// <summary>Only the recogniser's top N part candidates are considered.</summary>
    public int TopCandidates { get; init; } = 3;

    /// <summary>Part score at or above which a match counts as confident (with colour matching on, the colour must be known too).</summary>
    public double ConfidentScore { get; init; } = 0.55;

    /// <summary>Candidates below this score are ignored entirely.</summary>
    public double MinPartScore { get; init; } = 0;

    /// <summary>Largest CIE76 ΔE between measured and catalogue colour that still counts as the same colour.</summary>
    public double MaxColourDistance { get; init; } = 25;

    /// <summary>Recogniser colour predictions below this score are ignored.</summary>
    public double MinColourScore { get; init; } = 0.2;
}

/// <summary>
/// Assigns detected pieces to parts lines: each detection to at most one line, each line at most <c>quantity</c>
/// detections, best evidence first. Never forces a match.
/// </summary>
public static partial class PartMatcher
{
    public static IReadOnlyList<LineResult> Match(PartsList parts, IReadOnlyList<ScannedPhoto> photos, MatchOptions? options = null)
    {
        options ??= new MatchOptions();
        var lineIds = parts.Lines.ToDictionary(l => l.Number, IdsFor);

        var pairs = new List<Pair>();
        foreach (var photo in photos)
        {
            foreach (var detection in photo.Detections)
            {
                var candidates = detection.Candidates
                    .OrderByDescending(c => c.Score)
                    .Take(options.TopCandidates)
                    .Where(c => c.Score >= options.MinPartScore)
                    .ToList();
                if (candidates.Count == 0)
                {
                    continue;
                }

                foreach (var line in parts.Lines)
                {
                    var best = candidates.FirstOrDefault(c => Normalise(c.PartId).Overlaps(lineIds[line.Number]));
                    if (best is null)
                    {
                        continue;
                    }

                    // Even when colour isn't required, a piece in the right colour is preferred over one that isn't.
                    var fit = ColourFit(line.Colour, detection.Colour, options);
                    var rightColour = fit is { Evidence: ColourEvidence.Predicted or ColourEvidence.Measured };
                    if (options.MatchColour && fit is null)
                    {
                        continue;
                    }

                    var (evidence, distance) = options.MatchColour
                        ? fit!.Value
                        : (ColourEvidence.Ignored, MeasuredDistance(line.Colour, detection.Colour));
                    var confident = best.Score >= options.ConfidentScore && evidence != ColourEvidence.Unknown;
                    pairs.Add(new Pair(line, photo, detection, best.Score, evidence, distance, confident, rightColour));
                }
            }
        }

        var assigned = new HashSet<(string Photo, string Detection)>();
        var byLine = parts.Lines.ToDictionary(l => l.Number, _ => new List<Pair>());
        foreach (var pair in pairs
            .OrderByDescending(p => p.Confident)
            .ThenByDescending(p => p.RightColour)
            .ThenByDescending(p => p.PartScore)
            .ThenBy(p => p.ColourDistance ?? double.MaxValue)
            .ThenBy(p => p.Line.Number)
            .ThenBy(p => p.Detection.Id, StringComparer.Ordinal))
        {
            var list = byLine[pair.Line.Number];
            if (list.Count < pair.Line.Quantity && assigned.Add((pair.Photo.Path, pair.Detection.Id)))
            {
                list.Add(pair);
            }
        }

        return parts.Lines.Select(line =>
        {
            var matches = byLine[line.Number];
            var confident = matches.Count(m => m.Confident);
            return new LineResult
            {
                Number = line.Number,
                PartId = line.PartId,
                PartName = line.PartName,
                ColourName = line.Colour?.Name,
                ColourRgb = line.Colour?.Rgb,
                Quantity = line.Quantity,
                Found = confident,
                Status = confident >= line.Quantity ? LineStatus.Found
                    : confident > 0 ? LineStatus.PartlyFound
                    : matches.Count > 0 ? LineStatus.Uncertain
                    : LineStatus.NotFound,
                CatalogueUrl = line.CatalogueUrl,
                ImageUrl = line.ImageUrl,
                Matches = matches
                    .Select(m => new PieceMatch(
                        m.Photo.Path,
                        m.Detection.Id,
                        m.Detection.Box,
                        Math.Round(m.PartScore, 4),
                        m.ColourEvidence,
                        m.ColourDistance is { } d ? Math.Round(d, 2) : null,
                        m.Confident))
                    .ToList(),
            };
        }).ToList();
    }

    internal static (ColourEvidence Evidence, double? Distance)? ColourFit(PartColour? want, DetectionColour have, MatchOptions options)
    {
        var hasRgb = Rgb.TryParse(want?.Rgb, out var wantRgb);
        if (want is null || (!hasRgb && want.BrickLinkIds.Count == 0))
        {
            return (ColourEvidence.Unknown, null);
        }

        var distance = MeasuredDistance(want, have);

        var predicted = have.Predicted.Any(p =>
            p.Score >= options.MinColourScore
            && p.System.Equals("BrickLink", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(p.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && want.BrickLinkIds.Contains(id));
        if (predicted)
        {
            return (ColourEvidence.Predicted, distance);
        }

        return distance <= options.MaxColourDistance ? (ColourEvidence.Measured, distance) : null;
    }

    /// <summary>ΔE between the line's catalogue colour and the measured colour, when both are known.</summary>
    internal static double? MeasuredDistance(PartColour? want, DetectionColour have) =>
        Rgb.TryParse(want?.Rgb, out var wantRgb) && Rgb.TryParse(have.Measured, out var measured)
            ? ColourMath.DeltaE(wantRgb, measured)
            : null;

    /// <summary>All ids a parts line answers to: its own id plus every external id, with mould variants folded.</summary>
    internal static HashSet<string> IdsFor(PartLine line)
    {
        var ids = Normalise(line.PartId);
        foreach (var external in line.ExternalIds.Values.SelectMany(v => v))
        {
            ids.UnionWith(Normalise(external));
        }

        return ids;
    }

    /// <summary>Lower-cases an id and adds its base mould number, so "3069b" also answers to "3069".</summary>
    internal static HashSet<string> Normalise(string id)
    {
        var s = id.Trim().ToLowerInvariant();
        var set = new HashSet<string>(StringComparer.Ordinal) { s };
        if (MouldVariant().Match(s) is { Success: true } m)
        {
            set.Add(m.Groups[1].Value);
        }

        return set;
    }

    [GeneratedRegex("^([0-9]+)[a-z]$")]
    private static partial Regex MouldVariant();

    private sealed record Pair(
        PartLine Line,
        ScannedPhoto Photo,
        Detection Detection,
        double PartScore,
        ColourEvidence ColourEvidence,
        double? ColourDistance,
        bool Confident,
        bool RightColour);
}
