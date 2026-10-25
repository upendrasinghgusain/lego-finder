#:project ../../src/LegoFinder.Core/LegoFinder.Core.csproj
#:property TreatWarningsAsErrors=false
#:property PublishAot=false

// Regenerates the offline demo in this folder:
//   tray.png   – a synthetic top-down photo of pieces on a plain background
//   parts.json – a hand-written parts list (legofinder.parts/1)
//   scan.json  – a hand-made scan (legofinder.scan/1): real segmentation of tray.png, with candidates filled in
//                from the known layout instead of an online recogniser
//
// Run from the repo root:  dotnet run samples/demo/make-demo.cs

using LegoFinder.Core.Contracts;
using LegoFinder.Core.Scanning;
using SkiaSharp;

var dir = Path.GetDirectoryName(Path.GetFullPath("samples/demo/make-demo.cs"))!;
const int U = 36; // pixels per stud

// part, name, colour (Rebrickable id, BrickLink id, name, rgb), studs wide × high, top-left, recogniser score, round?
Piece[] layout =
[
    new("3001", "Brick 2 x 4", Red, 4, 2, 100, 100, 0.91),
    new("3001", "Brick 2 x 4", Red, 2, 4, 900, 560, 0.88),
    new("3001", "Brick 2 x 4", Red, 4, 2, 1120, 110, 0.86), // one more than needed
    new("3003", "Brick 2 x 2", Green, 2, 2, 400, 120, 0.90),
    new("3020", "Plate 2 x 4", Yellow, 4, 2, 620, 100, 0.84),
    new("3004", "Brick 1 x 2", Blue, 2, 1, 120, 420, 0.89),
    new("3004", "Brick 1 x 2", Blue, 1, 2, 330, 400, 0.87),
    new("3023", "Plate 1 x 2", Black, 2, 1, 520, 430, 0.83),
    new("3062b", "Brick Round 1 x 1", Lime, 1, 1, 760, 440, 0.42, Round: true), // weak recognition → dashed circle
    new("3003", "Brick 2 x 2", Red, 2, 2, 1040, 400, 0.90),                     // decoy: right colour, wrong part
    new("3001", "Brick 2 x 4", Blue, 4, 2, 150, 650, 0.90),                     // decoy: right part, wrong colour
    new("3710", "Plate 1 x 4", Grey, 4, 1, 500, 700, 0.85),                     // decoy: not in the model
];

using (var bitmap = new SKBitmap(new SKImageInfo(1400, 900, SKColorType.Rgba8888, SKAlphaType.Premul)))
using (var canvas = new SKCanvas(bitmap))
{
    using (var bg = new SKPaint())
    {
        // Gentle lighting gradient so the background isn't perfectly flat.
        bg.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 900),
            [SKColor.Parse("F0F0EC"), SKColor.Parse("E6E6E1")], SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, 1400, 900, bg);
    }

    foreach (var p in layout)
    {
        DrawPiece(canvas, p);
    }

    canvas.Flush();
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(dir, "tray.png"), data.ToArray());
}

var parts = new PartsList
{
    Source = "Hand-written",
    Kind = ModelKind.File,
    Id = "DEMO-1",
    Name = "Demo mini model",
    Lines =
    [
        Line(1, "3001", "Brick 2 x 4", Red, 2),
        Line(2, "3003", "Brick 2 x 2", Green, 1),
        Line(3, "3020", "Plate 2 x 4", Yellow, 1),
        Line(4, "3004", "Brick 1 x 2", Blue, 2),
        Line(5, "3023", "Plate 1 x 2", Black, 1),
        Line(6, "3022", "Plate 2 x 2", Orange, 1), // not in the photo
        Line(7, "3062b", "Brick Round 1 x 1", Lime, 1),
    ],
};
File.WriteAllText(Path.Combine(dir, "parts.json"), LegoJson.Serialize(parts) + "\n");

