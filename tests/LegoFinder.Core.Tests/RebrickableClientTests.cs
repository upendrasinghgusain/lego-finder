using System.Net;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Parts;

namespace LegoFinder.Core.Tests;

public class RebrickableClientTests
{
    private const string Item3001 = """
        {
          "id": 1, "inv_part_id": 1, "set_num": "MOC-37396", "quantity": 2, "is_spare": false,
          "part": {
            "part_num": "3001", "name": "Brick 2 x 4",
            "part_url": "https://rebrickable.com/parts/3001/",
            "part_img_url": "https://cdn.rebrickable.com/media/parts/elements/300121.jpg",
            "external_ids": { "BrickLink": ["3001"], "LEGO": ["3001", "65154"] }
          },
          "color": {
            "id": 4, "name": "Red", "rgb": "C91A09", "is_trans": false,
            "external_ids": { "BrickLink": { "ext_ids": [5], "ext_descrs": [["Red"]] } }
          }
        }
        """;

    private const string SpareItem = """
        { "quantity": 1, "is_spare": true, "part": { "part_num": "3069b", "name": "Tile 1 x 2" },
          "color": { "id": 0, "name": "Black", "rgb": "05131D", "is_trans": false } }
        """;

    private const string TransItem = """
        { "quantity": 1, "is_spare": false, "part": { "part_num": "3062b", "name": "Round 1 x 1" },
          "color": { "id": 36, "name": "Trans-Red", "rgb": "C91A09", "is_trans": true } }
        """;

    private static string Page(string? next, params string[] items) =>
        $$"""{ "count": {{items.Length}}, "next": {{(next is null ? "null" : $"\"{next}\"")}}, "previous": null, "results": [{{string.Join(",", items)}}] }""";

    [Fact]
    public async Task Fetches_moc_parts_across_pages_and_skips_spares()
    {
        var http = new FakeHttpHandler()
            .Reply(HttpStatusCode.OK, Page("https://rebrickable.com/api/v3/lego/mocs/MOC-37396/parts/?page=2&page_size=1000", Item3001, SpareItem))
            .Reply(HttpStatusCode.OK, Page(null, TransItem));
        var client = new RebrickableClient(http.Client(), "secret");
        var source = PartsSource.Parse("https://rebrickable.com/mocs/MOC-37396/Schwimpy/31058-velociraptooth/");

        var parts = await client.GetPartsAsync(source, includeSpares: false);

        Assert.Equal(("Rebrickable", ModelKind.Moc, "MOC-37396"), (parts.Source, parts.Kind, parts.Id));
        Assert.Equal(("31058 Velociraptooth", "Schwimpy"), (parts.Name, parts.Designer));
        Assert.Equal([1, 2], parts.Lines.Select(l => l.Number));

        var brick = parts.Lines[0];
        Assert.Equal(("3001", "Brick 2 x 4", 2), (brick.PartId, brick.PartName, brick.Quantity));
        Assert.Equal(["3001"], brick.ExternalIds["BrickLink"]);
        Assert.Equal(["3001", "65154"], brick.ExternalIds["LEGO"]);
        Assert.Equal([5], brick.Colour!.BrickLinkIds);
        Assert.Equal((4, "Red", "C91A09", false), (brick.Colour.Id, brick.Colour.Name, brick.Colour.Rgb, brick.Colour.IsTransparent));
        Assert.Equal("https://cdn.rebrickable.com/media/parts/elements/300121.jpg", brick.ImageUrl);
        Assert.True(parts.Lines[1].Colour!.IsTransparent);

        Assert.Equal(2, http.Requests.Count);
        var first = http.Requests[0].Request;
        Assert.Equal("https://rebrickable.com/api/v3/lego/mocs/MOC-37396/parts/?page_size=1000", first.RequestUri!.ToString());
        Assert.Equal("key secret", first.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Includes_spares_when_asked()
    {
        var http = new FakeHttpHandler().Reply(HttpStatusCode.OK, Page(null, Item3001, SpareItem));

        var parts = await new RebrickableClient(http.Client(), "k").GetPartsAsync(PartsSource.Parse("MOC-1"), includeSpares: true);

        Assert.Equal(2, parts.Lines.Count);
        Assert.Equal("https://rebrickable.com/mocs/MOC-1/", parts.SourceUrl);
    }

    [Fact]
    public async Task Fetches_set_details_then_parts()
    {
        var http = new FakeHttpHandler()
            .Reply(HttpStatusCode.OK, """{ "set_num": "31058-1", "name": "Mighty Dinosaurs", "set_url": "https://rebrickable.com/sets/31058-1/mighty-dinosaurs/" }""")
            .Reply(HttpStatusCode.OK, Page(null, Item3001));

        var parts = await new RebrickableClient(http.Client(), "k").GetPartsAsync(PartsSource.Parse("31058"), false);

        Assert.Equal((ModelKind.Set, "31058-1", "Mighty Dinosaurs"), (parts.Kind, parts.Id, parts.Name));
        Assert.Equal("https://rebrickable.com/sets/31058-1/mighty-dinosaurs/", parts.SourceUrl);
        Assert.EndsWith("/lego/sets/31058-1/", http.Requests[0].Request.RequestUri!.AbsolutePath);
        Assert.EndsWith("/lego/sets/31058-1/parts/", http.Requests[1].Request.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the API key")]
    [InlineData(HttpStatusCode.Forbidden, "Export the parts as 'Rebrickable CSV'")]
    [InlineData(HttpStatusCode.NotFound, "no inventory")]
    [InlineData(HttpStatusCode.InternalServerError, "returned 500")]
    public async Task Explains_http_errors(HttpStatusCode status, string message)
    {
        var http = new FakeHttpHandler().Reply(status);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new RebrickableClient(http.Client(), "k").GetPartsAsync(PartsSource.Parse("MOC-1"), false));

        Assert.Contains(message, ex.Message);
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task Retries_when_rate_limited()
    {
        var http = new FakeHttpHandler()
            .Reply(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new(TimeSpan.FromMilliseconds(1)))
            .Reply(HttpStatusCode.OK, Page(null, Item3001));

        var parts = await new RebrickableClient(http.Client(), "k").GetPartsAsync(PartsSource.Parse("MOC-1"), false);

        Assert.Single(parts.Lines);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Refuses_to_follow_next_links_to_other_hosts()
    {
        var http = new FakeHttpHandler().Reply(HttpStatusCode.OK, Page("https://evil.example/steal", Item3001));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RebrickableClient(http.Client(), "k").GetPartsAsync(PartsSource.Parse("MOC-1"), false));

        Assert.Contains("non-Rebrickable", ex.Message);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Csv_sources_are_not_api_sources()
    {
        var source = new PartsSource(PartsSourceKind.CsvFile, "x", null, null, null);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RebrickableClient(new FakeHttpHandler().Client(), "k").GetPartsAsync(source, false));
    }

    [Fact]
    public void Parses_minimal_items_and_falls_back_for_missing_fields()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{ "quantity": 3, "part": { "part_num": "99/1" } }""");

        var line = RebrickableClient.ParseLine(doc.RootElement, 7);

        Assert.Equal((7, "99/1", 3), (line.Number, line.PartId, line.Quantity));
        Assert.Null(line.Colour);
        Assert.Equal("https://rebrickable.com/parts/99%2F1/", line.CatalogueUrl);
        Assert.Empty(line.ExternalIds);
    }

    [Fact]
    public void Item_without_part_number_is_invalid()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{ "quantity": 3, "part": { } }""");
        Assert.Throws<InvalidDataException>(() => RebrickableClient.ParseLine(doc.RootElement, 1));
    }
}
