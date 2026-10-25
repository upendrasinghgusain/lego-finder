namespace LegoFinder.Core.Contracts;

/// <summary>Output of <c>legofinder find</c>: which parts were found where.</summary>
public sealed record FindResult
{
    public string Schema { get; init; } = Schemas.Find;
    public required ModelSummary Parts { get; init; }
    public required FindSummary Summary { get; init; }
    public required IReadOnlyList<AnnotatedImage> Images { get; init; }
    public required IReadOnlyList<LineResult> Lines { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ModelSummary(string Source, ModelKind Kind, string Id, string? Name, string? Designer, string? SourceUrl)
{
    public static ModelSummary From(PartsList parts) =>
        new(parts.Source, parts.Kind, parts.Id, parts.Name, parts.Designer, parts.SourceUrl);
}

public sealed record FindSummary(
    int Lines,
    int Found,
    int PartlyFound,
    int Uncertain,
    int NotFound,
    int PiecesNeeded,
    int PiecesFound)
{
    public static FindSummary From(IReadOnlyList<LineResult> lines) => new(
        lines.Count,
        lines.Count(l => l.Status == LineStatus.Found),
        lines.Count(l => l.Status == LineStatus.PartlyFound),
        lines.Count(l => l.Status == LineStatus.Uncertain),
        lines.Count(l => l.Status == LineStatus.NotFound),
        lines.Sum(l => l.Quantity),
        lines.Sum(l => l.Found));
}

public sealed record AnnotatedImage(string Photo, string Annotated, int Circles);

public sealed record LineResult
{
    public required int Number { get; init; }
    public required string PartId { get; init; }
    public string? PartName { get; init; }
    public string? ColourName { get; init; }
    public string? ColourRgb { get; init; }
    public required int Quantity { get; init; }

    /// <summary>Number of confident matches (capped at <see cref="Quantity"/>).</summary>
    public required int Found { get; init; }
    public required LineStatus Status { get; init; }
    public string? CatalogueUrl { get; init; }
    public string? ImageUrl { get; init; }
    public required IReadOnlyList<PieceMatch> Matches { get; init; }
}

public enum LineStatus
{
    Found,
    PartlyFound,
    Uncertain,
    NotFound,
}

public sealed record PieceMatch(
    string Photo,
    string DetectionId,
    Box Box,
    double PartScore,
    ColourEvidence ColourEvidence,
    double? ColourDistance,
    bool Confident);

public enum ColourEvidence
{
    /// <summary>The recogniser predicted this colour.</summary>
    Predicted,

    /// <summary>The measured pixel colour is close to the part's colour.</summary>
    Measured,

    /// <summary>The parts line has no colour information.</summary>
    Unknown,

    /// <summary>Colour matching is off (the default); any colour of the part counts.</summary>
    Ignored,
}
