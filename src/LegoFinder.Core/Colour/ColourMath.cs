using System.Globalization;

namespace LegoFinder.Core.Colour;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string ToHex() => $"{R:X2}{G:X2}{B:X2}";

    public static Rgb Parse(string hex) =>
        TryParse(hex, out var rgb) ? rgb : throw new FormatException($"'{hex}' is not a hex RGB colour.");

    public static bool TryParse(string? hex, out Rgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        rgb = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
}

public readonly record struct Lab(double L, double A, double B);

public static class ColourMath
{
    // sRGB → linear lookup; ToLab runs once per pixel during segmentation.
    private static readonly double[] Linear = Enumerable.Range(0, 256).Select(v =>
    {
        var s = v / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }).ToArray();

    /// <summary>sRGB (D65) to CIELAB.</summary>
    public static Lab ToLab(Rgb c) => ToLab(c.R, c.G, c.B);

    public static Lab ToLab(byte red, byte green, byte blue)
    {
        var r = Linear[red];
        var g = Linear[green];
        var b = Linear[blue];

        var x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        var y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        var z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;

        var fx = F(x);
        var fy = F(y);
        var fz = F(z);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));

        static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
    }

    /// <summary>CIE76 colour difference (Euclidean distance in CIELAB).</summary>
    public static double DeltaE(Lab a, Lab b)
    {
        var dl = a.L - b.L;
        var da = a.A - b.A;
        var db = a.B - b.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    public static double DeltaE(Rgb a, Rgb b) => DeltaE(ToLab(a), ToLab(b));

    /// <summary>Chroma (colourfulness) of a colour; near 0 for whites, greys and blacks.</summary>
    public static double Chroma(Lab c) => Math.Sqrt(c.A * c.A + c.B * c.B);
}
