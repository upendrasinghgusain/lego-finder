using System.Globalization;
using System.IO.Compression;
using LegoFinder.Core.Contracts;

namespace LegoFinder.Core.Parts;

/// <summary>
/// Rebrickable's full database export (https://rebrickable.com/downloads/), read straight from
/// <c>all_downloads.zip</c> or a folder of its CSV files. Works offline and needs no API key.
/// It has official set inventories (not MOCs), part names and pictures, and the Rebrickable → BrickLink part and
/// colour mappings that recognition results need (Brickognize answers in BrickLink numbers).
/// </summary>
public sealed class RebrickableCatalogue : IDisposable
{
    private readonly ZipArchive? _zip;
    private readonly string? _directory;
    private readonly Lazy<Dictionary<string, string>> _partNames;
    private readonly Lazy<Dictionary<string, List<string>>> _brickLinkParts;
    private readonly Lazy<Dictionary<int, List<int>>> _brickLinkColours;
    private readonly Lazy<Dictionary<(string Part, int Colour), string>> _elements;
    private readonly ColourTable _colours;

    private RebrickableCatalogue(ZipArchive? zip, string? directory, ColourTable colours)
    {
        _zip = zip;
        _directory = directory;
        _colours = colours;
        _partNames = new(() => Read("parts.csv", ["part_num", "name"]).ToDictionary(r => r[0], r => r[1], StringComparer.OrdinalIgnoreCase));
        _brickLinkParts = new(() => Read("part_mappings.csv", ["part_num", "ext_system", "ext_id"])
            .Where(r => r[1] == "BL")
            .GroupBy(r => r[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(r => r[2]).Distinct().ToList(), StringComparer.OrdinalIgnoreCase));
        _brickLinkColours = new(() => Read("color_mappings.csv", ["id", "ext_system", "ext_id"])
            .Where(r => r[1] == "BL" && Int(r[0]) is not null && Int(r[2]) is not null)
            .GroupBy(r => Int(r[0])!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => Int(r[2])!.Value).Distinct().ToList()));
        _elements = new(() =>
        {
            var map = new Dictionary<(string, int), string>();
            foreach (var r in Read("elements.csv", ["element_id", "part_num", "color_id"]))
            {
                if (Int(r[2]) is { } colour)
                {
                    map.TryAdd((r[1].ToLowerInvariant(), colour), r[0]);
                }
            }

            return map;
        });
    }

    /// <summary>Opens <c>all_downloads.zip</c> (or any zip of the CSVs) or a folder containing them.</summary>
    public static RebrickableCatalogue Open(string path, ColourTable? colours = null)
    {
        colours ??= ColourTable.Rebrickable;
        if (Directory.Exists(path))
        {
            return new RebrickableCatalogue(null, path, colours);
        }

        if (File.Exists(path))
        {
            try
            {
                return new RebrickableCatalogue(ZipFile.OpenRead(path), null, colours);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{path} is not a zip of Rebrickable CSV files: {ex.Message}", ex);
            }
        }

        throw new FileNotFoundException($"Rebrickable catalogue not found: {path}");
    }

    /// <summary>
    /// Where to look for the catalogue when none is given: <c>$LEGOFINDER_CATALOGUE</c>, then
    /// <c>data/rebrickable/all_downloads.zip</c> under the current directory.
    /// </summary>
    public static string? FindDefault(Func<string, string?> getEnvironmentVariable)
    {
        if (getEnvironmentVariable("LEGOFINDER_CATALOGUE") is { Length: > 0 } fromEnv)
        {
            return fromEnv;
        }

        var local = Path.Combine("data", "rebrickable", "all_downloads.zip");
        return File.Exists(local) ? local : null;
    }

