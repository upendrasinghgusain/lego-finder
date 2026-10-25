# LEGO Finder MVP1: A Command-Line Pipeline

**Version:** 1.1. This replaces v1.0, which described a single web app.
**Status:** Implemented. See [README.md](../README.md) for how to run it.
**Supersedes for now:** [spec.md](spec.md) (the full vision; it stays the long-term target)
**Input:** A URL or id for a LEGO model's parts list (a Rebrickable MOC or set), or a parts-list file
**Output:** Annotated copies of the collection photos, with every required piece circled and numbered. Each number is the line number in the model's parts list.

---

## 1. What changed

| Area | spec.md | MVP1 v1.0 | MVP1 v1.1 (this document) |
| --- | --- | --- | --- |
| Shape | Angular PWA + API | One web app | **Unix-style pipeline of small commands** |
| Input | YouTube URL via Android share | MOC URL in a web form | MOC/set URL or id on the command line |
| Photos | Uploaded tray photo | Static photo folder, indexed once | **Scanned on every run**; photos are expected to change |
| Output | Annotated image in the app | Annotated images + web legend | Annotated JPEGs + a JSON result; an optional HTML report |

### Why scan on every run

Today the photos are static. Later they will come from an automated camera pointed at a child's play area, so pieces will have moved between any two runs. So the design treats every photo as possibly new:

- **Segmentation** (finding where the pieces are) runs locally on every photo, every time. It is cheap.
- **Recognition** (asking an API what each piece is) is the slow, rate-limited step. It is cached by the **content hash of each piece's crop**. An unchanged photo costs nothing on the second run, and a new photo is recognised from scratch.
- With colour matching on (`--match-colour`), `find` knows which colours the model needs, so it **skips recognition for pieces whose measured colour matches none of them**. On a mixed-colour collection this removes most API calls. Colour matching is off by default (§5.2), so this pre-filter is opt-in.
- A scan file records each photo's SHA-256. If a photo has changed since the scan was written, `find` notices, warns, and rescans that photo, so stale positions are never drawn.

---

## 2. The pipeline

```text
legofinder parts <url|id|file.csv>        → parts JSON  (stdout)
legofinder scan  <photos…>                → scan JSON   (stdout)
legofinder find  --photos <photos…>       ← parts JSON  (stdin)
                 [--scan scan.json]       → find JSON   (stdout) + annotated JPEGs (--out-dir)
legofinder report                         ← find JSON   (stdin)
                                          → HTML        (stdout)
```

Typical use:

```sh
legofinder parts https://rebrickable.com/mocs/MOC-37396/Schwimpy/31058-velociraptooth/ \
  | legofinder find --photos photos/ --out-dir out/ \
  | legofinder report > out/report.html
```

`scan` is optional. `find` scans by itself when it isn't given `--scan`. `scan` exists to inspect, cache, or hand-correct what the scanner sees (§5.4).

### 2.1 Rules every command follows

- **stdout carries only data**: one JSON document, or HTML for `report`. Logs, progress and warnings go to **stderr**.
- Every command that reads data accepts `--in <file>` and otherwise reads stdin. Every command that writes data accepts `-o/--out <file>` and otherwise writes to stdout. **Piping is optional**, and every pipeline can be run as separate steps with files in between.
- All text is UTF-8 without a BOM. *Windows note:* Windows PowerShell 5.1 re-encodes text piped between programs. Use PowerShell 7, Git Bash or cmd for pipes, or use `--in`/`--out`.
- Every JSON document has a `schema` field (`legofinder.parts/1`, `legofinder.scan/1`, `legofinder.find/1`). A command rejects input with the wrong schema rather than guessing.
- **Exit codes:**

| Code | Meaning |
| --- | --- |
| `0` | Success. For `find`, every parts line was `Found`. |
| `1` | Error: bad input, network failure, etc. Nothing useful is on stdout. |
| `2` | `find` only: ran successfully, but some lines were not fully found. The output is still complete and valid. |

---

## 3. `legofinder parts`: the parts list

### 3.1 Sources

