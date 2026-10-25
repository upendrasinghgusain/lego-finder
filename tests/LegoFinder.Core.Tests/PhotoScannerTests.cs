using LegoFinder.Core.Colour;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Scanning;
using SkiaSharp;

namespace LegoFinder.Core.Tests;

public class PhotoScannerTests
{
    /// <summary>Answers "3001" for every crop and counts calls; can be told to fail.</summary>
    private sealed class FakeRecogniser(Exception? failWith = null) : IPartRecogniser
    {
        private int _calls;

        public int Calls => _calls;

        public string Name => "fake";

        public Task<Recognition> RecogniseAsync(byte[] jpeg, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            Assert.True(jpeg is [0xFF, 0xD8, ..], "crop should be a JPEG");
            return failWith is null
                ? Task.FromResult(new Recognition([new PartCandidate("3001", "Brick 2 x 4", "BrickLink", 0.9)], [new ColourCandidate("5", "Red", "BrickLink", 0.8)]))
                : Task.FromException<Recognition>(failWith);
        }
    }

    private static string TwoPieceTray(TempDir dir, string name = "tray.png")
    {
        using var tray = TestImages.Tray(400, 300,
            (new SKRect(20, 30, 100, 70), "C91A09"),
            (new SKRect(250, 20, 330, 60), "237841"));
        return TestImages.SavePng(tray, dir.File(name));
    }

    [Fact]
    public async Task Scans_and_recognises_every_piece()
    {
        using var dir = new TempDir();
        var path = TwoPieceTray(dir);
        var recogniser = new FakeRecogniser();
        var logs = new List<string>();

        var photo = await new PhotoScanner(recogniser, cache: null, log: logs.Add).ScanAsync(path, "photos/tray.png");

        Assert.Equal(("photos/tray.png", 400, 300, "EEEEEA"), (photo.Path, photo.Width, photo.Height, photo.Background));
        Assert.Equal(PhotoScanner.Sha256Of(path), photo.Sha256);
        Assert.Equal(["tray#1", "tray#2"], photo.Detections.Select(d => d.Id));
        Assert.All(photo.Detections, d =>
        {
            Assert.Equal(RecognitionStatus.Ok, d.Recognition);
            Assert.Equal("3001", Assert.Single(d.Candidates).PartId);
            Assert.Equal("5", Assert.Single(d.Colour.Predicted).Id);
            Assert.NotNull(d.Colour.Measured);
        });
        Assert.Equal(2, recogniser.Calls);
        Assert.Contains(logs, l => l.Contains("2 pieces, 2 to recognise"));
    }

    [Fact]
    public async Task Second_scan_of_an_unchanged_photo_uses_the_cache()
    {
        using var dir = new TempDir();
        var path = TwoPieceTray(dir);
        var cache = new RecognitionCache(dir.File("cache"));
        var recogniser = new FakeRecogniser();
        var scanner = new PhotoScanner(recogniser, cache);

        await scanner.ScanAsync(path);
        var again = await scanner.ScanAsync(path);

        Assert.Equal(2, recogniser.Calls);
        Assert.All(again.Detections, d => Assert.Equal(RecognitionStatus.Cached, d.Recognition));
        Assert.All(again.Detections, d => Assert.Equal("3001", d.Candidates[0].PartId));
    }

    [Fact]
    public async Task A_changed_photo_is_recognised_afresh()
    {
        using var dir = new TempDir();
        var path = TwoPieceTray(dir);
        var cache = new RecognitionCache(dir.File("cache"));
        var recogniser = new FakeRecogniser();
        var scanner = new PhotoScanner(recogniser, cache);
        await scanner.ScanAsync(path);

        // The child moved (and turned) the red brick; the green one stayed put.
        using (var moved = TestImages.Tray(400, 300, (new SKRect(150, 150, 190, 230), "C91A09"), (new SKRect(250, 20, 330, 60), "237841")))
        {
            TestImages.SavePng(moved, path);
        }

        var after = await scanner.ScanAsync(path);

        Assert.Equal(3, recogniser.Calls); // only the moved piece is new
        Assert.Contains(after.Detections, d => d.Recognition == RecognitionStatus.Ok);
        Assert.Contains(after.Detections, d => d.Recognition == RecognitionStatus.Cached);
    }

