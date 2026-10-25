using LegoFinder.Core.Colour;
using LegoFinder.Core.Scanning;
using SkiaSharp;

namespace LegoFinder.Core.Tests;

public class SegmenterTests
{
    [Fact]
    public void Finds_each_piece_with_tight_boxes_and_colours_in_reading_order()
    {
        using var tray = TestImages.Tray(400, 300,
            (new SKRect(250, 20, 330, 60), "0055BF"),  // top right, blue
            (new SKRect(20, 30, 100, 70), "C91A09"),   // top left, red
            (new SKRect(100, 200, 140, 280), "237841")); // bottom, green

        var result = PieceSegmenter.Segment(tray);

        Assert.Equal("EEEEEA", result.Background.ToHex());
        Assert.Equal(3, result.Pieces.Count);

        var red = result.Pieces[0];
        Assert.Equal(20 / 400.0, red.Box.X, 3);
        Assert.Equal(30 / 300.0, red.Box.Y, 3);
        Assert.Equal(80 / 400.0, red.Box.W, 3);
        Assert.Equal(40 / 300.0, red.Box.H, 3);
        Assert.Equal(80 * 40 / (400.0 * 300), red.AreaFraction, 4);
        Assert.True(ColourMath.DeltaE(red.MeasuredColour, Rgb.Parse("C91A09")) < 3);

        Assert.True(ColourMath.DeltaE(result.Pieces[1].MeasuredColour, Rgb.Parse("0055BF")) < 3);
        Assert.True(ColourMath.DeltaE(result.Pieces[2].MeasuredColour, Rgb.Parse("237841")) < 3);
    }

    [Fact]
    public void Drops_specks_and_huge_regions()
    {
        using var tray = TestImages.Tray(400, 300,
            (new SKRect(10, 10, 12, 12), "000000"),     // 4 px speck
            (new SKRect(100, 100, 140, 140), "C91A09"));

        var result = PieceSegmenter.Segment(tray);

        Assert.Single(result.Pieces);
    }

    [Fact]
    public void Ignores_regions_larger_than_the_maximum()
    {
        using var tray = TestImages.Tray(200, 200, (new SKRect(20, 20, 180, 180), "C91A09"));

        Assert.Empty(PieceSegmenter.Segment(tray).Pieces);
        Assert.Single(PieceSegmenter.Segment(tray, new SegmenterOptions { MaxAreaFraction = 0.9 }).Pieces);
    }

    [Fact]
    public void Joins_a_piece_split_by_a_one_pixel_gap()
    {
        using var tray = TestImages.Tray(200, 200,
            (new SKRect(50, 50, 80, 90), "C91A09"),
            (new SKRect(81, 50, 110, 90), "C91A09"));

        Assert.Single(PieceSegmenter.Segment(tray).Pieces);
        Assert.Equal(2, PieceSegmenter.Segment(tray, new SegmenterOptions { DilateRadius = 0 }).Pieces.Count);
    }

    [Fact]
    public void Light_grey_on_white_needs_a_lower_threshold()
    {
        using var tray = TestImages.Tray(200, 200, SKColors.White, (new SKRect(50, 50, 100, 100), "E4E4E4"));

        Assert.Empty(PieceSegmenter.Segment(tray).Pieces);
        Assert.Single(PieceSegmenter.Segment(tray, new SegmenterOptions { ForegroundThreshold = 5 }).Pieces);
    }

    [Fact]
    public void Large_photos_are_analysed_downscaled_with_the_same_normalised_boxes()
    {
        using var tray = TestImages.Tray(3200, 2400, (new SKRect(800, 600, 1200, 1000), "C91A09"));

        var piece = Assert.Single(PieceSegmenter.Segment(tray).Pieces);

        Assert.Equal(0.25, piece.Box.X, 2);
        Assert.Equal(0.25, piece.Box.Y, 2);
        Assert.Equal(0.125, piece.Box.W, 2);
        Assert.Equal(1.0 / 6, piece.Box.H, 2);
    }

    [Fact]
    public void White_balance_corrects_a_tinted_neutral_background()
    {
        // A warm cast: the "grey" background and the blue piece are both shifted towards red.
        var cast = new SKColor(0xF0, 0xE6, 0xDC);
        using var tray = TestImages.Tray(200, 200, cast, (new SKRect(50, 50, 100, 100), "1058B4"));

        var piece = Assert.Single(PieceSegmenter.Segment(tray).Pieces);

        var gains = PieceSegmenter.WhiteBalanceGains(Rgb.Parse("F0E6DC"), ColourMath.ToLab(Rgb.Parse("F0E6DC")), 12);
        Assert.True(gains.R < 1 && gains.B > 1);
        Assert.True(ColourMath.DeltaE(piece.MeasuredColour, Rgb.Parse("0055BF"))
            < ColourMath.DeltaE(Rgb.Parse("1058B4"), Rgb.Parse("0055BF")));
    }

    [Fact]
    public void Coloured_backgrounds_are_not_white_balanced()
    {
        var bg = Rgb.Parse("2060C0");
        Assert.Equal((1.0, 1.0, 1.0), PieceSegmenter.WhiteBalanceGains(bg, ColourMath.ToLab(bg), 12));

        var black = Rgb.Parse("000000");
        Assert.Equal((1.0, 1.0, 1.0), PieceSegmenter.WhiteBalanceGains(black, ColourMath.ToLab(black), 12));
    }

    [Fact]
    public void Background_is_the_median_of_the_border()
    {
        // 10×10 image: border is grey except for one red pixel.
        var pixels = new byte[10 * 10 * 4];
        for (var i = 0; i < 100; i++)
        {
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = 200;
        }

        pixels[0] = 255;
        pixels[1] = pixels[2] = 0;

        Assert.Equal(new Rgb(200, 200, 200), PieceSegmenter.EstimateBackground(pixels, 10, 10, 0.1));
    }

    [Fact]
    public void Dilate_grows_by_radius_in_every_direction()
    {
        var mask = new bool[7 * 7];
        mask[3 * 7 + 3] = true;

        var grown = PieceSegmenter.Dilate(mask, 7, 7, 1);

        for (var y = 0; y < 7; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                Assert.Equal(x is >= 2 and <= 4 && y is >= 2 and <= 4, grown[y * 7 + x]);
            }
        }
    }

    [Fact]
    public void Dilate_handles_edges()
    {
        var mask = new bool[3 * 3];
        mask[0] = true;

        var grown = PieceSegmenter.Dilate(mask, 3, 3, 1);

        Assert.Equal([true, true, false, true, true, false, false, false, false], grown);
    }

    [Fact]
    public void Labels_are_eight_connected()
    {
        // Diagonal pixels join; a separate pixel gets its own label.
        bool[] mask =
        [
            true, false, false, false,
            false, true, false, false,
            false, false, false, true,
        ];

        var (labels, count) = PieceSegmenter.Label(mask, 4, 3);

        Assert.Equal(2, count);
        Assert.Equal(labels[0], labels[5]);
        Assert.NotEqual(labels[0], labels[11]);
        Assert.Equal(-1, labels[1]);
    }

    [Fact]
    public void Demo_tray_segments_into_its_twelve_pieces()
    {
        using var tray = Imaging.ImageIo.LoadOriented(Repo.Demo("tray.png"));
        Assert.Equal(12, PieceSegmenter.Segment(tray).Pieces.Count);
    }
}