| Input | Example | How it is read |
| --- | --- | --- |
| Rebrickable MOC URL | `https://rebrickable.com/mocs/MOC-37396/Schwimpy/31058-velociraptooth/` | API `GET /api/v3/lego/mocs/{id}/parts/` |
| MOC id | `MOC-37396` | same |
| Rebrickable set URL | `https://rebrickable.com/sets/31058-1/mighty-dinosaurs/` | API `GET /api/v3/lego/sets/{id}/` + `/parts/` |
| Set number | `31058-1` or `31058` (`-1` is added) | same |
| Rebrickable CSV export | `velociraptooth.csv` (columns `Part,Color,Quantity[,Is Spare]`) | Local file. Colour names and RGB come from Rebrickable's colour table, embedded in the tool. |

- The API needs a free Rebrickable API key, passed with `--api-key` or the `REBRICKABLE_API_KEY` environment variable. The CSV route needs no key.
- **Offline catalogue (optional, recommended).** Rebrickable's database export (`all_downloads.zip` from rebrickable.com/downloads) is read straight from the zip. It is found via `--catalogue`, then `$LEGOFINDER_CATALOGUE`, then `data/rebrickable/all_downloads.zip`, and has two uses:
  - **Sets** are read from it with no key or network, using the latest inventory version and leaving out minifigs. For example, 31058-1 gives 67 lines and 174 pieces in about 4 s.
  - Every other parts list is **enriched** without overwriting anything: part names, pictures (`elements.csv`), BrickLink part ids (`part_mappings.csv`) and BrickLink colour ids (`color_mappings.csv`). The BrickLink part ids are needed because recognition returns BrickLink numbers. Of the export's roughly 62k BrickLink mappings, about 52k differ from the Rebrickable number. Most of those are printed parts and stickers, but they include common parts such as `6141`→`4073` and `11153`→`61678`.
  - It contains **no MOC inventories**, so MOCs still need the API or a CSV export.
- Only `rebrickable.com` URLs are accepted. The tool never fetches an arbitrary host (SSRF safety).
- Verified 2026-10-25 with a real key: set details, set parts and `lego/sets/{set}/alternates/` work (31058-1 has 475 alternates, including MOC-37396 Velociraptooth). **The MOC parts endpoint returns 403 "You do not have permission"**, so MOC parts lists come from the CSV export. MOCs have no details endpoint, so the MOC's name and designer are taken from the URL slug.
- Keys and settings can live in a gitignored `.env` file (see `.env.example`). Real environment variables take priority.
- Spare parts are excluded unless `--include-spares` is passed. Minifig sub-parts are not included.
- Lines are numbered **1..N in source order**. That number is what appears in the circles. Rebrickable inventories carry **no build-step order**, so "which order" means list order only. Build-step order is a future feature that would need the instructions themselves.
- **Hand-written parts files are a first-class input.** Anything matching the schema below can be piped into `find`.

### 3.2 `legofinder.parts/1`

```json
{
  "schema": "legofinder.parts/1",
  "source": "Rebrickable",
  "kind": "Moc",
  "id": "MOC-37396",
  "name": "31058 Velociraptooth",
  "designer": "Schwimpy",
  "sourceUrl": "https://rebrickable.com/mocs/MOC-37396/Schwimpy/31058-velociraptooth/",
  "totalQuantity": 45,
  "lines": [
    {
      "number": 1,
      "partId": "3001",
      "partName": "Brick 2 x 4",
      "colour": { "id": 4, "name": "Red", "rgb": "C91A09", "isTransparent": false, "brickLinkIds": [5] },
      "quantity": 3,
      "imageUrl": "https://cdn.rebrickable.com/media/parts/elements/300121.jpg",
      "catalogueUrl": "https://rebrickable.com/parts/3001/",
      "externalIds": { "BrickLink": ["3001"] }
    }
  ]
}
```

The values above are illustrative and are not the real MOC-37396 inventory.

---

## 4. `legofinder scan`: what is in the photos

### 4.1 Photo assumptions (MVP)

- JPEG or PNG. EXIF orientation is applied.
- A single layer of pieces on a plain background that contrasts with them. Pieces may touch slightly, but not pile up.
- Fairly even lighting. A near-neutral (white or grey) background enables automatic white balance.

