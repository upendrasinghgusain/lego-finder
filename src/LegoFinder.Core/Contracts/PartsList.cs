namespace LegoFinder.Core.Contracts;

/// <summary>Output of <c>legofinder parts</c>: a model's parts list, numbered in source order.</summary>
public sealed record PartsList
{
    public string Schema { get; init; } = Schemas.Parts;
    public required string Source { get; init; }
    public required ModelKind Kind { get; init; }
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Designer { get; init; }
    public string? SourceUrl { get; init; }
    public int TotalQuantity => Lines.Sum(l => l.Quantity);
    public required IReadOnlyList<PartLine> Lines { get; init; }
}

public enum ModelKind
{
    Moc,
    Set,
    File,
}

public sealed record PartLine
{
    /// <summary>1-based line number; this is the number drawn in the circles.</summary>
    public required int Number { get; init; }
    public required string PartId { get; init; }
    public string? PartName { get; init; }
    public PartColour? Colour { get; init; }
    public required int Quantity { get; init; }
    public string? ImageUrl { get; init; }
    public string? CatalogueUrl { get; init; }

    /// <summary>Ids of the same part in other catalogues, keyed by catalogue name (e.g. "BrickLink").</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ExternalIds { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}

public sealed record PartColour
{
    /// <summary>Rebrickable colour id.</summary>
    public int? Id { get; init; }
    public string? Name { get; init; }

    /// <summary>Hex RGB without '#', e.g. "C91A09".</summary>
    public string? Rgb { get; init; }
    public bool IsTransparent { get; init; }
    public IReadOnlyList<int> BrickLinkIds { get; init; } = [];
}
