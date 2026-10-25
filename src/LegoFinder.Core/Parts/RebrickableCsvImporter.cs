using System.Globalization;
using LegoFinder.Core.Contracts;

namespace LegoFinder.Core.Parts;

/// <summary>
/// Reads a Rebrickable CSV parts export: a header row with <c>Part</c>, <c>Color</c>, <c>Quantity</c>
/// and optionally <c>Is Spare</c>. Colours are Rebrickable colour ids.
/// </summary>
public static class RebrickableCsvImporter
{
    public static PartsList Import(TextReader reader, string id, bool includeSpares = false, ColourTable? colours = null)
    {
        colours ??= ColourTable.Rebrickable;
        var header = reader.ReadLine() ?? throw new InvalidDataException("CSV file is empty.");
        var columns = SplitCsv(header).Select(c => c.Trim().ToLowerInvariant()).ToList();

        var partCol = Require(columns, "part");
        var colourCol = Require(columns, "color", "colour");
        var qtyCol = Require(columns, "quantity", "qty");
        var spareCol = columns.FindIndex(c => c is "is spare" or "is_spare" or "spare");

        // Merge repeated part+colour rows, keeping first-seen order.
        var lines = new List<(string Part, int? Colour, int Qty)>();
        var lineNo = 1;
        while (reader.ReadLine() is { } raw)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var f = SplitCsv(raw);
            if (f.Count <= Math.Max(partCol, Math.Max(colourCol, qtyCol)))
            {
                throw new InvalidDataException($"CSV line {lineNo} has too few columns.");
            }

            if (spareCol >= 0 && spareCol < f.Count && IsTrue(f[spareCol]) && !includeSpares)
            {
                continue;
            }

            var part = f[partCol].Trim();
            if (!int.TryParse(f[qtyCol].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            {
                throw new InvalidDataException($"CSV line {lineNo}: bad quantity '{f[qtyCol]}'.");
            }

            int? colour = int.TryParse(f[colourCol].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : null;

            var existing = lines.FindIndex(l => l.Part == part && l.Colour == colour);
            if (existing >= 0)
            {
                lines[existing] = lines[existing] with { Qty = lines[existing].Qty + qty };
            }
            else
            {
                lines.Add((part, colour, qty));
            }
        }

        return new PartsList
        {
            Source = "RebrickableCsv",
            Kind = ModelKind.File,
            Id = id,
            Name = id,
            Lines = lines.Select((l, i) => new PartLine
            {
                Number = i + 1,
                PartId = l.Part,
                Quantity = l.Qty,
                Colour = l.Colour is { } cid ? colours.Find(cid) ?? new PartColour { Id = cid } : null,
                CatalogueUrl = $"https://rebrickable.com/parts/{Uri.EscapeDataString(l.Part)}/",
            }).ToList(),
        };
    }

    private static int Require(List<string> columns, params string[] names)
    {
        var i = columns.FindIndex(names.Contains);
        return i >= 0 ? i : throw new InvalidDataException($"CSV header is missing a '{names[0]}' column.");
    }

    private static bool IsTrue(string s) => s.Trim().ToLowerInvariant() is "true" or "1" or "yes";

    /// <summary>Minimal CSV field splitter supporting double-quoted fields.</summary>
    internal static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
