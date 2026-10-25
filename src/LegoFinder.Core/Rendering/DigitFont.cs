using SkiaSharp;

namespace LegoFinder.Core.Rendering;

/// <summary>
/// A 5×7 bitmap font for the digits 0–9. Built in so annotations look identical everywhere and never depend on
/// installed fonts; chunky digits are also easy for a child to read.
/// </summary>
public static class DigitFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    private static readonly string[][] Glyphs =
    [
        [" ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### "], // 0
        ["  #  ", " ##  ", "# #  ", "  #  ", "  #  ", "  #  ", "#####"], // 1
        [" ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####"], // 2
        [" ### ", "#   #", "    #", "  ## ", "    #", "#   #", " ### "], // 3
        ["   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # "], // 4
        ["#####", "#    ", "#### ", "    #", "    #", "#   #", " ### "], // 5
        [" ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### "], // 6
        ["#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   "], // 7
        [" ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### "], // 8
        [" ### ", "#   #", "#   #", " ####", "    #", "    #", " ### "], // 9
    ];

    /// <summary>Whether pixel (x, y) of a digit's glyph is set.</summary>
    public static bool IsSet(char digit, int x, int y) =>
        digit is >= '0' and <= '9' && Glyphs[digit - '0'][y][x] == '#';

    /// <summary>Width of <paramref name="text"/> in glyph cells, including one blank cell between digits.</summary>
    public static int CellWidth(string text) => text.Length == 0 ? 0 : text.Length * (GlyphWidth + 1) - 1;

    /// <summary>Draws digits centred on (<paramref name="cx"/>, <paramref name="cy"/>) with square cells of size <paramref name="cell"/>.</summary>
    public static void Draw(SKCanvas canvas, string text, float cx, float cy, float cell, SKColor colour)
    {
        if (text.Any(c => c is < '0' or > '9'))
        {
            throw new ArgumentException("Only digits can be drawn.", nameof(text));
        }

        var left = cx - CellWidth(text) * cell / 2;
        var top = cy - GlyphHeight * cell / 2;
        using var paint = new SKPaint { Color = colour, IsAntialias = true, Style = SKPaintStyle.Fill };
        for (var i = 0; i < text.Length; i++)
        {
            var glyphLeft = left + i * (GlyphWidth + 1) * cell;
            for (var y = 0; y < GlyphHeight; y++)
            {
                for (var x = 0; x < GlyphWidth; x++)
                {
                    if (IsSet(text[i], x, y))
                    {
                        // Slight overlap avoids hairline gaps between cells when anti-aliased.
                        canvas.DrawRect(glyphLeft + x * cell - 0.25f, top + y * cell - 0.25f, cell + 0.5f, cell + 0.5f, paint);
                    }
                }
            }
        }
    }
}
