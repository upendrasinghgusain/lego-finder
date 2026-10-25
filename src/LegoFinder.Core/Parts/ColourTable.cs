using System.Globalization;
using LegoFinder.Core.Contracts;

namespace LegoFinder.Core.Parts;

/// <summary>
/// Rebrickable's colour table (id, name, RGB), embedded from
/// https://cdn.rebrickable.com/media/downloads/colors.csv.gz so CSV imports work offline.
/// </summary>
public sealed class ColourTable
{
    private readonly Dictionary<int, PartColour> _byId;

    private ColourTable(Dictionary<int, PartColour> byId) => _byId = byId;

    public static ColourTable Rebrickable { get; } = LoadEmbedded();

    public int Count => _byId.Count;

    public PartColour? Find(int id) => _byId.GetValueOrDefault(id);

    internal static ColourTable Parse(TextReader reader)
    {
        var byId = new Dictionary<int, PartColour>();
        var header = reader.ReadLine();
        if (header is null || !header.StartsWith("id,name,rgb", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Colour table has an unexpected header.");
        }

        while (reader.ReadLine() is { } line)
        {
            var f = line.Split(',');
            if (f.Length < 4 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            byId[id] = new PartColour
            {
                Id = id,
                Name = f[1],
                Rgb = f[2],
                IsTransparent = f[3].Equals("True", StringComparison.OrdinalIgnoreCase),
            };
        }

        return new ColourTable(byId);
    }

    private static ColourTable LoadEmbedded()
    {
        using var stream = typeof(ColourTable).Assembly.GetManifestResourceStream("LegoFinder.Core.rebrickable-colors.csv")
            ?? throw new InvalidOperationException("Embedded colour table is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader);
    }
}
