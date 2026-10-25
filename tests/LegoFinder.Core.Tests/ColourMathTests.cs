using LegoFinder.Core.Colour;

namespace LegoFinder.Core.Tests;

public class ColourMathTests
{
    [Theory]
    [InlineData("C91A09", 0xC9, 0x1A, 0x09)]
    [InlineData("#0055bf", 0x00, 0x55, 0xBF)]
    [InlineData(" FFFFFF ", 255, 255, 255)]
    public void Parses_hex(string hex, int r, int g, int b)
    {
        Assert.True(Rgb.TryParse(hex, out var rgb));
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), rgb);
        Assert.Equal(rgb, Rgb.Parse(hex));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("GGGGGG")]
    [InlineData("1234567")]
    public void Rejects_bad_hex(string? hex)
    {
        Assert.False(Rgb.TryParse(hex, out _));
    }

    [Fact]
    public void Parse_throws_on_bad_hex() => Assert.Throws<FormatException>(() => Rgb.Parse("nope"));

    [Fact]
    public void Formats_hex_upper_case() => Assert.Equal("0A0B0C", new Rgb(10, 11, 12).ToHex());

    [Fact]
    public void Lab_of_white_black_and_red_match_reference_values()
    {
        var white = ColourMath.ToLab(new Rgb(255, 255, 255));
        Assert.Equal(100, white.L, 1);
        Assert.Equal(0, white.A, 1);
        Assert.Equal(0, white.B, 1);

        var black = ColourMath.ToLab(new Rgb(0, 0, 0));
        Assert.Equal(0, black.L, 3);

        // sRGB pure red is L*53.24 a*80.09 b*67.20.
        var red = ColourMath.ToLab(new Rgb(255, 0, 0));
        Assert.Equal(53.24, red.L, 1);
        Assert.Equal(80.09, red.A, 1);
        Assert.Equal(67.20, red.B, 1);
    }

    [Fact]
    public void DeltaE_is_zero_for_same_colour_and_symmetric()
    {
        var a = new Rgb(201, 26, 9);
        var b = new Rgb(0, 85, 191);
        Assert.Equal(0, ColourMath.DeltaE(a, a), 9);
        Assert.Equal(ColourMath.DeltaE(a, b), ColourMath.DeltaE(b, a), 9);
        Assert.True(ColourMath.DeltaE(a, b) > 50);
    }

    [Fact]
    public void Similar_reds_are_close_and_red_vs_orange_is_further()
    {
        var red = Rgb.Parse("C91A09");
        var photoRed = Rgb.Parse("C3241A");
        var orange = Rgb.Parse("FE8A18");
        Assert.True(ColourMath.DeltaE(red, photoRed) < 10);
        Assert.True(ColourMath.DeltaE(red, orange) > 25);
    }

    [Fact]
    public void Chroma_is_low_for_greys_and_high_for_saturated_colours()
    {
        Assert.True(ColourMath.Chroma(ColourMath.ToLab(Rgb.Parse("A0A0A0"))) < 1);
        Assert.True(ColourMath.Chroma(ColourMath.ToLab(Rgb.Parse("C91A09"))) > 50);
    }
}