### 4.2 Pipeline per photo

1. **Normalise.** Apply EXIF orientation and downscale to at most 1600 px on the long side for analysis. Coordinates are stored normalised, so this doesn't affect output.
2. **Background.** Take the per-channel median of a border band of pixels.
3. **Foreground mask.** Mark each pixel whose CIELAB distance from the background is above a threshold (default 18).
4. **Pieces.** Close small gaps (a 1-pixel dilation), label connected components (8-connected), and drop components smaller than 0.03% or larger than 25% of the image.
5. **Colour.** Take the per-channel median of the piece's own mask pixels. If the background is near-neutral, white-balance against it.
6. **Recognise.** Crop the piece from the full-resolution photo with 15% padding, downscale it to at most 512 px, and send it to the recogniser. The result is cached by the SHA-256 of the crop's pixels.

The recogniser sits behind `IPartRecogniser`:

| `--recogniser` | Behaviour |
| --- | --- |
| `brickognize` (default) | `POST https://api.brickognize.com/predict/parts/?predict_color=true&top_k_items=5&top_k_colors=3&min_similarity_items=0.3` with the crop as `query_image`. Verified 2026-10-25: no key is needed, and it returns **BrickLink** part ids and **BrickLink** colour ids with scores. The minimum similarity is lowered from the API default of 0.5 so that weak candidates can become dashed "maybe" circles. |
| `none` | Segmentation only, with no candidates. Useful for checking segmentation, and as a starting point for hand-labelling. |

Calls run with bounded concurrency (`--concurrency`, default 4) and are retried on HTTP 429/5xx. When one crop's recognition fails, that piece is marked `Failed` and the scan carries on.

### 4.3 `legofinder.scan/1`

```json
{
  "schema": "legofinder.scan/1",
  "recogniser": "brickognize",
  "photos": [
    {
      "path": "photos/tray-01.jpg",
      "sha256": "…",
      "width": 4032,
      "height": 3024,
      "background": "EEEEEA",
      "detections": [
        {
          "id": "tray-01#17",
          "box": { "x": 0.412, "y": 0.288, "w": 0.051, "h": 0.034 },
          "areaFraction": 0.0011,
          "colour": {
            "measured": "C3241A",
            "predicted": [ { "id": "5", "name": "Red", "system": "BrickLink", "score": 0.73 } ]
          },
          "candidates": [
            { "partId": "3001", "name": "Brick 2 x 4", "system": "BrickLink", "score": 0.65 }
          ],
          "recognition": "Ok"
        }
      ]
    }
  ]
}
```

- `box` is normalised (0–1) to the **orientation-corrected original photo**.
- `recognition` is one of `Ok`, `Cached`, `Skipped` (filtered out by the colour pre-filter), `Failed` or `None`.
- `scan --annotate <dir>` also writes each photo with every detection circled and numbered by its id, for checking segmentation and as a guide for hand-labelling.
- Relative `path`s are resolved against the scan file's directory.

### 4.4 Hand-made scans

A scan file is plain JSON, so it can be written or corrected by hand. For example, run `scan --recogniser none` and then fill in the `candidates`. This gives a ground truth for evaluation and lets the matching and drawing steps be shown working regardless of recognition quality. `samples/demo/` contains one.

---

## 5. `legofinder find`: match and draw

### 5.1 Inputs

- The parts list, from stdin or `--in`.
- Photos, from `--photos <files or folders>` (scanned now), **or** `--scan <scan.json>` (already scanned), or both. When a scan file is given, each photo's current SHA-256 is checked against it. A photo that has changed is rescanned when the file still exists, with a warning either way.
- `--out-dir` (default `out`) for the annotated images.

With `--match-colour`, `find` skips recognition for pieces whose measured colour is further than `--prefilter-distance` (default 35 ΔE) from every needed colour. Pass `--recognise-all` to turn this off. Without `--match-colour` every piece is recognised.

### 5.2 Part and colour compatibility

A detection is compatible with a parts line when both of these hold:

