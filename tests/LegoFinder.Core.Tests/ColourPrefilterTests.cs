using LegoFinder.Core.Colour;
using LegoFinder.Core.Pipeline;

namespace LegoFinder.Core.Tests;

public class ColourPrefilterTests
{
    [Fact]
    public void Keeps_pieces_near_a_needed_colour_and_skips_the_rest()
    {
        var filter = ColourPrefilter.For(Make.Parts(Make.Line(1, "3001", "C91A09"), Make.Line(2, "3003", "0055BF")), 35)!;

        Assert.True(filter(Rgb.Parse("C3241A")));  // photo red
        Assert.True(filter(Rgb.Parse("1060C0")));  // photo blue
        Assert.False(filter(Rgb.Parse("237841"))); // green isn't needed
        Assert.False(filter(Rgb.Parse("F2CD37"))); // nor yellow
    }

    [Fact]
    public void No_filter_when_any_line_has_no_colour()
    {
        Assert.Null(ColourPrefilter.For(Make.Parts(Make.Line(1, "3001", "C91A09"), Make.Line(2, "3003", rgb: null)), 35));
    }

    [Fact]
    public void Transparent_colours_get_a_wider_margin()
    {
        var parts = Make.Parts(Make.Line(1, "3062b", "C91A09", transparent: true));
        var measured = Rgb.Parse("D87A70"); // trans-red over a light background

        Assert.True(ColourMath.DeltaE(measured, Rgb.Parse("C91A09")) > 35);
        Assert.True(ColourPrefilter.For(parts, 35)!(measured));
    }
}
