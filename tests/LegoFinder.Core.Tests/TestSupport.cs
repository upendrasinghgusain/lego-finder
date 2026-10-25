using System.Net;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Imaging;
using SkiaSharp;

namespace LegoFinder.Core.Tests;

/// <summary>A temporary directory deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "legofinder-tests", Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; temp files are harmless.
        }
    }
}

/// <summary>Records requests and replies from a queue of canned responses.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public FakeHttpHandler Reply(HttpStatusCode status, string body = "{}", Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    public FakeHttpHandler Reply(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

    public HttpClient Client() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        }

        return _responses.Dequeue()(request);
    }
}

/// <summary>Builds synthetic "tray" photos: coloured rectangles on a plain background.</summary>
internal static class TestImages
{
    public static readonly SKColor Background = SKColor.Parse("EEEEEA");

    public static SKBitmap Tray(int width, int height, params (SKRect Rect, string Hex)[] pieces) =>
        Tray(width, height, Background, pieces);

    public static SKBitmap Tray(int width, int height, SKColor background, params (SKRect Rect, string Hex)[] pieces)
    {
        var bitmap = new SKBitmap(ImageIo.Info(width, height));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background);
        using var paint = new SKPaint { IsAntialias = false };
        foreach (var (rect, hex) in pieces)
        {
            paint.Color = SKColor.Parse(hex);
            canvas.DrawRect(rect, paint);
        }

        canvas.Flush();
        return bitmap;
    }

    public static string SavePng(SKBitmap bitmap, string path)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    public static SKColor Pixel(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);
}

internal static class Repo
{
    /// <summary>The repository root (the folder containing LegoFinder.slnx).</summary>
    public static string Root { get; } = Find();

    public static string Demo(string file) => Path.Combine(Root, "samples", "demo", file);

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LegoFinder.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>Small builders for contract objects used across tests.</summary>
internal static class Make
{
    public static PartLine Line(int number, string part, string? rgb, int quantity = 1, int[]? brickLinkColours = null,
        Dictionary<string, IReadOnlyList<string>>? externalIds = null, bool transparent = false) => new()
    {
        Number = number,
        PartId = part,
        PartName = "Part " + part,
        Quantity = quantity,
        Colour = rgb is null && brickLinkColours is null
            ? null
            : new PartColour { Rgb = rgb, Name = "Colour " + rgb, BrickLinkIds = brickLinkColours ?? [], IsTransparent = transparent },
        ExternalIds = externalIds ?? new Dictionary<string, IReadOnlyList<string>>(),
    };

    public static PartsList Parts(params PartLine[] lines) => new()
    {
        Source = "Test",
        Kind = ModelKind.File,
        Id = "TEST-1",
        Name = "Test model",
        Lines = lines,
    };

    public static Detection Detection(string id, string? measured, params (string Part, double Score)[] candidates) => new()
    {
        Id = id,
        Box = new Box(0.1, 0.1, 0.1, 0.1),
        Colour = new DetectionColour { Measured = measured },
        Candidates = candidates.Select(c => new PartCandidate(c.Part, null, "BrickLink", c.Score)).ToList(),
        Recognition = RecognitionStatus.Ok,
    };

    public static ScannedPhoto Photo(string path, params Detection[] detections) => new()
    {
        Path = path,
        Sha256 = "00",
        Width = 100,
        Height = 100,
        Detections = detections,
    };
}
