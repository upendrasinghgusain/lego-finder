using LegoFinder.Core.Contracts;
using LegoFinder.Core.Pipeline;
using LegoFinder.Core.Scanning;
using SkiaSharp;

namespace LegoFinder.Core.Tests;

/// <summary>End-to-end matching and drawing on the hand-made demo in samples/demo.</summary>
public class FindRunnerTests
{
    private static PartsList DemoParts() => LegoJson.Deserialize<PartsList>(File.ReadAllText(Repo.Demo("parts.json")), Schemas.Parts);

    private static ScanResult DemoScan() => LegoJson.Deserialize<ScanResult>(File.ReadAllText(Repo.Demo("scan.json")), Schemas.Scan);

    /// <summary>Copies the demo photo next to a scan file so tests can change it safely.</summary>
    private static string CopyDemo(TempDir dir)
    {
        File.Copy(Repo.Demo("tray.png"), dir.File("tray.png"));
        return dir.Path;
    }

    [Fact]
    public async Task Demo_finds_the_expected_lines_and_draws_one_image()
    {
        using var dir = new TempDir();
        var logs = new List<string>();
        var runner = new FindRunner(new PhotoScanner(null, null), logs.Add);

        var result = await runner.RunAsync(DemoParts(), [], DemoScan(), Path.Combine(Repo.Root, "samples", "demo"),
            new FindOptions { OutDir = dir.File("out") });

        Assert.Equal(
            [LineStatus.Found, LineStatus.Found, LineStatus.Found, LineStatus.Found, LineStatus.Found, LineStatus.NotFound, LineStatus.Uncertain],
            result.Lines.Select(l => l.Status));
        Assert.Equal(new FindSummary(7, 5, 0, 1, 1, 9, 7), result.Summary);
        Assert.Empty(result.Warnings);
        Assert.Empty(logs);

        // Two of the three red 2×4s; never the red 2×2 or blue 2×4 decoys.
        var reds = result.Lines[0].Matches.Select(m => m.DetectionId).ToList();
        Assert.Equal(2, reds.Count);
        var scan = DemoScan().Photos[0].Detections.ToDictionary(d => d.Id);
        Assert.All(result.Lines.SelectMany(l => l.Matches), m => Assert.Equal(
            result.Lines.Single(l => l.Matches.Contains(m)).PartId,
            scan[m.DetectionId].Candidates[0].PartId));

        var image = Assert.Single(result.Images);
        Assert.Equal(("tray.png", 8), (image.Photo, image.Circles));
        Assert.EndsWith("out/tray.annotated.jpg", image.Annotated);
        using var annotated = SKBitmap.Decode(image.Annotated);
        Assert.Equal((1400, 900), (annotated.Width, annotated.Height));
        Assert.Equal(ModelSummary.From(DemoParts()), result.Parts);
    }

    [Fact]
    public async Task A_changed_photo_is_rescanned_with_a_warning()
    {
        using var dir = new TempDir();
        var scanDir = CopyDemo(dir);
        using (var moved = TestImages.Tray(1400, 900, (new SKRect(100, 100, 244, 172), "C91A09")))
        {
            TestImages.SavePng(moved, dir.File("tray.png"));
        }

        var logs = new List<string>();
        var result = await new FindRunner(new PhotoScanner(null, null), logs.Add)
            .RunAsync(DemoParts(), [], DemoScan(), scanDir, new FindOptions { OutDir = dir.File("out") });

        Assert.Contains(result.Warnings, w => w.Contains("changed since it was scanned"));
        Assert.Contains(logs, l => l.StartsWith("warning:"));
        // The rescan had no recogniser, so nothing can be matched any more: no stale circles.
        Assert.All(result.Lines, l => Assert.Equal(LineStatus.NotFound, l.Status));
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task A_missing_photo_is_still_matched_but_not_drawn()
    {
        using var dir = new TempDir();

        var result = await new FindRunner(new PhotoScanner(null, null))
            .RunAsync(DemoParts(), [], DemoScan(), dir.Path, new FindOptions { OutDir = dir.File("out") });

        Assert.Contains(result.Warnings, w => w.Contains("not found"));
        Assert.Equal(5, result.Summary.Found);
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task Photos_given_both_ways_are_only_used_once()
    {
        using var dir = new TempDir();
        var scanDir = CopyDemo(dir);

        var result = await new FindRunner(new PhotoScanner(null, null))
            .RunAsync(DemoParts(), [dir.Path], DemoScan(), scanDir, new FindOptions { OutDir = dir.File("out") });

        Assert.Single(result.Images);
        Assert.Equal(5, result.Summary.Found);
    }

    [Fact]
    public async Task Scans_photos_directly_and_names_outputs_uniquely()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("a"));
        Directory.CreateDirectory(dir.File("b"));
        File.Copy(Repo.Demo("tray.png"), dir.File("a/tray.png"));
        File.Copy(Repo.Demo("tray.png"), dir.File("b/tray.png"));
        var recogniser = new EverythingIs("3001", "5");

        var result = await new FindRunner(new PhotoScanner(recogniser, null))
            .RunAsync(DemoParts(), [dir.File("a"), dir.File("b/tray.png")], null, null, new FindOptions { OutDir = dir.File("out") });

        Assert.Equal(["tray.annotated.jpg", "tray-2.annotated.jpg"], result.Images.Select(i => Path.GetFileName(i.Annotated)));
        Assert.Equal(24, recogniser.Calls); // colour matching is off, so every piece is recognised
        Assert.Equal(LineStatus.Found, result.Lines[0].Status);
    }

    [Fact]
    public async Task With_colour_matching_only_pieces_in_needed_colours_are_recognised()
    {
        using var dir = new TempDir();
        var recogniser = new EverythingIs("3001", "5");

        var result = await new FindRunner(new PhotoScanner(recogniser, null)).RunAsync(DemoParts(), [Repo.Demo("tray.png")], null, null,
            new FindOptions { OutDir = dir.File("out"), Match = new Matching.MatchOptions { MatchColour = true } });

        // The grey 1×4 is the only piece in a colour the demo model doesn't use.
        Assert.Equal(11, recogniser.Calls);
        // Every piece "is" a 3001 with predicted colour Red, so only line 1 (red 2×4 ×2) matches.
        Assert.Equal(LineStatus.Found, result.Lines[0].Status);
        Assert.All(result.Lines.Skip(1), l => Assert.Equal(LineStatus.NotFound, l.Status));
    }

    [Fact]
    public async Task Recognise_all_disables_the_colour_prefilter()
    {
        using var dir = new TempDir();
        var recogniser = new EverythingIs("3001", "5");

        await new FindRunner(new PhotoScanner(recogniser, null))
            .RunAsync(DemoParts(), [Repo.Demo("tray.png")], null, null,
                new FindOptions { OutDir = dir.File("out"), RecogniseAll = true, Match = new Matching.MatchOptions { MatchColour = true } });

        Assert.Equal(12, recogniser.Calls);
    }

    [Fact]
    public async Task No_photos_is_an_error()
    {
        using var dir = new TempDir();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new FindRunner(new PhotoScanner(null, null)).RunAsync(DemoParts(), [], null, null, new FindOptions { OutDir = dir.Path }));
    }

    private sealed class EverythingIs(string part, string colour) : IPartRecogniser
    {
        private int _calls;

        public int Calls => _calls;

        public string Name => "everything";

        public Task<Recognition> RecogniseAsync(byte[] jpeg, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new Recognition(
                [new PartCandidate(part, null, "BrickLink", 0.9)], [new ColourCandidate(colour, null, "BrickLink", 0.9)]));
        }
    }
}