// Real segmentation, then fill in what a perfect-ish recogniser would say for each piece.
var scanner = new PhotoScanner(recogniser: null, cache: null);
var photo = await scanner.ScanAsync(Path.Combine(dir, "tray.png"), "tray.png");
var detections = photo.Detections.Select(d =>
{
    var cx = d.Box.CentreX * photo.Width;
    var cy = d.Box.CentreY * photo.Height;
    var p = layout.Single(p => cx >= p.X && cx <= p.X + p.W * U && cy >= p.Y && cy <= p.Y + p.H * U);
    return d with
    {
        Candidates =
        [
            new PartCandidate(p.Part, p.Name, "BrickLink", p.Score),
            new PartCandidate(p.Part + "pr9999", p.Name + " with print", "BrickLink", Math.Round(p.Score * 0.6, 2)),
        ],
        Colour = d.Colour with { Predicted = [new ColourCandidate(p.Colour.BrickLinkId.ToString(), p.Colour.Name, "BrickLink", 0.8)] },
        Recognition = RecognitionStatus.Ok,
    };
}).ToList();

if (detections.Count != layout.Length)
{
    throw new InvalidOperationException($"Segmentation found {detections.Count} pieces, expected {layout.Length}.");
}

var scan = new ScanResult { Recogniser = "hand-made", Photos = [photo with { Detections = detections }] };
File.WriteAllText(Path.Combine(dir, "scan.json"), LegoJson.Serialize(scan) + "\n");
Console.WriteLine($"Wrote tray.png, parts.json, scan.json ({detections.Count} pieces) to {dir}");

static void DrawPiece(SKCanvas canvas, Piece p)
{
    var colour = SKColor.Parse(p.Colour.Rgb);
    var rect = new SKRect(p.X, p.Y, p.X + p.W * U, p.Y + p.H * U);
    using var body = new SKPaint { IsAntialias = true, Color = colour };
    using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = Shade(colour, 0.7f) };
    using var stud = new SKPaint { IsAntialias = true, Color = Shade(colour, 1.12f) };
    using var studEdge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = Shade(colour, 0.8f) };

    if (p.Round)
    {
        canvas.DrawCircle(rect.MidX, rect.MidY, U / 2f, body);
        canvas.DrawCircle(rect.MidX, rect.MidY, U / 2f, edge);
    }
    else
    {
        canvas.DrawRoundRect(rect, 3, 3, body);
        canvas.DrawRoundRect(rect, 3, 3, edge);
    }

    for (var sx = 0; sx < p.W; sx++)
    {
        for (var sy = 0; sy < p.H; sy++)
        {
            var c = new SKPoint(p.X + (sx + 0.5f) * U, p.Y + (sy + 0.5f) * U);
            canvas.DrawCircle(c, U * 0.3f, stud);
            canvas.DrawCircle(c, U * 0.3f, studEdge);
        }
    }
}

static SKColor Shade(SKColor c, float f) => new(
    (byte)Math.Clamp(c.Red * f, 0, 255), (byte)Math.Clamp(c.Green * f, 0, 255), (byte)Math.Clamp(c.Blue * f, 0, 255));

static PartLine Line(int number, string part, string name, Col c, int qty) => new()
{
    Number = number,
    PartId = part,
    PartName = name,
    Quantity = qty,
    Colour = new PartColour { Id = c.RebrickableId, Name = c.Name, Rgb = c.Rgb, BrickLinkIds = [c.BrickLinkId] },
    CatalogueUrl = $"https://rebrickable.com/parts/{part}/",
    ExternalIds = new Dictionary<string, IReadOnlyList<string>> { ["BrickLink"] = [part] },
};

record Col(int RebrickableId, int BrickLinkId, string Name, string Rgb);

record Piece(string Part, string Name, Col Colour, int W, int H, int X, int Y, double Score, bool Round = false);

static partial class Program
{
    static readonly Col Red = new(4, 5, "Red", "C91A09");
    static readonly Col Green = new(2, 6, "Green", "237841");
    static readonly Col Yellow = new(14, 3, "Yellow", "F2CD37");
    static readonly Col Blue = new(1, 7, "Blue", "0055BF");
    static readonly Col Black = new(0, 11, "Black", "05131D");
    static readonly Col Orange = new(25, 4, "Orange", "FE8A18");
    static readonly Col Lime = new(27, 34, "Lime", "BBE90B");
    static readonly Col Grey = new(71, 86, "Light Bluish Gray", "A0A5A9");
}
