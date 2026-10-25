using System.CommandLine;
using System.Text;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Matching;
using LegoFinder.Core.Parts;
using LegoFinder.Core.Pipeline;
using LegoFinder.Core.Rendering;
using LegoFinder.Core.Reporting;
using LegoFinder.Core.Scanning;

namespace LegoFinder.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;

    /// <summary><c>find</c> ran fine but not every parts line was found.</summary>
    public const int PartlyFound = 2;
}

/// <summary>The <c>legofinder</c> command tree. Each subcommand is one pipeline stage.</summary>
public static class LegoFinderCli
{
    public static Task<int> RunAsync(string[] args, CliContext ctx)
    {
        var root = new RootCommand("""
            Find a LEGO model's pieces in photos of your collection.

            Pipeline:  legofinder parts <url> | legofinder find --photos <dir> | legofinder report > report.html
            """);
        root.Subcommands.Add(PartsCommand(ctx));
        root.Subcommands.Add(ScanCommand(ctx));
        root.Subcommands.Add(FindCommand(ctx));
        root.Subcommands.Add(ReportCommand(ctx));

        return root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = ctx.Out, Error = ctx.Error });
    }

    // ---- parts ---------------------------------------------------------------------------------------------------

    private static Command PartsCommand(CliContext ctx)
    {
        var source = new Argument<string>("source")
        {
            Description = "Rebrickable MOC/set URL, MOC id (MOC-37396), set number (31058-1) or a Rebrickable CSV export",
        };
        var apiKey = new Option<string?>("--api-key") { Description = "Rebrickable API key (default: $REBRICKABLE_API_KEY)" };
        var catalogue = new Option<string?>("--catalogue")
        {
            Description = "Rebrickable database export (all_downloads.zip or its folder) for offline sets and filling in CSV details "
                + "(default: $LEGOFINDER_CATALOGUE, then data/rebrickable/all_downloads.zip)",
        };
        var spares = new Option<bool>("--include-spares") { Description = "Include spare parts" };
        var output = OutOption("Write the parts JSON here instead of stdout");

        var command = new Command("parts", "Get a model's parts list → legofinder.parts/1 JSON") { source, apiKey, catalogue, spares, output };
        command.SetAction((result, ct) => Guard(ctx, async () =>
        {
            var parsed = PartsSource.Parse(result.GetValue(source)!);
            var cataloguePath = result.GetValue(catalogue) ?? RebrickableCatalogue.FindDefault(ctx.GetEnvironmentVariable);
            using var db = cataloguePath is null ? null : RebrickableCatalogue.Open(cataloguePath);

            PartsList parts;
            if (parsed.Kind == PartsSourceKind.CsvFile)
            {
                using var reader = new StreamReader(result.GetValue(source)!.Trim());
                parts = RebrickableCsvImporter.Import(reader, parsed.Id, result.GetValue(spares));
            }
            else if (parsed.Kind == PartsSourceKind.RebrickableSet && db is not null)
            {
                ctx.Log($"reading set {parsed.Id} from {cataloguePath}");
                parts = db.GetSet(parsed.Id, result.GetValue(spares));
            }
            else
            {
                var key = result.GetValue(apiKey) ?? ctx.GetEnvironmentVariable("REBRICKABLE_API_KEY");
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ArgumentException(
                        "A Rebrickable API key is needed: set REBRICKABLE_API_KEY or pass --api-key (free at https://rebrickable.com/api/). "
                        + "Or export the parts list as CSV and pass the file instead"
                        + (parsed.Kind == PartsSourceKind.RebrickableSet ? ", or pass --catalogue with Rebrickable's all_downloads.zip." : "."));
                }

                parts = await new RebrickableClient(ctx.HttpClientFactory(), key).GetPartsAsync(parsed, result.GetValue(spares), ct);
            }

            if (db is not null && parts.Source != "RebrickableCatalogue")
            {
                parts = db.Enrich(parts);
                ctx.Log($"filled in names, pictures and BrickLink ids from {cataloguePath}");
            }

            var unknown = parts.Lines.Count(l => l.Colour is { Rgb: null });
            if (unknown > 0)
            {
                ctx.Log($"warning: {unknown} line(s) have a colour id not in the colour table.");
            }

            ctx.Log($"{parts.Name ?? parts.Id}: {parts.Lines.Count} lines, {parts.TotalQuantity} pieces");
            Write(ctx, result.GetValue(output), LegoJson.Serialize(parts));
            return ExitCodes.Ok;
        }));
        return command;
    }

    // ---- scan ----------------------------------------------------------------------------------------------------

    private static Command ScanCommand(CliContext ctx)
    {
        var photos = new Argument<string[]>("photos") { Description = "Photo files or folders", Arity = ArgumentArity.OneOrMore };
        var scanOptions = new ScannerOptions();
        var output = OutOption("Write the scan JSON here instead of stdout (photo paths are recorded relative to it)");
        var annotate = new Option<string?>("--annotate")
        {
            Description = "Also write each photo with every detection circled and numbered (solid = recognised) into this folder",
        };

        var command = new Command("scan", "Find and identify the pieces in photos → legofinder.scan/1 JSON") { photos, output, annotate };
        scanOptions.AddTo(command);
        command.SetAction((result, ct) => Guard(ctx, async () =>
        {
            var scanner = scanOptions.CreateScanner(result, ctx);
            var outPath = result.GetValue(output);
            var baseDir = outPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(outPath));

            var scanned = new List<ScannedPhoto>();
            foreach (var file in PhotoScanner.ExpandPhotoPaths(result.GetValue(photos)!))
            {
                var recorded = baseDir is null ? file : Path.GetRelativePath(baseDir, Path.GetFullPath(file));
                var photo = await scanner.ScanAsync(file, recorded.Replace('\\', '/'), null, ct);
                scanned.Add(photo);
                if (result.GetValue(annotate) is { } annotateDir)
                {
                    Directory.CreateDirectory(annotateDir);
                    var preview = Path.Combine(annotateDir, Path.GetFileNameWithoutExtension(file) + ".detections.jpg");
                    DetectionPreview.Write(file, photo, preview);
                    ctx.Log($"wrote {preview}");
                }
            }

            if (scanned.Count == 0)
            {
                throw new ArgumentException("No .jpg/.jpeg/.png photos found.");
            }

            Write(ctx, outPath, LegoJson.Serialize(new ScanResult { Recogniser = scanner.RecogniserName, Photos = scanned }));
            return ExitCodes.Ok;
        }));
        return command;
    }

    // ---- find ----------------------------------------------------------------------------------------------------

    private static Command FindCommand(CliContext ctx)
    {
        var input = InOption("Parts JSON to read instead of stdin");
        var photos = new Option<string[]>("--photos") { Description = "Photo files or folders to scan now", AllowMultipleArgumentsPerToken = true };
        var scan = new Option<string?>("--scan") { Description = "Reuse a scan JSON (changed photos are rescanned)" };
        var outDir = new Option<string>("--out-dir") { Description = "Where annotated images go", DefaultValueFactory = _ => "out" };
        var matchColour = new Option<bool>("--match-colour") { Description = "Require pieces to be the parts list's colour (default: any colour of the right part)" };
        var recogniseAll = new Option<bool>("--recognise-all") { Description = "With --match-colour: recognise every piece, not just ones in a needed colour" };
        var prefilter = new Option<double>("--prefilter-distance") { Description = "With --match-colour: ΔE for the colour pre-filter", DefaultValueFactory = _ => 35 };
        var confident = new Option<double>("--confident-score") { Description = "Part score for a confident match", DefaultValueFactory = _ => 0.55 };
        var colourDistance = new Option<double>("--max-colour-distance") { Description = "With --match-colour: max ΔE between measured and catalogue colour", DefaultValueFactory = _ => 25 };
        var scanOptions = new ScannerOptions();
        var output = OutOption("Write the find JSON here instead of stdout");

        var command = new Command("find", "Match a parts list (stdin) against photos, draw numbered circles → legofinder.find/1 JSON")
        {
            input, photos, scan, outDir, matchColour, recogniseAll, prefilter, confident, colourDistance, output,
        };
        scanOptions.AddTo(command);
        command.SetAction((result, ct) => Guard(ctx, async () =>
        {
            var parts = LegoJson.Deserialize<PartsList>(ReadInput(ctx, result.GetValue(input), "parts JSON"), Schemas.Parts);

            ScanResult? existing = null;
            string? scanDir = null;
            if (result.GetValue(scan) is { } scanPath)
            {
                existing = LegoJson.Deserialize<ScanResult>(File.ReadAllText(scanPath), Schemas.Scan);
                scanDir = Path.GetDirectoryName(Path.GetFullPath(scanPath));
            }

            var runner = new FindRunner(scanOptions.CreateScanner(result, ctx), ctx.Log);
            var found = await runner.RunAsync(parts, result.GetValue(photos) ?? [], existing, scanDir, new FindOptions
            {
                OutDir = result.GetValue(outDir)!,
                RecogniseAll = result.GetValue(recogniseAll),
                PrefilterDistance = result.GetValue(prefilter),
                Match = new MatchOptions
                {
                    MatchColour = result.GetValue(matchColour),
                    ConfidentScore = result.GetValue(confident),
                    MaxColourDistance = result.GetValue(colourDistance),
                },
            }, ct);

            var s = found.Summary;
            ctx.Log($"found {s.Found} of {s.Lines} lines fully, {s.PartlyFound} partly, {s.Uncertain} uncertain, {s.NotFound} not found "
                + $"({s.PiecesFound}/{s.PiecesNeeded} pieces); {found.Images.Count} annotated image(s) in {result.GetValue(outDir)}");
            Write(ctx, result.GetValue(output), LegoJson.Serialize(found));
            return s.Found == s.Lines ? ExitCodes.Ok : ExitCodes.PartlyFound;
        }));
        return command;
    }

    // ---- report --------------------------------------------------------------------------------------------------

    private static Command ReportCommand(CliContext ctx)
    {
        var input = InOption("Find JSON to read instead of stdin");
        var output = OutOption("Write the HTML here instead of stdout");

        var command = new Command("report", "Turn a find result (stdin) into a self-contained HTML page") { input, output };
        command.SetAction((result, _) => Guard(ctx, () =>
        {
            var inPath = result.GetValue(input);
            var found = LegoJson.Deserialize<FindResult>(ReadInput(ctx, inPath, "find JSON"), Schemas.Find);
            var inDir = inPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(inPath));

            var html = HtmlReport.Render(found, path =>
            {
                // Annotated paths are relative to where find ran; fall back to the input file's folder.
                foreach (var candidate in new[] { path, inDir is null ? null : Path.Combine(inDir, path) })
                {
                    if (candidate is not null && File.Exists(candidate))
                    {
                        return File.ReadAllBytes(candidate);
                    }
                }

                ctx.Log($"warning: annotated image not found: {path}");
                return null;
            });
            Write(ctx, result.GetValue(output), html);
            return Task.FromResult(ExitCodes.Ok);
        }));
        return command;
    }

    // ---- shared --------------------------------------------------------------------------------------------------

    /// <summary>Options shared by <c>scan</c> and <c>find</c> for building a <see cref="PhotoScanner"/>.</summary>
    private sealed class ScannerOptions
    {
        private readonly Option<string> _recogniser = new("--recogniser")
        {
            Description = "brickognize (online, default) or none (segmentation only)",
            DefaultValueFactory = _ => "brickognize",
        };

        private readonly Option<string?> _cacheDir = new("--cache-dir") { Description = "Recognition cache folder" };
        private readonly Option<bool> _noCache = new("--no-cache") { Description = "Don't read or write the recognition cache" };
        private readonly Option<int> _concurrency = new("--concurrency") { Description = "Parallel recogniser calls", DefaultValueFactory = _ => 4 };
        private readonly Option<double> _threshold = new("--threshold") { Description = "Foreground ΔE from background", DefaultValueFactory = _ => 18 };
        private readonly Option<double> _minArea = new("--min-area") { Description = "Smallest piece, as a fraction of the photo", DefaultValueFactory = _ => 0.0003 };

        public ScannerOptions()
        {
            _recogniser.AcceptOnlyFromAmong("brickognize", "none");
        }

        public void AddTo(Command command)
        {
            foreach (var option in new Option[] { _recogniser, _cacheDir, _noCache, _concurrency, _threshold, _minArea })
            {
                command.Options.Add(option);
            }
        }

        public PhotoScanner CreateScanner(ParseResult result, CliContext ctx)
        {
            IPartRecogniser? recogniser = result.GetValue(_recogniser) == "none"
                ? null
                : new BrickognizeRecogniser(ctx.HttpClientFactory());
            var cache = result.GetValue(_noCache) || recogniser is null
                ? null
                : new RecognitionCache(result.GetValue(_cacheDir) ?? RecognitionCache.DefaultDirectory);

            return new PhotoScanner(recogniser, cache, new ScanOptions
            {
                Concurrency = result.GetValue(_concurrency),
                Segmenter = new SegmenterOptions
                {
                    ForegroundThreshold = result.GetValue(_threshold),
                    MinAreaFraction = result.GetValue(_minArea),
                },
            }, ctx.Log);
        }
    }

    private static Option<string?> InOption(string description) => new("--in", "-i") { Description = description };

    private static Option<string?> OutOption(string description) => new("--out", "-o") { Description = description };

    private static string ReadInput(CliContext ctx, string? path, string what)
    {
        if (path is not null)
        {
            return File.ReadAllText(path);
        }

        if (!ctx.InputRedirected)
        {
            throw new ArgumentException($"No {what} on stdin. Pipe it in or pass --in <file>.");
        }

        return ctx.In.ReadToEnd();
    }

    private static void Write(CliContext ctx, string? path, string content)
    {
        if (path is null)
        {
            ctx.Out.Write(content);
            ctx.Out.WriteLine();
            ctx.Out.Flush();
            return;
        }

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir)
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content + Environment.NewLine, new UTF8Encoding(false));
    }

    /// <summary>Turns failures into a one-line message on stderr and exit code 1, keeping stdout clean.</summary>
    private static async Task<int> Guard(CliContext ctx, Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            ctx.Log("cancelled");
            return ExitCodes.Error;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException
            or HttpRequestException or UnauthorizedAccessException or KeyNotFoundException
            or System.Text.Json.JsonException)
        {
            ctx.Log("error: " + ex.Message);
            return ExitCodes.Error;
        }
    }
}
