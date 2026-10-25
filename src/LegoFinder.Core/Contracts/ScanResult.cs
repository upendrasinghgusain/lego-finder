namespace LegoFinder.Core.Contracts;

/// <summary>Output of <c>legofinder scan</c>: the pieces found in each photo.</summary>
public sealed record ScanResult
{
    public string Schema { get; init; } = Schemas.Scan;
    public required string Recogniser { get; init; }
    public required IReadOnlyList<ScannedPhoto> Photos { get; init; }
}

public sealed record ScannedPhoto
{
    /// <summary>Photo path; relative paths are resolved against the scan file's directory.</summary>
    public required string Path { get; init; }
    public required string Sha256 { get; init; }

    /// <summary>Size after EXIF orientation is applied.</summary>
    public required int Width { get; init; }
    public required int Height { get; init; }
    public string? Background { get; init; }
    public required IReadOnlyList<Detection> Detections { get; init; }
}

public sealed record Detection
{
    public required string Id { get; init; }
    public required Box Box { get; init; }
    public double AreaFraction { get; init; }
    public DetectionColour Colour { get; init; } = new();
    public IReadOnlyList<PartCandidate> Candidates { get; init; } = [];
    public RecognitionStatus Recognition { get; init; } = RecognitionStatus.None;
}

/// <summary>Axis-aligned box, normalised to 0–1 of the photo's width and height.</summary>
public sealed record Box(double X, double Y, double W, double H)
{
    public double CentreX => X + W / 2;
    public double CentreY => Y + H / 2;
}

public sealed record DetectionColour
{
    /// <summary>Hex RGB measured from the photo's pixels (white-balanced when possible).</summary>
    public string? Measured { get; init; }

    /// <summary>Colours suggested by the recogniser, best first.</summary>
    public IReadOnlyList<ColourCandidate> Predicted { get; init; } = [];
}

public sealed record ColourCandidate(string Id, string? Name, string System, double Score);

public sealed record PartCandidate(string PartId, string? Name, string System, double Score);

public enum RecognitionStatus
{
    /// <summary>No recogniser was run (e.g. <c>--recogniser none</c>).</summary>
    None,
    Ok,
    Cached,

    /// <summary>Not sent to the recogniser because its colour matched nothing needed.</summary>
    Skipped,
    Failed,
}
