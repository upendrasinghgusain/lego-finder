using System.Net;
using System.Text.Json;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Scanning;

namespace LegoFinder.Core.Tests;

public class RecognitionTests
{
    // Trimmed from a real response for a photo of a red 2×4 brick (2026-10-25).
    private const string BrickognizeResponse = """
        {
          "listing_id": "res-9b18031d95e242bb",
          "bounding_box": { "left": 9.1, "upper": 39.9, "right": 182.9, "lower": 151.4, "image_width": 192.0, "image_height": 192.0, "score": 0.979 },
          "items": [
            { "id": "3001", "name": "Brick 2 x 4", "img_url": "https://x/3001.webp", "external_sites": [], "category": "Brick", "type": "part", "score": 0.6523 },
            { "id": "3001special", "name": "Brick 2 x 4 special", "type": "part", "score": 0.5441 },
            { "id": "6001", "name": "A set", "type": "set", "score": 0.5 }
          ],
          "colors": [ { "id": "5", "name": "Red", "score": 0.7319 } ]
        }
        """;

    [Fact]
    public void Parses_brickognize_parts_and_colours()
    {
        using var doc = JsonDocument.Parse(BrickognizeResponse);

        var r = BrickognizeRecogniser.Parse(doc.RootElement);

        Assert.Equal(
            [new PartCandidate("3001", "Brick 2 x 4", "BrickLink", 0.6523), new PartCandidate("3001special", "Brick 2 x 4 special", "BrickLink", 0.5441)],
            r.Candidates);
        Assert.Equal([new ColourCandidate("5", "Red", "BrickLink", 0.7319)], r.Colours);
    }

    [Fact]
    public void Parses_empty_responses()
    {
        using var doc = JsonDocument.Parse("""{ "listing_id": "x", "items": [] }""");

        var r = BrickognizeRecogniser.Parse(doc.RootElement);

        Assert.Empty(r.Candidates);
        Assert.Empty(r.Colours);
    }

    [Fact]
    public async Task Posts_the_crop_as_query_image_with_colour_prediction()
    {
        var http = new FakeHttpHandler().Reply(HttpStatusCode.OK, BrickognizeResponse);
        var recogniser = new BrickognizeRecogniser(http.Client(), topK: 7);

        var r = await recogniser.RecogniseAsync([0xFF, 0xD8, 0xFF]);

        Assert.Equal("brickognize", recogniser.Name);
        Assert.Equal("3001", r.Candidates[0].PartId);
        var (request, body) = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.brickognize.com/predict/parts/?predict_color=true&top_k_items=7&top_k_colors=3&min_similarity_items=0.3", request.RequestUri!.ToString());
        Assert.Contains("name=query_image", body);
        Assert.Contains("image/jpeg", body);
    }

    [Fact]
    public async Task Retries_transient_failures()
    {
        var http = new FakeHttpHandler()
            .Reply(HttpStatusCode.ServiceUnavailable, configure: r => r.Headers.RetryAfter = new(TimeSpan.FromMilliseconds(1)))
            .Reply(HttpStatusCode.OK, BrickognizeResponse);

        var r = await new BrickognizeRecogniser(http.Client()).RecogniseAsync([1]);

        Assert.Equal(2, r.Candidates.Count);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Gives_up_on_client_errors()
    {
        var http = new FakeHttpHandler().Reply(HttpStatusCode.UnprocessableEntity);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new BrickognizeRecogniser(http.Client()).RecogniseAsync([1]));

        Assert.Contains("422", ex.Message);
        Assert.Single(http.Requests);
    }

    [Fact]
    public void Cache_round_trips_and_misses()
    {
        using var dir = new TempDir();
        var cache = new RecognitionCache(dir.Path);
        var key = RecognitionCache.Key("brickognize", [1, 2, 3, 4], 1, 1);
        var value = new Recognition([new PartCandidate("3001", "Brick", "BrickLink", 0.9)], [new ColourCandidate("5", "Red", "BrickLink", 0.7)]);

        Assert.Null(cache.TryGet(key));
        cache.Put(key, value);
        var back = cache.TryGet(key)!;

        Assert.Equal(value.Candidates, back.Candidates);
        Assert.Equal(value.Colours, back.Colours);
        Assert.Equal(dir.Path, cache.Directory);
    }

    [Fact]
    public void Corrupt_cache_entries_are_misses()
    {
        using var dir = new TempDir();
        var cache = new RecognitionCache(dir.Path);
        var key = RecognitionCache.Key("r", [1], 1, 1);
        cache.Put(key, new Recognition([], []));
        File.WriteAllText(Directory.GetFiles(dir.Path, "*.json", SearchOption.AllDirectories).Single(), "{ broken");

        Assert.Null(cache.TryGet(key));
    }

    [Fact]
    public void Cache_keys_depend_on_recogniser_size_and_pixels()
    {
        var a = RecognitionCache.Key("brickognize", [1, 2, 3, 4], 1, 1);

        Assert.Equal(a, RecognitionCache.Key("brickognize", [1, 2, 3, 4], 1, 1));
        Assert.NotEqual(a, RecognitionCache.Key("other", [1, 2, 3, 4], 1, 1));
        Assert.NotEqual(a, RecognitionCache.Key("brickognize", [1, 2, 3, 5], 1, 1));
        Assert.NotEqual(a, RecognitionCache.Key("brickognize", [1, 2, 3, 4], 2, 1));
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Default_cache_directory_is_under_local_app_data()
    {
        Assert.EndsWith(Path.Combine("legofinder", "cache"), RecognitionCache.DefaultDirectory);
    }
}