- **Part.** One of its top 3 candidates has an id equal to the line's `partId` or to any of its `externalIds`, compared case-insensitively. Mould-variant suffixes are tolerated, so `3069b` ≈ `3069`.
- **Colour.** This is checked **only with `--match-colour`**, which is off by default: for a child building a model, a blue 2×4 brick is an acceptable stand-in for a red one. With colour matching off, any colour of the right part matches and the evidence is `Ignored`, but a piece in the right colour is still **preferred** when there's a choice. With colour matching on, the colour needs one of the following:
  - A predicted colour (score ≥ 0.2) in the line colour's `brickLinkIds`. Its evidence is `Predicted`.
  - Otherwise, the measured colour is within `--max-colour-distance` (default 25 ΔE, CIE76) of the line's RGB. Its evidence is `Measured`.
  - Otherwise, if the line has no colour information at all, the colour evidence is `Unknown`.

### 5.3 Assignment

1. List every compatible (line, detection) pair.
2. Sort them by how confident they are, then right colour before wrong colour, then by part score descending, then by colour distance ascending.
   - **Confident** means part score ≥ `--confident-score` (default 0.55) and colour evidence that isn't `Unknown`. Evidence is never `Unknown` when colour matching is off.
3. Walk the pairs greedily. Each detection is assigned to at most **one** line, and each line receives at most `quantity` detections.
4. Give each line a status:

| Status | Meaning |
| --- | --- |
| `Found` | `quantity` confident matches |
| `PartlyFound` | at least 1 confident match, but fewer than `quantity` |
| `Uncertain` | only non-confident matches |
| `NotFound` | no compatible detection in any photo |

Part score and colour distance are reported separately and never merged. No match is ever forced.

### 5.4 Annotated images

- One new JPEG per photo that has at least one match: `<out-dir>/<photo>.annotated.jpg`. The original is never modified.
- Each matched piece gets a ring around its box, in a colour that cycles by line number, plus a badge showing the **line number** in large white digits on a dark disc.
- A solid ring means a confident match; a **dashed** ring means an uncertain one.
- Several copies of a line show the same number, so three red 2×4 bricks show three "1" circles.
- The digits use a built-in bitmap font, so rendering is deterministic and doesn't depend on installed fonts.

### 5.5 `legofinder.find/1`

```json
{
  "schema": "legofinder.find/1",
  "parts": { "source": "Rebrickable", "kind": "Moc", "id": "MOC-37396", "name": "…", "designer": "…", "sourceUrl": "…" },
  "summary": { "lines": 20, "found": 12, "partlyFound": 3, "uncertain": 1, "notFound": 4, "piecesNeeded": 45, "piecesFound": 31 },
  "images": [ { "photo": "photos/tray-01.jpg", "annotated": "out/tray-01.annotated.jpg", "circles": 31 } ],
  "lines": [
    {
      "number": 1, "partId": "3001", "partName": "Brick 2 x 4", "colourName": "Red", "colourRgb": "C91A09",
      "quantity": 3, "found": 2, "status": "PartlyFound",
      "catalogueUrl": "https://rebrickable.com/parts/3001/", "imageUrl": "…",
      "matches": [
        { "photo": "photos/tray-01.jpg", "detectionId": "tray-01#17", "box": { "x": 0.41, "y": 0.29, "w": 0.05, "h": 0.03 },
          "partScore": 0.65, "colourEvidence": "Predicted", "colourDistance": 6.2, "confident": true }
      ]
    }
  ],
  "warnings": []
}
```

---

## 6. `legofinder report`

This reads a find result and writes one self-contained HTML page, with the images embedded. The page shows:

- The summary.
- The annotated images.
- A legend: number, part thumbnail, name, colour swatch, found/needed, status, and a link to the catalogue page.

---

## 7. Code structure

