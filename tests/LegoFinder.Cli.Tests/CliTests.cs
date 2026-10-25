using System.Net;
using System.Text.Json;
using LegoFinder.Cli;

namespace LegoFinder.Cli.Tests;

/// <summary>Runs <c>legofinder</c> commands in-process with captured stdin/stdout/stderr.</summary>
public class CliTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "legofinder-cli-tests", Guid.NewGuid().ToString("N"));

    public CliTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed record Run(int Exit, string Out, string Err);

    private static async Task<Run> Cli(string[] args, string? stdin = null, Func<HttpClient>? http = null, Dictionary<string, string>? env = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var ctx = new CliContext
        {
            In = new StringReader(stdin ?? ""),
            Out = output,
            Error = error,
            InputRedirected = stdin is not null,
            GetEnvironmentVariable = name => env?.GetValueOrDefault(name),
            HttpClientFactory = http ?? (() => throw new InvalidOperationException("No network in tests")),
        };

        var exit = await LegoFinderCli.RunAsync(args, ctx);
        return new Run(exit, output.ToString(), error.ToString());
    }

    private static string Demo(string file) => Path.Combine(RepoRoot(), "samples", "demo", file);

    private static string RepoRoot()
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

    private string Csv()
    {
        var path = Path.Combine(_temp, "model.csv");
        File.WriteAllText(path, "Part,Color,Quantity\n3001,4,2\n3003,2,1\n3022,25,1\n");
        return path;
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public async Task Help_lists_the_pipeline_commands()
    {
        var run = await Cli(["--help"]);

        Assert.Equal(0, run.Exit);
        foreach (var command in new[] { "parts", "scan", "find", "report" })
        {
            Assert.Contains(command, run.Out);
        }
    }

    [Fact]
    public async Task Unknown_commands_fail()
    {
        var run = await Cli(["frobnicate"]);
        Assert.NotEqual(0, run.Exit);
    }

    [Fact]
    public async Task Parts_from_csv_writes_json_to_stdout_and_logs_to_stderr()
    {
        var run = await Cli(["parts", Csv()]);

        Assert.Equal(0, run.Exit);
        var json = Json(run.Out);
        Assert.Equal("legofinder.parts/1", json.GetProperty("schema").GetString());
        Assert.Equal(3, json.GetProperty("lines").GetArrayLength());
        Assert.Equal("Red", json.GetProperty("lines")[0].GetProperty("colour").GetProperty("name").GetString());
        Assert.Contains("3 lines, 4 pieces", run.Err);
    }

    [Fact]
    public async Task Parts_warns_about_unknown_colour_ids()
    {
        var path = Path.Combine(_temp, "odd.csv");
        File.WriteAllText(path, "Part,Color,Quantity\n3001,987654,1\n");

        var run = await Cli(["parts", path]);

        Assert.Equal(0, run.Exit);
        Assert.Contains("not in the colour table", run.Err);
    }

    [Fact]
    public async Task Parts_can_write_to_a_file()
    {
        var outPath = Path.Combine(_temp, "sub", "parts.json");

        var run = await Cli(["parts", Csv(), "-o", outPath]);

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Out);
        Assert.Equal("legofinder.parts/1", Json(File.ReadAllText(outPath)).GetProperty("schema").GetString());
    }

    private string MiniCatalogue()
    {
        var folder = Path.Combine(_temp, "db");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "sets.csv"), "set_num,name,year,theme_id,num_parts,img_url\n31058-1,Mighty Dinosaurs,2017,672,3,x\n");
        File.WriteAllText(Path.Combine(folder, "inventories.csv"), "id,version,set_num\n7,1,31058-1\n");
        File.WriteAllText(Path.Combine(folder, "inventory_parts.csv"), "inventory_id,part_num,color_id,quantity,is_spare,img_url\n7,3001,4,2,False,\n7,6141,0,1,False,\n");
        File.WriteAllText(Path.Combine(folder, "parts.csv"), "part_num,name,part_cat_id,part_material\n3001,Brick 2 x 4,11,Plastic\n6141,Plate Round 1 x 1,21,Plastic\n");
        File.WriteAllText(Path.Combine(folder, "part_mappings.csv"), "part_num,ext_system,ext_id,is_export_id,is_import_id\n6141,BL,4073,True,True\n");
        File.WriteAllText(Path.Combine(folder, "color_mappings.csv"), "id,ext_system,ext_id,ext_descr,is_export_id,is_import_id\n4,BL,5,x,True,True\n");
        File.WriteAllText(Path.Combine(folder, "elements.csv"), "element_id,part_num,color_id,design_id\n");
        return folder;
    }

    [Fact]
    public async Task Parts_reads_sets_offline_from_the_catalogue()
    {
        var run = await Cli(["parts", "31058", "--catalogue", MiniCatalogue()]);

        Assert.Equal(0, run.Exit);
        var json = Json(run.Out);
        Assert.Equal("RebrickableCatalogue", json.GetProperty("source").GetString());
        Assert.Equal("Mighty Dinosaurs", json.GetProperty("name").GetString());
        Assert.Equal("4073", json.GetProperty("lines")[1].GetProperty("externalIds").GetProperty("BrickLink")[0].GetString());
        Assert.Contains("reading set 31058-1", run.Err);
    }

    [Fact]
    public async Task Parts_fills_in_csv_details_from_the_catalogue_found_via_the_environment()
    {
        var run = await Cli(["parts", Csv()], env: new() { ["LEGOFINDER_CATALOGUE"] = MiniCatalogue() });

        Assert.Equal(0, run.Exit);
        var first = Json(run.Out).GetProperty("lines")[0];
        Assert.Equal("Brick 2 x 4", first.GetProperty("partName").GetString());
        Assert.Equal(5, first.GetProperty("colour").GetProperty("brickLinkIds")[0].GetInt32());
        Assert.Contains("filled in names", run.Err);
    }

    [Fact]
    public async Task Parts_for_a_set_without_key_or_catalogue_suggests_the_catalogue()
    {
        var run = await Cli(["parts", "31058-1"]);

        Assert.Equal(1, run.Exit);
        Assert.Contains("--catalogue", run.Err);
    }

    [Fact]
    public async Task Parts_reports_a_missing_catalogue()
    {
        var run = await Cli(["parts", Csv(), "--catalogue", Path.Combine(_temp, "nope.zip")]);

        Assert.Equal(1, run.Exit);
        Assert.Contains("catalogue not found", run.Err);
    }

    [Fact]
    public async Task Parts_from_url_needs_an_api_key()
    {
        var run = await Cli(["parts", "https://rebrickable.com/mocs/MOC-37396/Schwimpy/31058-velociraptooth/"]);

        Assert.Equal(1, run.Exit);
        Assert.Equal("", run.Out);
        Assert.Contains("error: A Rebrickable API key is needed", run.Err);
    }

    [Fact]
    public async Task Parts_from_url_uses_the_environment_key()
    {
        var handler = new FakeHandler("""
            { "next": null, "results": [ { "quantity": 2, "is_spare": false,
              "part": { "part_num": "3001", "name": "Brick 2 x 4" },
              "color": { "id": 4, "name": "Red", "rgb": "C91A09", "is_trans": false } } ] }
            """);

        var run = await Cli(["parts", "MOC-37396"], http: () => new HttpClient(handler),
            env: new() { ["REBRICKABLE_API_KEY"] = "env-key" });

        Assert.Equal(0, run.Exit);
        Assert.Equal("key env-key", handler.LastAuthorization);
        Assert.Equal("3001", Json(run.Out).GetProperty("lines")[0].GetProperty("partId").GetString());
    }

    [Fact]
    public async Task Parts_reports_http_failures_cleanly()
    {
        var run = await Cli(["parts", "MOC-1", "--api-key", "bad"], http: () => new HttpClient(new FakeHandler("{}", HttpStatusCode.Unauthorized)));

        Assert.Equal(1, run.Exit);
        Assert.Contains("rejected the API key", run.Err);
        Assert.Equal("", run.Out);
    }

    [Theory]
    [InlineData("https://example.com/x", "Only rebrickable.com")]
    [InlineData("nonsense", "Can't tell")]
    [InlineData("missing.csv", "CSV file not found")]
    public async Task Parts_rejects_bad_sources(string source, string message)
    {
        var run = await Cli(["parts", source]);

        Assert.Equal(1, run.Exit);
        Assert.Contains(message, run.Err);
    }

    [Fact]
    public async Task Scan_without_recogniser_segments_the_demo_photo()
    {
        var run = await Cli(["scan", Demo("tray.png"), "--recogniser", "none"]);

        Assert.Equal(0, run.Exit);
        var json = Json(run.Out);
        Assert.Equal("none", json.GetProperty("recogniser").GetString());
        Assert.Equal(12, json.GetProperty("photos")[0].GetProperty("detections").GetArrayLength());
    }

    [Fact]
    public async Task Scan_to_a_file_records_paths_relative_to_it()
    {
        var photos = Path.Combine(_temp, "photos");
        Directory.CreateDirectory(photos);
        File.Copy(Demo("tray.png"), Path.Combine(photos, "tray.png"));
        var outPath = Path.Combine(_temp, "scans", "scan.json");

        var run = await Cli(["scan", photos, "--recogniser", "none", "-o", outPath]);

        Assert.Equal(0, run.Exit);
        Assert.Equal("../photos/tray.png", Json(File.ReadAllText(outPath)).GetProperty("photos")[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Scan_can_draw_every_detection_for_checking()
    {
        var previews = Path.Combine(_temp, "previews");

        var run = await Cli(["scan", Demo("tray.png"), "--recogniser", "none", "--annotate", previews]);

        Assert.Equal(0, run.Exit);
        var preview = Path.Combine(previews, "tray.detections.jpg");
        Assert.True(File.Exists(preview));
        Assert.Contains("wrote", run.Err);
        Assert.Equal(0xFF, File.ReadAllBytes(preview)[0]);
    }

    [Fact]
    public async Task Find_with_match_colour_rejects_wrong_colours()
    {
        // A parts list wanting a *green* 2×4: the demo photo only has red and blue 2×4s.
        const string parts = """
            { "schema": "legofinder.parts/1", "source": "t", "kind": "File", "id": "t",
              "lines": [ { "number": 1, "partId": "3001", "quantity": 1,
                           "colour": { "name": "Green", "rgb": "237841", "brickLinkIds": [6] } } ] }
            """;
        string[] args = ["find", "--scan", Demo("scan.json"), "--out-dir", Path.Combine(_temp, "out"), "--recogniser", "none"];

        var anyColour = await Cli(args, stdin: parts);
        var strict = await Cli([.. args, "--match-colour"], stdin: parts);

        Assert.Equal(ExitCodes.Ok, anyColour.Exit);
        Assert.Equal("Ignored", Json(anyColour.Out).GetProperty("lines")[0].GetProperty("matches")[0].GetProperty("colourEvidence").GetString());
        Assert.Equal(ExitCodes.PartlyFound, strict.Exit);
        Assert.Equal("NotFound", Json(strict.Out).GetProperty("lines")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Scan_of_an_empty_folder_fails()
    {
        var run = await Cli(["scan", _temp, "--recogniser", "none"]);

        Assert.Equal(1, run.Exit);
        Assert.Contains("No .jpg/.jpeg/.png photos", run.Err);
    }

    [Fact]
    public async Task Scan_rejects_unknown_recognisers()
    {
        var run = await Cli(["scan", Demo("tray.png"), "--recogniser", "magic"]);
        Assert.NotEqual(0, run.Exit);
    }

    [Fact]
    public async Task Find_reads_parts_from_stdin_and_exits_2_when_not_everything_is_found()
    {
        var outDir = Path.Combine(_temp, "out");

        var run = await Cli(["find", "--scan", Demo("scan.json"), "--out-dir", outDir, "--recogniser", "none"],
            stdin: File.ReadAllText(Demo("parts.json")));

        Assert.Equal(ExitCodes.PartlyFound, run.Exit);
        var json = Json(run.Out);
        Assert.Equal("legofinder.find/1", json.GetProperty("schema").GetString());
        Assert.Equal(5, json.GetProperty("summary").GetProperty("found").GetInt32());
        Assert.True(File.Exists(Path.Combine(outDir, "tray.annotated.jpg")));
        Assert.Contains("found 5 of 7 lines fully", run.Err);
    }

    [Fact]
    public async Task Find_exits_0_when_everything_is_found()
    {
        var parts = File.ReadAllText(Demo("parts.json"));
        using var doc = JsonDocument.Parse(parts);
        // Keep only line 1 (two red 2×4 bricks, both in the photo).
        var onlyFirst = $$"""
            { "schema": "legofinder.parts/1", "source": "t", "kind": "File", "id": "t",
              "lines": [ {{doc.RootElement.GetProperty("lines")[0].GetRawText()}} ] }
            """;

        var run = await Cli(["find", "--scan", Demo("scan.json"), "--out-dir", Path.Combine(_temp, "out"), "--recogniser", "none"], stdin: onlyFirst);

        Assert.Equal(ExitCodes.Ok, run.Exit);
    }

    [Fact]
    public async Task Find_without_input_explains_how_to_provide_it()
    {
        var run = await Cli(["find", "--scan", Demo("scan.json")]);

        Assert.Equal(1, run.Exit);
        Assert.Contains("No parts JSON on stdin", run.Err);
    }

    [Fact]
    public async Task Find_rejects_the_wrong_document_type()
    {
        var run = await Cli(["find", "--scan", Demo("scan.json"), "--recogniser", "none"], stdin: File.ReadAllText(Demo("scan.json")));

        Assert.Equal(1, run.Exit);
        Assert.Contains("Expected a 'legofinder.parts/1' document but got 'legofinder.scan/1'", run.Err);
        Assert.Equal("", run.Out);
    }

    [Fact]
    public async Task Find_without_photos_fails()
    {
        var run = await Cli(["find", "--in", Demo("parts.json"), "--recogniser", "none"]);

        Assert.Equal(1, run.Exit);
        Assert.Contains("No photos to search", run.Err);
    }

    [Fact]
    public async Task Report_turns_find_output_into_html()
    {
        var outDir = Path.Combine(_temp, "out");
        var findJson = Path.Combine(_temp, "find.json");
        var find = await Cli(["find", "--in", Demo("parts.json"), "--scan", Demo("scan.json"), "--out-dir", outDir, "--recogniser", "none", "-o", findJson]);
        Assert.Equal(ExitCodes.PartlyFound, find.Exit);

        var report = await Cli(["report", "--in", findJson]);

        Assert.Equal(0, report.Exit);
        Assert.StartsWith("<!doctype html>", report.Out);
        Assert.Contains("data:image/jpeg;base64,", report.Out);
        Assert.Contains("Demo mini model", report.Out);
    }

    [Fact]
    public async Task Report_warns_when_an_image_is_missing()
    {
        const string json = """
            { "schema": "legofinder.find/1",
              "parts": { "source": "t", "kind": "File", "id": "t" },
              "summary": { "lines": 0, "found": 0, "partlyFound": 0, "uncertain": 0, "notFound": 0, "piecesNeeded": 0, "piecesFound": 0 },
              "images": [ { "photo": "p.jpg", "annotated": "nowhere/p.annotated.jpg", "circles": 1 } ],
              "lines": [] }
            """;
        var htmlPath = Path.Combine(_temp, "report.html");

        var run = await Cli(["report", "-o", htmlPath], stdin: json);

        Assert.Equal(0, run.Exit);
        Assert.Contains("annotated image not found", run.Err);
        Assert.Contains("Image not available", File.ReadAllText(htmlPath));
    }

    [Fact]
    public async Task Whole_pipeline_chains_through_stdout()
    {
        var outDir = Path.Combine(_temp, "out");

        var parts = await Cli(["parts", Csv()]);
        var find = await Cli(["find", "--scan", Demo("scan.json"), "--out-dir", outDir, "--recogniser", "none"], stdin: parts.Out);
        var report = await Cli(["report"], stdin: find.Out);

        Assert.Equal((0, ExitCodes.PartlyFound, 0), (parts.Exit, find.Exit, report.Exit));
        var summary = Json(find.Out).GetProperty("summary");
        // CSV has red 2×4 ×2 (found), green 2×2 (found), orange 2×2 plate (absent); colours come from the measured pixels.
        Assert.Equal(2, summary.GetProperty("found").GetInt32());
        Assert.Equal(1, summary.GetProperty("notFound").GetInt32());
        Assert.Contains("Found <strong>2</strong> of 3 part types", report.Out);
    }

    private sealed class FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
