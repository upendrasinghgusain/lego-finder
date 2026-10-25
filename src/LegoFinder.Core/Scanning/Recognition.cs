using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using LegoFinder.Core.Contracts;

namespace LegoFinder.Core.Scanning;

/// <summary>Identifies the single LEGO part shown in a cropped image.</summary>
public interface IPartRecogniser
{
    /// <summary>Short name recorded in scan output and used to separate cache entries.</summary>
    string Name { get; }

    Task<Recognition> RecogniseAsync(byte[] jpeg, CancellationToken ct = default);
}

public sealed record Recognition(IReadOnlyList<PartCandidate> Candidates, IReadOnlyList<ColourCandidate> Colours);

/// <summary>
/// Brickognize (https://brickognize.com) part recognition. Returns BrickLink part and colour ids. No API key needed.
/// </summary>
public sealed class BrickognizeRecogniser(HttpClient http, int topK = 5) : IPartRecogniser
{
    public static readonly Uri Endpoint = new("https://api.brickognize.com/predict/parts/");

    public string Name => "brickognize";

    public async Task<Recognition> RecogniseAsync(byte[] jpeg, CancellationToken ct = default)
    {
        // Ask for weaker candidates than the API default (0.5) so they can become dashed "maybe" circles.
        var uri = new Uri(Endpoint, $"?predict_color=true&top_k_items={topK}&top_k_colors=3&min_similarity_items=0.3");
        for (var attempt = 1; ; attempt++)
        {
            using var form = new MultipartFormDataContent();
            var image = new ByteArrayContent(jpeg);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(image, "query_image", "piece.jpg");

            using var response = await http.PostAsync(uri, form, ct);
            if (IsTransient(response.StatusCode) && attempt < 3)
            {
                await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt), ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Brickognize returned {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return Parse(doc.RootElement);
        }
    }

    internal static Recognition Parse(JsonElement root)
    {
        var candidates = new List<PartCandidate>();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) && type.GetString() is { } t && t != "part")
                {
                    continue;
                }

                if (item.TryGetProperty("id", out var id) && id.GetString() is { } partId)
                {
                    candidates.Add(new PartCandidate(partId, Str(item, "name"), "BrickLink", Num(item, "score")));
                }
            }
        }

        var colours = new List<ColourCandidate>();
        if (root.TryGetProperty("colors", out var cols) && cols.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cols.EnumerateArray())
            {
                if (c.TryGetProperty("id", out var id) && id.ToString() is { Length: > 0 } colourId)
                {
                    colours.Add(new ColourCandidate(colourId, Str(c, "name"), "BrickLink", Num(c, "score")));
                }
            }
        }

        return new Recognition(candidates, colours);
    }

    private static bool IsTransient(HttpStatusCode code) => code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}

/// <summary>
/// On-disk cache of recognition results keyed by a hash of the crop's pixels, so an unchanged photo
/// never hits the recogniser twice while a changed one is always recognised afresh.
/// </summary>
public sealed class RecognitionCache(string directory)
{
    public string Directory { get; } = directory;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "legofinder", "cache");

    public static string Key(string recogniser, ReadOnlySpan<byte> pixels, int width, int height)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(System.Text.Encoding.UTF8.GetBytes($"{recogniser}|{width}x{height}|"));
        sha.AppendData(pixels);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    public Recognition? TryGet(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Recognition>(File.ReadAllText(path), LegoJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null; // A corrupt entry is just a cache miss.
        }
    }

    public void Put(string key, Recognition value)
    {
        var path = PathFor(key);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, LegoJson.Options));
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string key) => Path.Combine(Directory, key[..2], key + ".json");
}
