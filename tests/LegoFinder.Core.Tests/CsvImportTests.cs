using LegoFinder.Core.Contracts;
using LegoFinder.Core.Parts;

namespace LegoFinder.Core.Tests;

public class CsvImportTests
{
    private static PartsList Import(string csv, bool includeSpares = false) =>
        RebrickableCsvImporter.Import(new StringReader(csv), "model", includeSpares);

    [Fact]
    public void Imports_rows_in_order_with_colours_from_the_table()
    {
        var parts = Import("""
            Part,Color,Quantity
            3001,4,2
            3003,1,1
            """);

        Assert.Equal("RebrickableCsv", parts.Source);
        Assert.Equal(ModelKind.File, parts.Kind);
        Assert.Equal("model", parts.Id);
        Assert.Collection(parts.Lines,
            l =>
            {
                Assert.Equal((1, "3001", 2), (l.Number, l.PartId, l.Quantity));
                Assert.Equal(("Red", "C91A09"), (l.Colour!.Name, l.Colour.Rgb));
                Assert.Equal("https://rebrickable.com/parts/3001/", l.CatalogueUrl);
            },
            l =>
            {
                Assert.Equal((2, "3003", 1), (l.Number, l.PartId, l.Quantity));
                Assert.Equal("Blue", l.Colour!.Name);
            });
        Assert.Equal(3, parts.TotalQuantity);
    }

    [Fact]
    public void Merges_repeated_part_and_colour()
    {
        var parts = Import("Part,Color,Quantity\n3001,4,2\n3003,4,1\n3001,4,3\n");

        Assert.Equal(2, parts.Lines.Count);
        Assert.Equal(5, parts.Lines[0].Quantity);
    }

    [Fact]
    public void Skips_spares_unless_asked()
    {
        const string csv = "Part,Color,Quantity,Is Spare\n3001,4,2,False\n3001,4,1,True\n3069b,0,1,True\n";

        Assert.Equal(2, Import(csv).TotalQuantity);
        Assert.Equal(4, Import(csv, includeSpares: true).TotalQuantity);
    }

    [Fact]
    public void Accepts_header_variants_quoted_fields_and_blank_lines()
    {
        var parts = Import("\"PART\",\"Colour\",\"Qty\"\n\"3001\",\"4\",\"1\"\n\n\"98138pr0001\",\"15\",\"2\"\n");

        Assert.Equal(["3001", "98138pr0001"], parts.Lines.Select(l => l.PartId));
        Assert.Equal("White", parts.Lines[1].Colour!.Name);
    }

    [Fact]
    public void Unknown_colour_id_keeps_the_id_without_rgb()
    {
        var line = Assert.Single(Import("Part,Color,Quantity\n3001,999999,1\n").Lines);

        Assert.Equal(999999, line.Colour!.Id);
        Assert.Null(line.Colour.Rgb);
    }

    [Fact]
    public void Non_numeric_colour_means_no_colour()
    {
        Assert.Null(Assert.Single(Import("Part,Color,Quantity\n3001,,1\n").Lines).Colour);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("Part,Quantity\n3001,1\n", "'color'")]
    [InlineData("Color,Quantity\n4,1\n", "'part'")]
    [InlineData("Part,Color\n3001,4\n", "'quantity'")]
    [InlineData("Part,Color,Quantity\n3001,4,lots\n", "bad quantity")]
    [InlineData("Part,Color,Quantity\n3001,4,0\n", "bad quantity")]
    [InlineData("Part,Color,Quantity\n3001,4\n", "too few columns")]
    public void Rejects_malformed_csv(string csv, string message)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Import(csv));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Splits_escaped_quotes()
    {
        Assert.Equal(["a", "b \"c\", d", ""], RebrickableCsvImporter.SplitCsv("a,\"b \"\"c\"\", d\","));
    }

    [Fact]
    public void Embedded_colour_table_loads()
    {
        var table = ColourTable.Rebrickable;

        Assert.True(table.Count > 200);
        Assert.Equal("Black", table.Find(0)!.Name);
        Assert.True(table.Find(47)!.IsTransparent); // Trans-Clear
        Assert.Null(table.Find(123456));
    }

    [Fact]
    public void Colour_table_rejects_unexpected_header()
    {
        Assert.Throws<InvalidDataException>(() => ColourTable.Parse(new StringReader("a,b,c\n")));
    }

    [Fact]
    public void Colour_table_skips_malformed_rows()
    {
        var table = ColourTable.Parse(new StringReader("id,name,rgb,is_trans\nx,Bad,000000,False\n1,Blue,0055BF,False\nshort\n"));
        Assert.Equal(1, table.Count);
    }
}