```text
LegoFinder.slnx
src/LegoFinder.Core/        all logic; no console I/O
  Contracts/                JSON records for the three schemas + serializer options
  Imaging/                  SkiaSharp decode (with EXIF orientation), resample, JPEG encode
  Parts/                    source parsing, Rebrickable API client, CSV import, colour table
  Colour/                   RGB ↔ CIELAB, ΔE
  Scanning/                 segmenter, recognisers (Brickognize, none), recognition cache, photo scanner
  Matching/                 PartMatcher
  Rendering/                AnnotationRenderer, bitmap digit font
  Reporting/                HTML report
src/LegoFinder.Cli/         System.CommandLine host: one exe, subcommands, stdin/stdout/exit codes
tests/LegoFinder.Core.Tests/
tests/LegoFinder.Cli.Tests/ end-to-end runs of the commands in-process against sample data
samples/demo/               synthetic tray photo + hand-made scan + parts list, works offline
```

Stack: C# on .NET 10 (the current LTS), System.CommandLine 2, **SkiaSharp** for decoding, drawing and encoding, and xUnit v3. There is no database or web server.

*Why not ImageSharp:* its patched releases (4.1.2+) need a Six Labors licence key for Release builds, and every older release has open high-severity advisories. SkiaSharp is MIT-licensed and ships native binaries for Windows, macOS and Linux.

---

## 8. Evaluation and acceptance

### Test set

- The reference model is **MOC-37396 Velociraptooth**, an alternate build of set 31058.
- Use 1–3 real photos of the collection containing most of its parts plus some look-alike decoys, and one hand-made scan as ground truth.

### Measures

| Measure | Question |
| --- | --- |
| Segmentation recall | What fraction of the real pieces got their own box? |
| Recognition top-1 / top-3 | Is the correct part id first, or in the top 3? |
| Colour accuracy | Is the colour evidence right? |
| Match precision | What fraction of the drawn circles are on a correct piece? (This matters most: a wrong circle sends the child to the wrong piece.) |
| Line coverage | What fraction of lines are `Found` or `PartlyFound`? |
| Scan time | How many seconds does a fresh photo take, with and without the colour pre-filter? |

### MVP1 is done when

1. `parts` produces a correct numbered parts list for Velociraptooth, via the API or the CSV.
2. `parts | find | report` runs end to end, and each stage also runs on its own with files in between.
3. With the hand-made scan, every circle is on the right piece. This is covered by an automated test on `samples/demo`.
4. With automatic scanning of real photos, the measures above are recorded. The target is match precision ≥ 80%. Falling short of it is an acceptable, documented finding.
5. Pieces that weren't found are listed, never hidden.
6. A changed photo is detected and rescanned rather than drawn from stale positions.

---

### 8.1 Findings from the first real photos (2026-10-25)

Two phone photos (3024×4032) of about 100–150 pieces each, in a clear plastic storage tray on blue-grey fabric:

- **Segmentation: fails on this setup.** Photos 1 and 2 gave 42 and 46 detections for roughly 150 and 100 pieces. The background is not plain: the tray's clear plastic lets the textured fabric show through, so the single border-colour estimate is wrong for the tray interior. The tray's walls and dividers are detected as pieces, and touching pieces merge. This breaks the §4.1 assumptions, as expected.
- **Recognition: promising.** On the pieces that did segment cleanly, Brickognize's top-1 guess looked right in nearly every case that could be checked by eye. For example, 3710 Plate 1×4 scored 0.89, 93061 skeleton arm 0.90, 3022 Plate 2×2 0.81, and 47905 1×1 brick with studs 0.81. Recognising 88 crops took about 14 s.
- **Colour predictions: unreliable.** For example, dark red was called "Reddish Brown". This supports making colour matching optional and off by default.

Next steps:

1. For the MVP, photograph pieces on a plain, single-colour surface (no tray), not touching.
2. For the camera use case, replace `PieceSegmenter` with a learned detector or segmenter that copes with trays, texture and touching pieces. Recognition and everything downstream can stay as they are.

## 9. Deferred

These are deferred to later versions, which follow [spec.md](spec.md):

- YouTube input and model resolution.
- Android share, a PWA, and a web UI. A web UI could call `LegoFinder.Core` directly.
- Automated camera capture. The scanning design (§1) is ready for it, but driving a camera is not in scope.
- Build-step ordering.
- Overlapping or piled pieces, and a learned detector to replace colour segmentation. §8.1 shows this is needed for the real use case.
- Re-identifying a piece that has moved without re-recognising it, for example by matching crop embeddings across photos.