    /// <summary>The parts list of an official set (latest inventory version), without minifigs.</summary>
    public PartsList GetSet(string setNum, bool includeSpares)
    {
        var set = Read("sets.csv", ["set_num", "name"]).FirstOrDefault(r => r[0].Equals(setNum, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Set {setNum} is not in the Rebrickable catalogue.");

        var inventoryId = Read("inventories.csv", ["id", "version", "set_num"])
            .Where(r => r[2].Equals(setNum, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => Int(r[1]) ?? 0)
            .Select(r => r[0])
            .FirstOrDefault()
            ?? throw new ArgumentException($"Set {setNum} has no inventory in the Rebrickable catalogue.");

        var lines = new List<(string Part, int Colour, int Qty, string? Image)>();
        foreach (var r in Read("inventory_parts.csv", ["inventory_id", "part_num", "color_id", "quantity", "is_spare", "img_url"]))
        {
            if (r[0] != inventoryId || (!includeSpares && r[4].Equals("True", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var colour = Int(r[2]) ?? -1;
            var qty = Int(r[3]) ?? 0;
            var existing = lines.FindIndex(l => l.Part == r[1] && l.Colour == colour);
            if (existing >= 0)
            {
                lines[existing] = lines[existing] with { Qty = lines[existing].Qty + qty };
            }
            else
            {
                lines.Add((r[1], colour, qty, r[5].Length > 0 ? r[5] : null));
            }
        }

        var parts = new PartsList
        {
            Source = "RebrickableCatalogue",
            Kind = ModelKind.Set,
            Id = set[0],
            Name = set[1],
            SourceUrl = $"https://rebrickable.com/sets/{Uri.EscapeDataString(set[0])}/",
            Lines = lines.Select((l, i) => new PartLine
            {
                Number = i + 1,
                PartId = l.Part,
                Quantity = l.Qty,
                Colour = _colours.Find(l.Colour) ?? new PartColour { Id = l.Colour },
                ImageUrl = l.Image,
                CatalogueUrl = $"https://rebrickable.com/parts/{Uri.EscapeDataString(l.Part)}/",
            }).ToList(),
        };
        return Enrich(parts);
    }

    /// <summary>
    /// Fills in what a parts list is missing (names, BrickLink part and colour ids, colour details, pictures)
    /// without changing anything it already has.
    /// </summary>
    public PartsList Enrich(PartsList parts) => parts with
    {
        Lines = parts.Lines.Select(line =>
        {
            var key = line.PartId.ToLowerInvariant();
            var externalIds = new Dictionary<string, IReadOnlyList<string>>(line.ExternalIds);
            if (!externalIds.ContainsKey("BrickLink") && _brickLinkParts.Value.TryGetValue(line.PartId, out var bl))
            {
                externalIds["BrickLink"] = bl;
            }

            var colour = line.Colour;
            if (colour?.Id is { } colourId)
            {
                var known = _colours.Find(colourId);
                colour = colour with
                {
                    Name = colour.Name ?? known?.Name,
                    Rgb = colour.Rgb ?? known?.Rgb,
                    IsTransparent = colour.IsTransparent || known?.IsTransparent == true,
                    BrickLinkIds = colour.BrickLinkIds.Count > 0
                        ? colour.BrickLinkIds
                        : _brickLinkColours.Value.GetValueOrDefault(colourId) ?? [],
                };
            }

            var image = line.ImageUrl;
            if (image is null && colour?.Id is { } cid && _elements.Value.TryGetValue((key, cid), out var element))
            {
                image = $"https://cdn.rebrickable.com/media/parts/elements/{element}.jpg";
            }

            return line with
            {
                PartName = line.PartName ?? _partNames.Value.GetValueOrDefault(line.PartId),
                ExternalIds = externalIds,
                Colour = colour,
                ImageUrl = image,
            };
        }).ToList(),
    };

    public void Dispose() => _zip?.Dispose();

    /// <summary>Streams the named columns of one CSV file, in the order asked for.</summary>
    private IEnumerable<string[]> Read(string file, string[] columns)
    {
        using var reader = Open(file);
        var header = RebrickableCsvImporter.SplitCsv(reader.ReadLine() ?? "");
        var indexes = columns.Select(c => header.IndexOf(c)).ToArray();
        if (indexes.Any(i => i < 0))
        {
            throw new InvalidDataException($"{file} is missing column(s): {string.Join(", ", columns.Where((_, k) => indexes[k] < 0))}");
        }

        var max = indexes.Max();
        while (reader.ReadLine() is { } line)
        {
            // Only quote-aware split when needed; most rows have no quotes and this keeps big files fast.
            var fields = line.Contains('"') ? RebrickableCsvImporter.SplitCsv(line) : [.. line.Split(',')];
            if (fields.Count > max)
            {
                yield return indexes.Select(i => fields[i]).ToArray();
            }
        }
    }

    private StreamReader Open(string file)
    {
        if (_directory is not null)
        {
            var path = Path.Combine(_directory, file);
            return File.Exists(path)
                ? new StreamReader(path)
                : throw new FileNotFoundException($"{file} is missing from the Rebrickable catalogue folder {_directory}.");
        }

        var entry = _zip!.Entries.FirstOrDefault(e => e.Name.Equals(file, StringComparison.OrdinalIgnoreCase)
                || e.Name.Equals(file + ".gz", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"{file} is missing from the Rebrickable catalogue zip.");
        var stream = entry.Open();
        return new StreamReader(entry.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(stream, CompressionMode.Decompress)
            : stream);
    }

    private static int? Int(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