    [Fact]
    public async Task Colour_prefilter_skips_unwanted_pieces()
    {
        using var dir = new TempDir();
        var recogniser = new FakeRecogniser();
        var onlyRed = (Rgb c) => ColourMath.DeltaE(c, Rgb.Parse("C91A09")) < 20;

        var photo = await new PhotoScanner(recogniser, null).ScanAsync(TwoPieceTray(dir), shouldRecognise: onlyRed);

        Assert.Equal(1, recogniser.Calls);
        Assert.Equal([RecognitionStatus.Ok, RecognitionStatus.Skipped], photo.Detections.Select(d => d.Recognition));
        Assert.Empty(photo.Detections[1].Candidates);
    }

    [Fact]
    public async Task Recogniser_failures_mark_pieces_failed_without_stopping_the_scan()
    {
        using var dir = new TempDir();
        var logs = new List<string>();
        var scanner = new PhotoScanner(new FakeRecogniser(new HttpRequestException("boom")), null, log: logs.Add);

        var photo = await scanner.ScanAsync(TwoPieceTray(dir));

        Assert.All(photo.Detections, d => Assert.Equal(RecognitionStatus.Failed, d.Recognition));
        Assert.Contains(logs, l => l.Contains("boom"));
    }

    [Fact]
    public async Task Without_a_recogniser_only_segments()
    {
        using var dir = new TempDir();
        var scanner = new PhotoScanner(null, null);

        var photo = await scanner.ScanAsync(TwoPieceTray(dir));

        Assert.Equal("none", scanner.RecogniserName);
        Assert.Equal(2, photo.Detections.Count);
        Assert.All(photo.Detections, d => Assert.Equal(RecognitionStatus.None, d.Recognition));
    }

    [Fact]
    public async Task Large_pieces_are_downscaled_for_the_recogniser()
    {
        using var dir = new TempDir();
        using (var tray = TestImages.Tray(3000, 2000, (new SKRect(500, 500, 1700, 1300), "C91A09")))
        {
            TestImages.SavePng(tray, dir.File("big.png"));
        }

        var sizes = new List<(int, int)>();
        var recogniser = new SizeRecorder(sizes);
        await new PhotoScanner(recogniser, null, new ScanOptions { CropMaxSide = 256 }).ScanAsync(dir.File("big.png"));

        var (w, h) = Assert.Single(sizes);
        Assert.Equal(256, Math.Max(w, h));
    }

    private sealed class SizeRecorder(List<(int, int)> sizes) : IPartRecogniser
    {
        public string Name => "size";

        public Task<Recognition> RecogniseAsync(byte[] jpeg, CancellationToken ct = default)
        {
            using var bitmap = SKBitmap.Decode(jpeg);
            lock (sizes)
            {
                sizes.Add((bitmap.Width, bitmap.Height));
            }

            return Task.FromResult(new Recognition([], []));
        }
    }

    [Fact]
    public void Expands_folders_to_sorted_photos_and_skips_annotated_output()
    {
        using var dir = new TempDir();
        foreach (var name in new[] { "b.JPG", "a.png", "c.jpeg", "notes.txt", "a.annotated.jpg" })
        {
            File.WriteAllText(dir.File(name), "");
        }

        var files = PhotoScanner.ExpandPhotoPaths([dir.Path, dir.File("notes.txt")]);

        Assert.Equal(["a.png", "b.JPG", "c.jpeg", "notes.txt"], files.Select(Path.GetFileName));
    }

    [Fact]
    public void Missing_paths_are_reported()
    {
        Assert.Throws<FileNotFoundException>(() => PhotoScanner.ExpandPhotoPaths(["/definitely/not/here.jpg"]));
    }

    [Theory]
    [InlineData("x.JPG", true)]
    [InlineData("x.jpeg", true)]
    [InlineData("x.png", true)]
    [InlineData("x.gif", false)]
    [InlineData("x", false)]
    public void Recognises_photo_extensions(string path, bool expected) => Assert.Equal(expected, PhotoScanner.IsPhoto(path));
}
