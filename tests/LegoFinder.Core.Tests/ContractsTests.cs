using LegoFinder.Core.Contracts;

namespace LegoFinder.Core.Tests;

public class ContractsTests
{
    [Fact]
    public void Parts_list_round_trips_with_schema_and_total()
    {
        var parts = Make.Parts(Make.Line(1, "3001", "C91A09", 2), Make.Line(2, "3003", null, 3));

        var json = LegoJson.Serialize(parts);
        var back = LegoJson.Deserialize<PartsList>(json, Schemas.Parts);

        Assert.Contains("\"schema\": \"legofinder.parts/1\"", json);
        Assert.Contains("\"totalQuantity\": 5", json);
        Assert.Contains("\"kind\": \"File\"", json); // enums as strings
        Assert.Equal(2, back.Lines.Count);
        Assert.Equal("C91A09", back.Lines[0].Colour!.Rgb);
        Assert.Null(back.Lines[1].Colour);
        Assert.Equal(5, back.TotalQuantity);
    }

    [Fact]
    public void Nulls_are_omitted_and_non_ascii_is_kept_readable()
    {
        var parts = Make.Parts(Make.Line(1, "3001", null) with { PartName = "Brick 2 × 4" });

        var json = LegoJson.Serialize(parts);

        Assert.Contains("Brick 2 × 4", json);
        Assert.DoesNotContain("\"colour\"", json);
        Assert.DoesNotContain("\"designer\"", json);
    }

    [Fact]
    public void Wrong_schema_is_rejected()
    {
        var json = LegoJson.Serialize(new ScanResult { Recogniser = "none", Photos = [] });

        var ex = Assert.Throws<InvalidDataException>(() => LegoJson.Deserialize<PartsList>(json, Schemas.Parts));

        Assert.Contains("legofinder.parts/1", ex.Message);
        Assert.Contains("legofinder.scan/1", ex.Message);
    }

    [Theory]
    [InlineData("{}", "(no schema field)")]
    [InlineData("[]", "(no schema field)")]
    [InlineData("{\"schema\": 3}", "(no schema field)")]
    public void Missing_schema_is_rejected(string json, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => LegoJson.Deserialize<PartsList>(json, Schemas.Parts));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Invalid_json_is_reported_as_invalid_data()
    {
        var ex = Assert.Throws<InvalidDataException>(() => LegoJson.Deserialize<PartsList>("{not json", Schemas.Parts));
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void Right_schema_with_wrong_shape_is_reported_as_invalid_data()
    {
        const string json = """{ "schema": "legofinder.parts/1", "lines": "nope" }""";
        var ex = Assert.Throws<InvalidDataException>(() => LegoJson.Deserialize<PartsList>(json, Schemas.Parts));
        Assert.Contains("does not match", ex.Message);
    }

    [Fact]
    public void Hand_written_json_with_comments_and_trailing_commas_is_accepted()
    {
        const string json = """
            {
              // hand-written
              "schema": "legofinder.parts/1",
              "source": "Me", "kind": "File", "id": "X",
              "lines": [ { "number": 1, "partId": "3001", "quantity": 1, }, ],
            }
            """;

        var parts = LegoJson.Deserialize<PartsList>(json, Schemas.Parts);

        Assert.Equal("3001", Assert.Single(parts.Lines).PartId);
    }

    [Fact]
    public void Summary_counts_statuses_and_pieces()
    {
        LineResult L(LineStatus s, int qty, int found) => new()
        {
            Number = 1, PartId = "p", Quantity = qty, Found = found, Status = s, Matches = [],
        };

        var summary = FindSummary.From([
            L(LineStatus.Found, 2, 2), L(LineStatus.PartlyFound, 3, 1), L(LineStatus.Uncertain, 1, 0), L(LineStatus.NotFound, 4, 0),
        ]);

        Assert.Equal(new FindSummary(4, 1, 1, 1, 1, 10, 3), summary);
    }

    [Fact]
    public void Box_centre()
    {
        var box = new Box(0.2, 0.4, 0.2, 0.1);
        Assert.Equal(0.3, box.CentreX, 9);
        Assert.Equal(0.45, box.CentreY, 9);
    }
}
