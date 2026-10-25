# LegoFinder.Core

All of LEGO Finder's logic lives here. This library does no console I/O: the CLI in `src/LegoFinder.Cli` only parses arguments, reads stdin, writes stdout and sets exit codes. Something else, such as a web UI or a camera service, can call this library directly.

## Data flow

```text
PartsSource.Parse ─► RebrickableClient / RebrickableCsvImporter ─► PartsList          (parts)
                                                                       │
ImageIo.LoadOriented ─► PieceSegmenter ─► crops ─► IPartRecogniser ─► ScannedPhoto     (scan)
                                         (RecognitionCache)              │
                                                                       ▼
                     FindRunner: ColourPrefilter → PhotoScanner → PartMatcher → AnnotationRenderer   (find)
                                                                       │
                                                                 HtmlReport            (report)
```

| Folder | What's there |
| --- | --- |
| `Contracts/` | The three pipeline documents (`PartsList`, `ScanResult`, `FindResult`) and `LegoJson`, which every stage uses so the JSON always agrees. `LegoJson.Deserialize` rejects documents with the wrong `schema`. |
| `Parts/` | `PartsSource` turns a URL, id or file path into a source. `RebrickableClient` calls API v3: it only talks to `rebrickable.com`, follows paging and retries on 429. `RebrickableCsvImporter` reads CSV exports. `RebrickableCatalogue` reads Rebrickable's full database export (`all_downloads.zip`) for offline sets and to fill in names, pictures and BrickLink ids. `ColourTable` holds Rebrickable's colours, embedded. |
| `Colour/` | `Rgb`, CIELAB conversion and CIE76 ΔE. |
| `Imaging/` | `ImageIo`, the only SkiaSharp entry point for decoding: EXIF orientation, resampling, JPEG encoding, RGBA bytes. |
| `Scanning/` | `PieceSegmenter`, the classic-vision detector (background median → ΔE mask → dilation → 8-connected components → median colour with white balance). `IPartRecogniser`, with `BrickognizeRecogniser` as its implementation. `RecognitionCache`. `PhotoScanner`, which ties them together. |
| `Matching/` | `PartMatcher`: greedy one-to-one assignment of detections to parts lines, with explicit statuses. |
| `Rendering/` | `AnnotationRenderer` (rings and badges) and `DigitFont`, a built-in 5×7 bitmap font, so output never depends on installed fonts. |
| `Pipeline/` | `FindRunner`, which orchestrates `find`: stale-photo detection, colour pre-filter, matching, writing images. |
| `Reporting/` | `HtmlReport`: a self-contained page with escaped text and http(s)-only links. |

## Key decisions

- **Photos are assumed to change between runs.** Segmentation is cheap and runs every time. Recognition is cached by a hash of each crop's pixels rather than by photo, so pieces that haven't moved cost nothing and moved pieces are recognised afresh.
- **The colour pre-filter** (`ColourPrefilter`) skips recognition for pieces in colours the model doesn't use. This is the main lever for keeping rescans of a camera feed fast.
- **Scores are never merged.** Part score, colour evidence (`Predicted`, `Measured` or `Unknown`) and colour distance are reported separately, and confidence is decided per match.
- **No forced matches.** If a line can't be matched it is reported as `NotFound`. Ambiguous matches are kept, but drawn dashed.

## Extending

- **A different recogniser**, such as a local model or another API: implement `IPartRecogniser` and return candidates tagged with the id `System` they use. `PartMatcher` compares against each line's `PartId` and every `ExternalIds` entry, so BrickLink, Rebrickable or LEGO ids all work.
- **A better detector:** replace `PieceSegmenter.Segment` with something that returns `SegmentedPiece` boxes, for example a learned detector for cluttered or piled pieces. Everything downstream stays the same.
- **Another parts source:** produce a `PartsList` (and add a `PartsSourceKind` if the CLI should accept it).

## Dependencies

SkiaSharp (MIT) handles decoding, drawing and encoding. ImageSharp was considered, but its patched versions (4.1.2+) require a Six Labors licence key for Release builds, and the older versions carry known high-severity advisories.
